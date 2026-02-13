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
/// 工作流执行引擎 — 基于执行栈的数据驱动引擎。
/// 将 DAG 图中的节点压入执行栈，弹出执行，将输出数据通过边传播到下游节点。
/// 支持多端口输入/输出、等待队列（多输入合流）、条件路由。
/// </summary>
public class WorkflowEngine : IWorkflowEngine
{
    private readonly IAgentResolver _agentResolver;
    private readonly IToolInvokerFactory _toolInvokerFactory;
    private readonly IToolRegistrationRepository _toolRepo;
    private readonly IWorkflowExecutionRepository _executionRepo;
    private readonly IConditionEvaluator _conditionEvaluator;
    private readonly IExpressionEvaluator _expressionEvaluator;
    private readonly ILogger<WorkflowEngine> _logger;

    /// <summary>节点执行超时</summary>
    private static readonly TimeSpan NodeTimeout = TimeSpan.FromMinutes(5);

    /// <summary>每节点最大执行次数（无限循环保护）</summary>
    internal const int MaxNodeExecutions = 100;

    public WorkflowEngine(
        IAgentResolver agentResolver,
        IToolInvokerFactory toolInvokerFactory,
        IToolRegistrationRepository toolRepo,
        IWorkflowExecutionRepository executionRepo,
        IConditionEvaluator conditionEvaluator,
        IExpressionEvaluator expressionEvaluator,
        ILogger<WorkflowEngine> logger)
    {
        _agentResolver = agentResolver;
        _toolInvokerFactory = toolInvokerFactory;
        _toolRepo = toolRepo;
        _executionRepo = executionRepo;
        _conditionEvaluator = conditionEvaluator;
        _expressionEvaluator = expressionEvaluator;
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
            var ctx = new ExecutionContext(execution.GraphSnapshot) { MaxExecutionsPerNode = MaxNodeExecutions };

            // 跟踪每个节点的输出（供表达式引擎和最终输出使用）
            var nodeOutputs = new Dictionary<string, string?>();

            // 找到所有入度为 0 的起始节点
            var startNodes = FindStartNodes(ctx);

            // 将初始输入包装为结构化数据
            var initialInput = WrapInitialInput(execution.Input);

            // 将起始节点按拓扑序压入栈（反序压入以保证正序弹出）
            for (var i = startNodes.Count - 1; i >= 0; i--)
            {
                ctx.Push(new NodeExecutionTask
                {
                    Node = startNodes[i],
                    InputData = initialInput
                });
            }

            string? lastOutput = execution.Input.GetRawText();

            // 预计算 FanOut 下游分组和 FanIn 收集需求
            var fanOutDownstream = PrecomputeFanOutGroups(ctx);
            var fanInExpectedCounts = PrecomputeFanInCounts(ctx);
            var fanInAccumulatedOutputs = new Dictionary<string, List<string?>>();

            // 主循环：从栈中弹出任务执行
            while (!ctx.IsStackEmpty)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var task = ctx.Pop();
                var node = task.Node;

                // 跳过已处理的节点（如 Condition 跳过的分支）
                var nodeExec = execution.NodeExecutions.FirstOrDefault(ne => ne.NodeId == node.NodeId);
                if (nodeExec is not null && nodeExec.Status is NodeExecutionStatus.Skipped
                    or NodeExecutionStatus.Completed or NodeExecutionStatus.Failed)
                {
                    continue;
                }

                // 无限循环保护
                if (!ctx.IncrementAndCheckLimit(node.NodeId))
                {
                    var errorMsg = $"节点 {node.NodeId} 执行次数超过上限 ({MaxNodeExecutions})，疑似无限循环";
                    _logger.LogError(errorMsg);
                    execution.FailNode(node.NodeId, errorMsg);
                    await _executionRepo.UpdateAsync(execution, cancellationToken);
                    execution.Fail(errorMsg);
                    await _executionRepo.UpdateAsync(execution, cancellationToken);
                    return;
                }

                // FanOut: 使用并行执行语义（保持向后兼容）
                if (node.NodeType == WorkflowNodeType.FanOut)
                {
                    lastOutput = await ExecuteFanOutGroupAsync(
                        ctx, execution, node, task.InputData,
                        fanOutDownstream, fanInExpectedCounts, fanInAccumulatedOutputs,
                        nodeOutputs, cancellationToken);

                    if (execution.Status == ExecutionStatus.Failed)
                        return;
                    continue;
                }

                // FanIn: 在主循环中不应该被直接推入栈（由 FanOut 组处理）
                // 但如果到达这里，使用累积的输出进行聚合
                if (node.NodeType == WorkflowNodeType.FanIn)
                {
                    lastOutput = await ExecuteFanInFromAccumulator(
                        execution, node, fanInAccumulatedOutputs, nodeOutputs, cancellationToken);
                    PropagateData(ctx, execution, node, WrapStringAsOutput(lastOutput), nodeOutputs, cancellationToken);
                    continue;
                }

                // 提取用于记录的输入字符串
                var inputString = ExtractInputString(task.InputData);

                _logger.LogInformation("执行节点 {NodeId} (类型: {NodeType})", node.NodeId, node.NodeType);

                execution.StartNode(node.NodeId, inputString);
                await _executionRepo.UpdateAsync(execution, cancellationToken);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(NodeTimeout);

                try
                {
                    var exprCtx = BuildExpressionContext(execution, nodeOutputs, inputString);
                    var outputData = await DispatchNodeAsync(execution, node, task.InputData, exprCtx, cts.Token);

                    // 提取输出字符串用于记录
                    var outputString = ExtractOutputString(outputData);
                    lastOutput = outputString;
                    nodeOutputs[node.NodeId] = outputString;

                    execution.CompleteNode(node.NodeId, outputString);
                    await _executionRepo.UpdateAsync(execution, cancellationToken);

                    ctx.RecordResult(node.NodeId, new NodeRunResult
                    {
                        OutputData = outputData,
                        StartedAt = DateTimeOffset.UtcNow,
                        CompletedAt = DateTimeOffset.UtcNow,
                        IsSuccess = true
                    });

                    _logger.LogInformation("节点 {NodeId} 执行完成", node.NodeId);

                    // 将输出数据传播到下游节点
                    PropagateData(ctx, execution, node, outputData, nodeOutputs, cancellationToken);
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

    // ======================== Execution Stack Helpers ========================

    /// <summary>
    /// 找到所有入度为 0 的起始节点（拓扑排序起点）。
    /// </summary>
    private static List<WorkflowNodeVO> FindStartNodes(ExecutionContext ctx)
    {
        var startNodes = new List<WorkflowNodeVO>();
        foreach (var (nodeId, node) in ctx.NodeMap)
        {
            if (ctx.IncomingEdges[nodeId].Count == 0)
                startNodes.Add(node);
        }

        // 按拓扑序排序：使用 Kahn's 算法确保起始节点顺序一致
        // 对于单起始节点的 DAG，这只是返回那个节点
        return startNodes;
    }

    /// <summary>
    /// 将工作流初始输入（JsonElement）包装为 NodeInputData。
    /// </summary>
    private static NodeInputData WrapInitialInput(JsonElement input)
    {
        var item = new WorkflowItemVO(input);
        var portData = new PortDataVO([item]);
        return NodeInputData.FromSinglePort(portData);
    }

    /// <summary>
    /// 将结构化数据传播到下游节点通过出边。
    /// </summary>
    private void PropagateData(
        ExecutionContext ctx,
        WorkflowExecution execution,
        WorkflowNodeVO producerNode,
        NodeOutputData outputData,
        Dictionary<string, string?> nodeOutputs,
        CancellationToken cancellationToken)
    {
        if (!ctx.OutgoingEdges.TryGetValue(producerNode.NodeId, out var outEdges) || outEdges.Count == 0)
            return;

        // 按目标节点分组边，收集将要推入栈的任务
        var tasksToAdd = new List<NodeExecutionTask>();

        foreach (var edge in outEdges)
        {
            // 获取该边对应的源端口数据
            var portData = outputData.GetPort(edge.SourcePortIndex);
            if (portData is null || portData.Items.Count == 0)
            {
                // 该端口无数据，不传播（该下游分支不执行）
                continue;
            }

            // 附加 ItemSourceVO 血缘信息
            var taggedItems = portData.Items.Select((item, idx) =>
                new WorkflowItemVO(item.Json, new ItemSourceVO(producerNode.NodeId, edge.SourcePortIndex, idx))
            ).ToList();
            portData = new PortDataVO(taggedItems);

            // 跳过已处理的目标节点（如 Condition 跳过的分支）
            var targetExec = execution.NodeExecutions.FirstOrDefault(ne => ne.NodeId == edge.TargetNodeId);
            if (targetExec is not null && targetExec.Status is NodeExecutionStatus.Skipped
                or NodeExecutionStatus.Completed or NodeExecutionStatus.Failed)
            {
                continue;
            }

            var targetNode = ctx.NodeMap[edge.TargetNodeId];

            if (targetNode.InputCount > 1)
            {
                // 多输入节点：加入等待队列
                ctx.AddToWaiting(targetNode.NodeId, edge.TargetPortIndex, portData, targetNode.InputCount);

                if (ctx.TryPromote(targetNode.NodeId, out var combinedInput))
                {
                    // 所有端口到齐，推入执行栈
                    tasksToAdd.Add(new NodeExecutionTask
                    {
                        Node = targetNode,
                        InputData = combinedInput!
                    });
                }
            }
            else
            {
                // 单输入节点：直接推入执行栈
                var inputData = NodeInputData.FromSinglePort(portData);
                tasksToAdd.Add(new NodeExecutionTask
                {
                    Node = targetNode,
                    InputData = inputData
                });
            }
        }

        // 反序压入栈以保证正序弹出（维持拓扑序）
        for (var i = tasksToAdd.Count - 1; i >= 0; i--)
        {
            ctx.Push(tasksToAdd[i]);
        }
    }

    // ======================== FanOut/FanIn Group Handling ========================

    /// <summary>
    /// 预计算 FanOut 下游节点分组。
    /// </summary>
    private static Dictionary<string, List<WorkflowNodeVO>> PrecomputeFanOutGroups(ExecutionContext ctx)
    {
        var groups = new Dictionary<string, List<WorkflowNodeVO>>();
        foreach (var (nodeId, node) in ctx.NodeMap)
        {
            if (node.NodeType != WorkflowNodeType.FanOut) continue;

            var downstream = ctx.OutgoingEdges[nodeId]
                .Select(e => ctx.NodeMap[e.TargetNodeId])
                .ToList();
            groups[nodeId] = downstream;
        }
        return groups;
    }

    /// <summary>
    /// 预计算 FanIn 节点的期望输入数（= 入边数量）。
    /// </summary>
    private static Dictionary<string, int> PrecomputeFanInCounts(ExecutionContext ctx)
    {
        var counts = new Dictionary<string, int>();
        foreach (var (nodeId, node) in ctx.NodeMap)
        {
            if (node.NodeType == WorkflowNodeType.FanIn)
            {
                counts[nodeId] = ctx.IncomingEdges[nodeId].Count;
            }
        }
        return counts;
    }

    /// <summary>
    /// 执行 FanOut 节点组：透传输入→并行执行下游节点 (Task.WhenAll)→收集结果。
    /// 保持与旧引擎完全一致的并行执行语义。
    /// </summary>
    private async Task<string?> ExecuteFanOutGroupAsync(
        ExecutionContext ctx,
        WorkflowExecution execution,
        WorkflowNodeVO fanOutNode,
        NodeInputData inputData,
        Dictionary<string, List<WorkflowNodeVO>> fanOutDownstream,
        Dictionary<string, int> fanInExpectedCounts,
        Dictionary<string, List<string?>> fanInAccumulatedOutputs,
        Dictionary<string, string?> nodeOutputs,
        CancellationToken cancellationToken)
    {
        var inputString = ExtractInputString(inputData);
        _logger.LogInformation("执行 FanOut 节点 {NodeId}", fanOutNode.NodeId);

        // FanOut 节点本身：透传
        execution.StartNode(fanOutNode.NodeId, inputString);
        execution.CompleteNode(fanOutNode.NodeId, inputString);
        nodeOutputs[fanOutNode.NodeId] = inputString;
        await _executionRepo.UpdateAsync(execution, cancellationToken);

        if (!fanOutDownstream.TryGetValue(fanOutNode.NodeId, out var downstream) || downstream.Count == 0)
            return inputString;

        // 标记所有并行节点为 Running
        foreach (var parallelNode in downstream)
        {
            execution.StartNode(parallelNode.NodeId, inputString);
        }
        await _executionRepo.UpdateAsync(execution, cancellationToken);

        // 并行执行所有下游节点 (Task.WhenAll — 保持向后兼容)
        var tasks = downstream.Select(async parallelNode =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(NodeTimeout);

            try
            {
                var exprCtx = BuildExpressionContext(execution, nodeOutputs, inputString);
                var resolvedConfig = ResolveNodeConfig(parallelNode, exprCtx);
                var output = parallelNode.NodeType switch
                {
                    WorkflowNodeType.Agent => ExtractOutputString(
                        await DispatchAgentAsync(execution, parallelNode, inputString, resolvedConfig, cts.Token)),
                    WorkflowNodeType.Tool => ExtractOutputString(
                        await DispatchToolAsync(parallelNode, inputString, resolvedConfig, cts.Token)),
                    _ => inputString
                };
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

        // 顺序更新节点状态
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

                // 累积输出给 FanIn
                foreach (var edge in ctx.OutgoingEdges[result.NodeId])
                {
                    if (ctx.NodeMap[edge.TargetNodeId].NodeType == WorkflowNodeType.FanIn)
                    {
                        if (!fanInAccumulatedOutputs.TryGetValue(edge.TargetNodeId, out var accOutputs))
                        {
                            accOutputs = [];
                            fanInAccumulatedOutputs[edge.TargetNodeId] = accOutputs;
                        }
                        accOutputs.Add(result.Output);
                    }
                }
            }
        }
        await _executionRepo.UpdateAsync(execution, cancellationToken);

        // 任意并行分支失败 → 工作流失败 (FR-013)
        if (results.Any(r => r.Error is not null))
        {
            var failedNodes = results.Where(r => r.Error is not null).Select(r => r.NodeId);
            execution.Fail($"并行分支执行失败: {string.Join(", ", failedNodes)}");
            await _executionRepo.UpdateAsync(execution, cancellationToken);
            return inputString;
        }

        // 检查是否有 FanIn 下游，如果累积够了就推入栈
        foreach (var (fanInId, accOutputs) in fanInAccumulatedOutputs)
        {
            if (fanInExpectedCounts.TryGetValue(fanInId, out var expected) && accOutputs.Count >= expected)
            {
                // FanIn 就绪，推入栈
                var fanInNode = ctx.NodeMap[fanInId];
                ctx.Push(new NodeExecutionTask { Node = fanInNode, InputData = inputData });
            }
        }

        return inputString;
    }

    /// <summary>
    /// 从累积的输出中执行 FanIn 聚合。
    /// </summary>
    private async Task<string?> ExecuteFanInFromAccumulator(
        WorkflowExecution execution,
        WorkflowNodeVO fanInNode,
        Dictionary<string, List<string?>> fanInAccumulatedOutputs,
        Dictionary<string, string?> nodeOutputs,
        CancellationToken cancellationToken)
    {
        // 如果有累积的输出，使用它们
        List<string?> upstreamOutputs;
        if (fanInAccumulatedOutputs.TryGetValue(fanInNode.NodeId, out var accOutputs))
        {
            upstreamOutputs = accOutputs;
        }
        else
        {
            // 回退：从入边的源节点 nodeOutputs 收集
            upstreamOutputs = execution.GraphSnapshot.Edges
                .Where(e => e.TargetNodeId == fanInNode.NodeId)
                .Select(e => nodeOutputs.GetValueOrDefault(e.SourceNodeId))
                .ToList();
        }

        _logger.LogInformation("执行 FanIn 节点 {NodeId}, 聚合 {Count} 个上游节点输出",
            fanInNode.NodeId, upstreamOutputs.Count);

        var aggregatedOutputs = new List<JsonElement>();
        foreach (var output in upstreamOutputs)
        {
            if (output is not null)
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

        var fanInInput = JsonSerializer.Serialize(upstreamOutputs);
        execution.StartNode(fanInNode.NodeId, fanInInput);
        execution.CompleteNode(fanInNode.NodeId, aggregated);
        nodeOutputs[fanInNode.NodeId] = aggregated;
        await _executionRepo.UpdateAsync(execution, cancellationToken);

        return aggregated;
    }

    // ======================== Node Dispatch ========================

    /// <summary>
    /// 分派节点执行：根据节点类型调用对应的处理方法。
    /// 返回结构化输出数据。
    /// </summary>
    private async Task<NodeOutputData> DispatchNodeAsync(
        WorkflowExecution execution,
        WorkflowNodeVO node,
        NodeInputData inputData,
        ExpressionContext? exprContext,
        CancellationToken cancellationToken)
    {
        // 提取输入字符串（兼容旧式表达式引擎和条件求值）
        var inputString = ExtractInputString(inputData);

        // 如果有表达式上下文，解析节点 Config 中的 {{ }} 模板
        var resolvedConfig = exprContext is not null ? ResolveNodeConfig(node, exprContext) : node.Config;

        return node.NodeType switch
        {
            WorkflowNodeType.Start => NodeOutputData.FromSinglePort(inputData.GetPort(0) ?? PortDataVO.Empty), // 开始节点透传
            WorkflowNodeType.Agent => await DispatchAgentAsync(execution, node, inputString, resolvedConfig, cancellationToken),
            WorkflowNodeType.Tool => await DispatchToolAsync(node, inputString, resolvedConfig, cancellationToken),
            WorkflowNodeType.Condition => await DispatchConditionAsync(execution, node, inputData, inputString, cancellationToken),
            WorkflowNodeType.FanOut => DispatchFanOut(node, inputData),
            WorkflowNodeType.FanIn => DispatchFanIn(node, inputData),
            _ => NodeOutputData.FromSinglePort(inputData.GetPort(0) ?? PortDataVO.Empty) // 透传
        };
    }

    /// <summary>
    /// 执行 Agent 节点：提取文本输入→构建聊天消息→调用 IChatClient→包装响应为输出项。
    /// </summary>
    private async Task<NodeOutputData> DispatchAgentAsync(
        WorkflowExecution execution,
        WorkflowNodeVO node,
        string? inputString,
        string? resolvedConfig,
        CancellationToken cancellationToken)
    {
        if (!node.ReferenceId.HasValue)
            throw new InvalidOperationException($"Agent 节点 {node.NodeId} 缺少 ReferenceId");

        var resolved = await _agentResolver.ResolveAsync(
            node.ReferenceId.Value,
            execution.Id.ToString(),
            cancellationToken);

        var chatClient = resolved.Agent.GetService<IChatClient>()
            ?? throw new InvalidOperationException(
                $"Agent 节点 {node.NodeId} 不支持工作流执行（无 IChatClient 支持）");

        var messages = new List<ChatMessage>();
        var chatOptions = resolved.Agent.GetService<ChatOptions>();

        // 解析 Config 覆盖
        string? systemPromptOverride = null;
        string? userPromptOverride = null;
        if (!string.IsNullOrWhiteSpace(resolvedConfig))
        {
            try
            {
                using var configDoc = JsonDocument.Parse(resolvedConfig);
                if (configDoc.RootElement.TryGetProperty("systemPrompt", out var sp))
                    systemPromptOverride = sp.GetString();
                if (configDoc.RootElement.TryGetProperty("userPrompt", out var up))
                    userPromptOverride = up.GetString();
            }
            catch (JsonException) { }
        }

        if (systemPromptOverride is not null)
            messages.Add(new ChatMessage(ChatRole.System, systemPromptOverride));
        else if (chatOptions?.Instructions is not null)
            messages.Add(new ChatMessage(ChatRole.System, chatOptions.Instructions));

        messages.Add(new ChatMessage(ChatRole.User, userPromptOverride ?? inputString ?? "{}"));

        var response = await chatClient.GetResponseAsync(messages, chatOptions, cancellationToken);
        var lastMessage = response.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant);
        var outputText = lastMessage?.Text ?? "{}";

        // 将响应包装为结构化输出
        return WrapStringAsOutput(outputText);
    }

    /// <summary>
    /// 执行 Tool 节点：提取参数→调用工具→包装结果为输出项。
    /// </summary>
    private async Task<NodeOutputData> DispatchToolAsync(
        WorkflowNodeVO node,
        string? inputString,
        string? resolvedConfig,
        CancellationToken cancellationToken)
    {
        if (!node.ReferenceId.HasValue)
            throw new InvalidOperationException($"Tool 节点 {node.NodeId} 缺少 ReferenceId");

        var tool = await _toolRepo.GetByIdAsync(node.ReferenceId.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Tool 不存在: {node.ReferenceId.Value}");

        var invoker = _toolInvokerFactory.GetInvoker(tool.ToolType);

        var parameters = new Dictionary<string, object?>();
        if (inputString is not null)
        {
            try
            {
                var jsonDoc = JsonDocument.Parse(inputString);
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
                parameters["input"] = inputString;
            }
        }

        var result = await invoker.InvokeAsync(tool, null, parameters, cancellationToken: cancellationToken);

        if (!result.Success)
            throw new InvalidOperationException($"Tool 调用失败: {result.Error}");

        var outputText = result.Data.HasValue ? result.Data.Value.GetRawText() : "{}";
        return WrapStringAsOutput(outputText);
    }

    /// <summary>
    /// 执行 Condition 节点：评估条件边，路由数据到匹配的输出端口。
    /// 匹配的边 → 数据输出到该边对应的输出端口。
    /// 不匹配的边目标 → 跳过。
    /// </summary>
    private async Task<NodeOutputData> DispatchConditionAsync(
        WorkflowExecution execution,
        WorkflowNodeVO conditionNode,
        NodeInputData inputData,
        string? inputString,
        CancellationToken cancellationToken)
    {
        var conditionalEdges = execution.GraphSnapshot.Edges
            .Where(e => e.SourceNodeId == conditionNode.NodeId && e.EdgeType == WorkflowEdgeType.Conditional)
            .ToList();

        if (conditionalEdges.Count == 0)
        {
            // 无条件边 — 透传
            return NodeOutputData.FromSinglePort(inputData.GetPort(0) ?? PortDataVO.Empty);
        }

        // 构建表达式上下文
        var nodeOutputs = new Dictionary<string, string?>();
        var jsonInput = inputString ?? "{}";
        var exprCtx = BuildExpressionContext(execution, nodeOutputs, jsonInput);

        // 评估条件边
        string? matchedTargetNodeId = null;
        int? matchedSourcePortIndex = null;

        foreach (var edge in conditionalEdges)
        {
            if (edge.Condition is null) continue;

            bool matched;
            try
            {
                matched = _expressionEvaluator.EvaluateCondition(edge.Condition, exprCtx);
            }
            catch (ExpressionEvaluationException)
            {
                if (!_conditionEvaluator.TryEvaluate(edge.Condition, jsonInput, out matched))
                {
                    var errorMsg = $"条件表达式解析失败: {edge.Condition}";
                    _logger.LogWarning("节点 {NodeId} {Error}", conditionNode.NodeId, errorMsg);
                    throw new InvalidOperationException(errorMsg);
                }
            }

            if (matched && matchedTargetNodeId is null)
            {
                matchedTargetNodeId = edge.TargetNodeId;
                matchedSourcePortIndex = edge.SourcePortIndex;
            }
        }

        if (matchedTargetNodeId is null)
        {
            throw new InvalidOperationException("无匹配的条件分支");
        }

        // 跳过未匹配分支
        foreach (var edge in conditionalEdges)
        {
            if (edge.TargetNodeId != matchedTargetNodeId)
            {
                execution.SkipNode(edge.TargetNodeId);
            }
        }
        await _executionRepo.UpdateAsync(execution, cancellationToken);

        _logger.LogInformation("条件节点 {NodeId} 路由到分支 {TargetNodeId}",
            conditionNode.NodeId, matchedTargetNodeId);

        // 将输入数据透传到匹配的输出端口
        var inputPort = inputData.GetPort(0) ?? PortDataVO.Empty;

        // 对于 OutputCount=1 的旧式 Condition，数据直接放在端口 0
        if (conditionNode.OutputCount <= 1)
        {
            return NodeOutputData.FromSinglePort(inputPort);
        }

        // 对于多端口 Condition，数据放在匹配的端口上
        var ports = new PortDataVO?[conditionNode.OutputCount];
        ports[matchedSourcePortIndex ?? 0] = inputPort;
        return NodeOutputData.FromPorts(ports);
    }

    /// <summary>
    /// FanOut 节点：将输入复制到所有输出端口。
    /// </summary>
    private static NodeOutputData DispatchFanOut(WorkflowNodeVO node, NodeInputData inputData)
    {
        var inputPort = inputData.GetPort(0) ?? PortDataVO.Empty;

        // FanOut 复制输入到所有输出端口（OutputCount 个端口）
        // 但由于传统 FanOut 的 OutputCount 默认为 1，我们需要使用出边数来确定实际端口数
        // 最终由 PropagateData 根据边的 SourcePortIndex 分发
        return NodeOutputData.FromSinglePort(inputPort);
    }

    /// <summary>
    /// FanIn 节点：将所有输入端口的数据合并为 JSON 数组输出。
    /// </summary>
    private static NodeOutputData DispatchFanIn(WorkflowNodeVO node, NodeInputData inputData)
    {
        // 收集所有输入端口的数据
        var allItems = new List<JsonElement>();

        if (inputData.Connections.TryGetValue(NodeInputData.MainConnection, out var ports))
        {
            foreach (var port in ports)
            {
                if (port is null) continue;
                foreach (var item in port.Items)
                {
                    allItems.Add(item.Json);
                }
            }
        }

        // 序列化为 JSON 数组
        var aggregated = JsonSerializer.Serialize(allItems);
        return WrapStringAsOutput(aggregated);
    }

    // ======================== Data Conversion Helpers ========================

    /// <summary>
    /// 从 NodeInputData 提取第一个端口的第一个项的 JSON 原始文本，
    /// 用于兼容旧式字符串传递（表达式引擎、条件求值、Agent 消息）。
    /// </summary>
    private static string? ExtractInputString(NodeInputData inputData)
    {
        var port = inputData.GetPort(0);
        if (port is null || port.Items.Count == 0)
            return null;

        return port.Items[0].Json.GetRawText();
    }

    /// <summary>
    /// 从 NodeOutputData 提取第一个端口的第一个项的 JSON 原始文本。
    /// </summary>
    private static string? ExtractOutputString(NodeOutputData outputData)
    {
        var port = outputData.GetPort(0);
        if (port is null || port.Items.Count == 0)
            return null;

        return port.Items[0].Json.GetRawText();
    }

    /// <summary>
    /// 将字符串输出包装为 NodeOutputData（单端口单项）。
    /// </summary>
    private static NodeOutputData WrapStringAsOutput(string? text)
    {
        if (text is null)
            return NodeOutputData.Empty;

        JsonElement json;
        try
        {
            json = JsonDocument.Parse(text).RootElement;
        }
        catch (JsonException)
        {
            json = JsonDocument.Parse(JsonSerializer.Serialize(new { output = text })).RootElement;
        }

        var item = new WorkflowItemVO(json);
        var portData = new PortDataVO([item]);
        return NodeOutputData.FromSinglePort(portData);
    }

    // ======================== Expression & Config ========================

    /// <summary>
    /// 根据当前执行状态构建表达式求值上下文。
    /// </summary>
    private static ExpressionContext BuildExpressionContext(
        WorkflowExecution execution,
        Dictionary<string, string?> nodeOutputs,
        string? currentInput)
    {
        var outputs = new Dictionary<string, List<string?>>();
        foreach (var (nodeId, output) in nodeOutputs)
        {
            outputs[nodeId] = [output];
        }

        return new ExpressionContext
        {
            NodeOutputs = outputs,
            CurrentInput = currentInput,
            ExecutionId = execution.Id,
            WorkflowId = execution.WorkflowDefinitionId,
        };
    }

    /// <summary>
    /// 对节点 Config 中的 {{ expr }} 模板表达式求值。
    /// </summary>
    private string? ResolveNodeConfig(WorkflowNodeVO node, ExpressionContext context)
    {
        if (string.IsNullOrWhiteSpace(node.Config))
            return node.Config;

        try
        {
            return _expressionEvaluator.Evaluate(node.Config, context);
        }
        catch (ExpressionEvaluationException ex)
        {
            _logger.LogWarning(ex, "节点 {NodeId} Config 表达式解析失败", node.NodeId);
            return node.Config;
        }
    }

    /// <summary>
    /// 拓扑排序（Kahn's 算法）。返回 DAG 节点的执行顺序。
    /// 保留用于测试兼容。
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
