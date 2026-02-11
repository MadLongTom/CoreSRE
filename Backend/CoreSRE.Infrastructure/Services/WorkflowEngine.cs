using System.Text.Json;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// 工作流执行引擎。
/// 将 DAG 图转换为顺序 / 并行 / 条件分支执行流程，逐节点更新状态并持久化。
/// US1: 顺序执行（拓扑排序 → 逐节点执行）
/// </summary>
public class WorkflowEngine : IWorkflowEngine
{
    private readonly IAgentResolver _agentResolver;
    private readonly IToolInvokerFactory _toolInvokerFactory;
    private readonly IToolRegistrationRepository _toolRepo;
    private readonly IWorkflowExecutionRepository _executionRepo;
    private readonly IConditionEvaluator _conditionEvaluator;
    private readonly ILogger<WorkflowEngine> _logger;

    /// <summary>节点执行超时（FR-022）</summary>
    private static readonly TimeSpan NodeTimeout = TimeSpan.FromMinutes(5);

    public WorkflowEngine(
        IAgentResolver agentResolver,
        IToolInvokerFactory toolInvokerFactory,
        IToolRegistrationRepository toolRepo,
        IWorkflowExecutionRepository executionRepo,
        IConditionEvaluator conditionEvaluator,
        ILogger<WorkflowEngine> logger)
    {
        _agentResolver = agentResolver;
        _toolInvokerFactory = toolInvokerFactory;
        _toolRepo = toolRepo;
        _executionRepo = executionRepo;
        _conditionEvaluator = conditionEvaluator;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(WorkflowExecution execution, CancellationToken cancellationToken)
    {
        _logger.LogInformation("开始执行工作流 {ExecutionId}, WorkflowDefinitionId={WorkflowDefinitionId}",
            execution.Id, execution.WorkflowDefinitionId);

        execution.Start();
        await _executionRepo.UpdateAsync(execution, cancellationToken);

        try
        {
            var sortedNodes = TopologicalSort(execution.GraphSnapshot);

            // 跟踪每个节点的输出（供 FanIn 聚合使用）
            var nodeOutputs = new Dictionary<string, string?>();
            var processedNodes = new HashSet<string>();

            // 预计算 FanOut 下游分组
            var fanOutDownstream = new Dictionary<string, List<WorkflowNodeVO>>();
            var parallelNodeIds = new HashSet<string>();
            var nodeMap = execution.GraphSnapshot.Nodes.ToDictionary(n => n.NodeId);

            foreach (var fn in sortedNodes.Where(n => n.NodeType == WorkflowNodeType.FanOut))
            {
                var downstream = execution.GraphSnapshot.Edges
                    .Where(e => e.SourceNodeId == fn.NodeId)
                    .Select(e => nodeMap[e.TargetNodeId])
                    .ToList();
                fanOutDownstream[fn.NodeId] = downstream;
                foreach (var d in downstream)
                    parallelNodeIds.Add(d.NodeId);
            }

            string? lastOutput = execution.Input.GetRawText();

            foreach (var node in sortedNodes)
            {
                if (processedNodes.Contains(node.NodeId))
                    continue;

                cancellationToken.ThrowIfCancellationRequested();

                if (node.NodeType == WorkflowNodeType.FanOut)
                {
                    // FanOut: 透传 + 并行执行下游节点
                    await ExecuteFanOutGroupAsync(
                        execution, node, lastOutput, fanOutDownstream,
                        nodeOutputs, processedNodes, cancellationToken);

                    if (execution.Status == ExecutionStatus.Failed)
                        return; // 所有并行分支均失败
                }
                else if (node.NodeType == WorkflowNodeType.FanIn)
                {
                    // FanIn: 聚合上游节点输出为 JSON 数组
                    lastOutput = await ExecuteFanInAsync(
                        execution, node, nodeOutputs, processedNodes, cancellationToken);
                }
                else if (node.NodeType == WorkflowNodeType.Condition)
                {
                    // Condition: 评估条件边并路由到匹配分支
                    var continueExecution = await ExecuteConditionNodeAsync(
                        execution, node, lastOutput, nodeOutputs, processedNodes, cancellationToken);

                    if (!continueExecution)
                        return; // 条件评估失败或无匹配分支
                }
                else
                {
                    // 顺序节点执行
                    _logger.LogInformation("执行节点 {NodeId} (类型: {NodeType})", node.NodeId, node.NodeType);

                    execution.StartNode(node.NodeId);
                    await _executionRepo.UpdateAsync(execution, cancellationToken);

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(NodeTimeout);

                    try
                    {
                        lastOutput = await ExecuteNodeAsync(execution, node, lastOutput, cts.Token);
                        nodeOutputs[node.NodeId] = lastOutput;
                        execution.CompleteNode(node.NodeId, lastOutput);
                        await _executionRepo.UpdateAsync(execution, cancellationToken);
                        processedNodes.Add(node.NodeId);

                        _logger.LogInformation("节点 {NodeId} 执行完成", node.NodeId);
                    }
                    catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        var timeoutMsg = $"节点执行超时（{NodeTimeout.TotalMinutes} 分钟）";
                        _logger.LogWarning("节点 {NodeId} {Error}", node.NodeId, timeoutMsg);
                        execution.FailNode(node.NodeId, timeoutMsg);
                        await _executionRepo.UpdateAsync(execution, cancellationToken);

                        execution.Fail($"节点 {node.NodeId} 执行超时");
                        await _executionRepo.UpdateAsync(execution, cancellationToken);
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "节点 {NodeId} 执行失败", node.NodeId);
                        execution.FailNode(node.NodeId, ex.Message);
                        await _executionRepo.UpdateAsync(execution, cancellationToken);

                        execution.Fail($"节点 {node.NodeId} 执行失败: {ex.Message}");
                        await _executionRepo.UpdateAsync(execution, cancellationToken);
                        return;
                    }
                }
            }

            // 全部节点执行完成
            JsonElement finalOutput;
            if (lastOutput is not null)
            {
                try
                {
                    finalOutput = JsonDocument.Parse(lastOutput).RootElement;
                }
                catch (JsonException)
                {
                    // 非 JSON 输出包装为 {"output": "..."}
                    finalOutput = JsonDocument.Parse(
                        JsonSerializer.Serialize(new { output = lastOutput })).RootElement;
                }
            }
            else
            {
                finalOutput = JsonDocument.Parse("{}").RootElement;
            }
            execution.Complete(finalOutput);
            await _executionRepo.UpdateAsync(execution, cancellationToken);

            _logger.LogInformation("工作流 {ExecutionId} 执行完成", execution.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("工作流 {ExecutionId} 被取消", execution.Id);
            if (execution.Status == ExecutionStatus.Running)
            {
                execution.Fail("工作流执行被取消");
                await _executionRepo.UpdateAsync(execution, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "工作流 {ExecutionId} 执行异常", execution.Id);
            if (execution.Status == ExecutionStatus.Running)
            {
                execution.Fail(ex.Message);
                await _executionRepo.UpdateAsync(execution, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 执行单个节点。根据节点类型分派到不同的执行逻辑。
    /// </summary>
    private async Task<string?> ExecuteNodeAsync(
        WorkflowExecution execution,
        WorkflowNodeVO node,
        string? input,
        CancellationToken cancellationToken)
    {
        return node.NodeType switch
        {
            WorkflowNodeType.Agent => await ExecuteAgentNodeAsync(execution, node, input, cancellationToken),
            WorkflowNodeType.Tool => await ExecuteToolNodeAsync(node, input, cancellationToken),
            _ => input // Condition, FanOut, FanIn — 顺序执行时直接透传（US2/US3 扩展）
        };
    }

    /// <summary>
    /// 执行 Agent 节点：通过 IAgentResolver 解析 AIAgent，发送消息并获取响应。
    /// </summary>
    private async Task<string?> ExecuteAgentNodeAsync(
        WorkflowExecution execution,
        WorkflowNodeVO node,
        string? input,
        CancellationToken cancellationToken)
    {
        if (!node.ReferenceId.HasValue)
            throw new InvalidOperationException($"Agent 节点 {node.NodeId} 缺少 ReferenceId");

        var agent = await _agentResolver.ResolveAsync(
            node.ReferenceId.Value,
            execution.Id.ToString(),
            cancellationToken);

        // 从 AIAgent 获取底层 IChatClient
        var chatClient = agent.GetService<IChatClient>()
            ?? throw new InvalidOperationException(
                $"Agent 节点 {node.NodeId} 不支持工作流执行（无 IChatClient 支持）");

        // 构建消息列表
        var messages = new List<ChatMessage>();
        var chatOptions = agent.GetService<ChatOptions>();
        if (chatOptions?.Instructions is not null)
        {
            messages.Add(new ChatMessage(ChatRole.System, chatOptions.Instructions));
        }
        messages.Add(new ChatMessage(ChatRole.User, input ?? "{}"));

        // 调用 Agent
        var response = await chatClient.GetResponseAsync(messages, chatOptions, cancellationToken);
        var lastMessage = response.Messages
            .LastOrDefault(m => m.Role == ChatRole.Assistant);

        return lastMessage?.Text ?? "{}";
    }

    /// <summary>
    /// 执行 Tool 节点：通过 IToolInvokerFactory 调用工具。
    /// </summary>
    private async Task<string?> ExecuteToolNodeAsync(
        WorkflowNodeVO node,
        string? input,
        CancellationToken cancellationToken)
    {
        if (!node.ReferenceId.HasValue)
            throw new InvalidOperationException($"Tool 节点 {node.NodeId} 缺少 ReferenceId");

        var tool = await _toolRepo.GetByIdAsync(node.ReferenceId.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Tool 不存在: {node.ReferenceId.Value}");

        var invoker = _toolInvokerFactory.GetInvoker(tool.ToolType);

        // 将 input JSON 解析为参数字典
        var parameters = new Dictionary<string, object?>();
        if (input is not null)
        {
            try
            {
                var jsonDoc = JsonDocument.Parse(input);
                foreach (var prop in jsonDoc.RootElement.EnumerateObject())
                {
                    parameters[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number => prop.Value.GetDecimal(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => null,
                        _ => prop.Value.GetRawText()
                    };
                }
            }
            catch (JsonException)
            {
                parameters["input"] = input;
            }
        }

        var result = await invoker.InvokeAsync(tool, null, parameters, cancellationToken: cancellationToken);

        if (!result.Success)
            throw new InvalidOperationException($"Tool 调用失败: {result.Error}");

        return result.Data.HasValue
            ? result.Data.Value.GetRawText()
            : "{}";
    }

    /// <summary>
    /// 执行 Condition 节点：评估所有条件边，路由到第一个匹配分支，跳过未匹配分支。
    /// 返回 true 表示继续执行，false 表示工作流已终止（无匹配或解析失败）。
    /// </summary>
    private async Task<bool> ExecuteConditionNodeAsync(
        WorkflowExecution execution,
        WorkflowNodeVO conditionNode,
        string? input,
        Dictionary<string, string?> nodeOutputs,
        HashSet<string> processedNodes,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("执行 Condition 节点 {NodeId}", conditionNode.NodeId);

        var conditionalEdges = execution.GraphSnapshot.Edges
            .Where(e => e.SourceNodeId == conditionNode.NodeId && e.EdgeType == WorkflowEdgeType.Conditional)
            .ToList();

        execution.StartNode(conditionNode.NodeId);
        await _executionRepo.UpdateAsync(execution, cancellationToken);

        if (conditionalEdges.Count == 0)
        {
            // 无条件边 — 透传
            execution.CompleteNode(conditionNode.NodeId, input);
            nodeOutputs[conditionNode.NodeId] = input;
            processedNodes.Add(conditionNode.NodeId);
            await _executionRepo.UpdateAsync(execution, cancellationToken);
            return true;
        }

        // 评估每条条件边
        string? matchedTargetNodeId = null;
        var jsonInput = input ?? "{}";

        foreach (var edge in conditionalEdges)
        {
            if (edge.Condition is null) continue;

            if (!_conditionEvaluator.TryEvaluate(edge.Condition, jsonInput, out var matched))
            {
                // 条件表达式解析失败 (FR-017)
                var errorMsg = $"条件表达式解析失败: {edge.Condition}";
                _logger.LogWarning("节点 {NodeId} {Error}", conditionNode.NodeId, errorMsg);

                execution.FailNode(conditionNode.NodeId, errorMsg);
                await _executionRepo.UpdateAsync(execution, cancellationToken);

                execution.Fail($"节点 {conditionNode.NodeId} {errorMsg}");
                await _executionRepo.UpdateAsync(execution, cancellationToken);
                return false;
            }

            if (matched && matchedTargetNodeId is null)
            {
                matchedTargetNodeId = edge.TargetNodeId;
            }
        }

        if (matchedTargetNodeId is null)
        {
            // 无匹配的条件分支 (FR-009)
            _logger.LogWarning("节点 {NodeId} 无匹配的条件分支", conditionNode.NodeId);

            execution.FailNode(conditionNode.NodeId, "无匹配的条件分支");
            await _executionRepo.UpdateAsync(execution, cancellationToken);

            execution.Fail("无匹配的条件分支");
            await _executionRepo.UpdateAsync(execution, cancellationToken);
            return false;
        }

        // 条件节点完成（透传输入）
        execution.CompleteNode(conditionNode.NodeId, input);
        nodeOutputs[conditionNode.NodeId] = input;
        processedNodes.Add(conditionNode.NodeId);

        // 跳过未匹配分支的目标节点
        foreach (var edge in conditionalEdges)
        {
            if (edge.TargetNodeId != matchedTargetNodeId)
            {
                execution.SkipNode(edge.TargetNodeId);
                processedNodes.Add(edge.TargetNodeId);
            }
        }
        await _executionRepo.UpdateAsync(execution, cancellationToken);

        _logger.LogInformation("条件节点 {NodeId} 路由到分支 {TargetNodeId}",
            conditionNode.NodeId, matchedTargetNodeId);
        return true;
    }

    /// <summary>
    /// 执行 FanOut 节点组：透传输入→标记FanOut完成→并行执行下游节点→收集结果。
    /// 如果所有并行分支均失败，将工作流标记为 Failed。
    /// </summary>
    private async Task ExecuteFanOutGroupAsync(
        WorkflowExecution execution,
        WorkflowNodeVO fanOutNode,
        string? input,
        Dictionary<string, List<WorkflowNodeVO>> fanOutDownstream,
        Dictionary<string, string?> nodeOutputs,
        HashSet<string> processedNodes,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("执行 FanOut 节点 {NodeId}", fanOutNode.NodeId);

        // FanOut 节点本身：透传
        execution.StartNode(fanOutNode.NodeId);
        execution.CompleteNode(fanOutNode.NodeId, input);
        nodeOutputs[fanOutNode.NodeId] = input;
        processedNodes.Add(fanOutNode.NodeId);
        await _executionRepo.UpdateAsync(execution, cancellationToken);

        if (!fanOutDownstream.TryGetValue(fanOutNode.NodeId, out var downstream) || downstream.Count == 0)
            return;

        // 标记所有并行节点为 Running
        foreach (var parallelNode in downstream)
        {
            execution.StartNode(parallelNode.NodeId);
        }
        await _executionRepo.UpdateAsync(execution, cancellationToken);

        // 并行执行所有下游节点
        var fanOutInput = input;
        var tasks = downstream.Select(async parallelNode =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(NodeTimeout);

            try
            {
                var output = await ExecuteNodeAsync(execution, parallelNode, fanOutInput, cts.Token);
                return (parallelNode.NodeId, Output: output, Error: (Exception?)null);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return (parallelNode.NodeId, Output: (string?)null,
                    Error: (Exception?)new TimeoutException($"节点执行超时（{NodeTimeout.TotalMinutes} 分钟）"));
            }
            catch (Exception ex)
            {
                return (parallelNode.NodeId, Output: (string?)null, Error: (Exception?)ex);
            }
        });

        var results = await Task.WhenAll(tasks);

        // 顺序更新节点状态（确保实体线程安全）
        foreach (var result in results)
        {
            if (result.Error is not null)
            {
                _logger.LogError(result.Error, "并行节点 {NodeId} 执行失败", result.NodeId);
                execution.FailNode(result.NodeId, result.Error.Message);
            }
            else
            {
                execution.CompleteNode(result.NodeId, result.Output);
                nodeOutputs[result.NodeId] = result.Output;
                _logger.LogInformation("并行节点 {NodeId} 执行完成", result.NodeId);
            }
            processedNodes.Add(result.NodeId);
        }
        await _executionRepo.UpdateAsync(execution, cancellationToken);

        // 任意并行分支失败 → 工作流失败 (FR-013)
        if (results.Any(r => r.Error is not null))
        {
            var failedNodes = results.Where(r => r.Error is not null).Select(r => r.NodeId);
            execution.Fail($"并行分支执行失败: {string.Join(", ", failedNodes)}");
            await _executionRepo.UpdateAsync(execution, cancellationToken);
        }
    }

    /// <summary>
    /// 执行 FanIn 节点：聚合所有上游节点的成功输出为 JSON 数组。
    /// </summary>
    private async Task<string?> ExecuteFanInAsync(
        WorkflowExecution execution,
        WorkflowNodeVO fanInNode,
        Dictionary<string, string?> nodeOutputs,
        HashSet<string> processedNodes,
        CancellationToken cancellationToken)
    {
        var upstreamNodeIds = execution.GraphSnapshot.Edges
            .Where(e => e.TargetNodeId == fanInNode.NodeId)
            .Select(e => e.SourceNodeId)
            .ToList();

        _logger.LogInformation("执行 FanIn 节点 {NodeId}, 聚合 {Count} 个上游节点输出",
            fanInNode.NodeId, upstreamNodeIds.Count);

        var aggregatedOutputs = new List<JsonElement>();
        foreach (var upId in upstreamNodeIds)
        {
            if (nodeOutputs.TryGetValue(upId, out var output) && output is not null)
            {
                try
                {
                    aggregatedOutputs.Add(JsonDocument.Parse(output).RootElement);
                }
                catch (JsonException)
                {
                    aggregatedOutputs.Add(JsonDocument.Parse(
                        JsonSerializer.Serialize(new { output })).RootElement);
                }
            }
        }

        var aggregated = JsonSerializer.Serialize(aggregatedOutputs);

        execution.StartNode(fanInNode.NodeId);
        execution.CompleteNode(fanInNode.NodeId, aggregated);
        nodeOutputs[fanInNode.NodeId] = aggregated;
        processedNodes.Add(fanInNode.NodeId);
        await _executionRepo.UpdateAsync(execution, cancellationToken);

        return aggregated;
    }

    /// <summary>
    /// 拓扑排序（Kahn's 算法）。返回 DAG 节点的执行顺序。
    /// </summary>
    internal static List<WorkflowNodeVO> TopologicalSort(WorkflowGraphVO graph)
    {
        var inDegree = new Dictionary<string, int>();
        var adjacency = new Dictionary<string, List<string>>();

        foreach (var node in graph.Nodes)
        {
            inDegree[node.NodeId] = 0;
            adjacency[node.NodeId] = [];
        }

        foreach (var edge in graph.Edges)
        {
            adjacency[edge.SourceNodeId].Add(edge.TargetNodeId);
            inDegree[edge.TargetNodeId]++;
        }

        var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var sorted = new List<WorkflowNodeVO>();
        var nodeMap = graph.Nodes.ToDictionary(n => n.NodeId);

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            sorted.Add(nodeMap[nodeId]);

            foreach (var neighbor in adjacency[nodeId])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        if (sorted.Count != graph.Nodes.Count)
            throw new InvalidOperationException("图中存在环，无法执行拓扑排序");

        return sorted;
    }
}
