using System.Text.Json;
using System.Threading.Channels;
using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Workflows.DTOs;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Workflows.Commands.ExecuteWorkflow;

/// <summary>
/// 启动工作流执行处理器。
/// 验证工作流为 Published 状态 → 验证 Agent/Tool 引用 → 快照图 → 创建 WorkflowExecution → 入队 Channel → 返回 202。
/// </summary>
public class ExecuteWorkflowCommandHandler : IRequestHandler<ExecuteWorkflowCommand, Result<WorkflowExecutionDto>>
{
    private readonly IWorkflowDefinitionRepository _workflowRepo;
    private readonly IAgentRegistrationRepository _agentRepo;
    private readonly IToolRegistrationRepository _toolRepo;
    private readonly IWorkflowExecutionRepository _executionRepo;
    private readonly Channel<ExecuteWorkflowRequest> _channel;
    private readonly IMapper _mapper;

    public ExecuteWorkflowCommandHandler(
        IWorkflowDefinitionRepository workflowRepo,
        IAgentRegistrationRepository agentRepo,
        IToolRegistrationRepository toolRepo,
        IWorkflowExecutionRepository executionRepo,
        Channel<ExecuteWorkflowRequest> channel,
        IMapper mapper)
    {
        _workflowRepo = workflowRepo;
        _agentRepo = agentRepo;
        _toolRepo = toolRepo;
        _executionRepo = executionRepo;
        _channel = channel;
        _mapper = mapper;
    }

    public async Task<Result<WorkflowExecutionDto>> Handle(
        ExecuteWorkflowCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Load workflow definition
        var workflow = await _workflowRepo.GetByIdAsync(request.WorkflowDefinitionId, cancellationToken);
        if (workflow is null)
            return Result<WorkflowExecutionDto>.NotFound(
                $"工作流定义不存在: {request.WorkflowDefinitionId}");

        // 2. Validate Published status (FR-014)
        if (workflow.Status != WorkflowStatus.Published)
            return Result<WorkflowExecutionDto>.Fail(
                $"仅允许执行 Published 状态的工作流，当前状态: {workflow.Status}");

        // 3. Validate agent/tool references exist (FR-015)
        var refError = await ValidateReferencesAsync(workflow, cancellationToken);
        if (refError is not null)
            return Result<WorkflowExecutionDto>.Fail(refError);

        // 4. Snapshot graph and create execution (FR-023)
        // Deep-copy the graph to avoid EF Core tracking the same owned VO instances
        // under both WorkflowDefinition.Graph and WorkflowExecution.GraphSnapshot.
        var graphSnapshot = workflow.Graph with
        {
            Nodes = workflow.Graph.Nodes.Select(n => n with { }).ToList(),
            Edges = workflow.Graph.Edges.Select(e => e with { }).ToList(),
        };
        var input = request.Input ?? JsonDocument.Parse("{}").RootElement;
        var execution = WorkflowExecution.Create(
            request.WorkflowDefinitionId,
            input,
            graphSnapshot);

        // 5. Persist execution
        await _executionRepo.AddAsync(execution, cancellationToken);

        // 6. Enqueue to Channel for async processing
        await _channel.Writer.WriteAsync(
            new ExecuteWorkflowRequest(execution.Id), cancellationToken);

        // 7. Map and return
        var dto = _mapper.Map<WorkflowExecutionDto>(execution);
        return Result<WorkflowExecutionDto>.Ok(dto);
    }

    private async Task<string?> ValidateReferencesAsync(
        WorkflowDefinition workflow,
        CancellationToken cancellationToken)
    {
        foreach (var node in workflow.Graph.Nodes)
        {
            if (node.NodeType == WorkflowNodeType.Agent && node.ReferenceId.HasValue)
            {
                var agent = await _agentRepo.GetByIdAsync(node.ReferenceId.Value, cancellationToken);
                if (agent is null)
                    return $"节点引用的 Agent 不存在: referenceId={node.ReferenceId.Value} (节点: {node.NodeId})";
            }
            else if (node.NodeType == WorkflowNodeType.Tool && node.ReferenceId.HasValue)
            {
                var tool = await _toolRepo.GetByIdAsync(node.ReferenceId.Value, cancellationToken);
                if (tool is null)
                    return $"节点引用的 Tool 不存在: referenceId={node.ReferenceId.Value} (节点: {node.NodeId})";
            }
        }

        return null;
    }
}
