using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Workflows.DTOs;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using MediatR;

namespace CoreSRE.Application.Workflows.Commands.UpdateWorkflow;

public class UpdateWorkflowCommandHandler : IRequestHandler<UpdateWorkflowCommand, Result<WorkflowDefinitionDto>>
{
    private readonly IWorkflowDefinitionRepository _workflowRepo;
    private readonly IAgentRegistrationRepository _agentRepo;
    private readonly IToolRegistrationRepository _toolRepo;
    private readonly IMapper _mapper;

    public UpdateWorkflowCommandHandler(
        IWorkflowDefinitionRepository workflowRepo,
        IAgentRegistrationRepository agentRepo,
        IToolRegistrationRepository toolRepo,
        IMapper mapper)
    {
        _workflowRepo = workflowRepo;
        _agentRepo = agentRepo;
        _toolRepo = toolRepo;
        _mapper = mapper;
    }

    public async Task<Result<WorkflowDefinitionDto>> Handle(
        UpdateWorkflowCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Fetch existing
        var workflow = await _workflowRepo.GetByIdAsync(request.Id, cancellationToken);
        if (workflow is null)
            return Result<WorkflowDefinitionDto>.NotFound();

        // 2. Uniqueness check (exclude self)
        if (await _workflowRepo.ExistsWithNameAsync(request.Name, request.Id, cancellationToken))
            return Result<WorkflowDefinitionDto>.Conflict($"工作流名称已存在: {request.Name}");

        // 3. Build domain graph and validate DAG
        var graph = BuildGraph(request.Graph);
        var dagResult = graph.Validate();
        if (!dagResult.IsValid)
        {
            var message = string.Join("; ", dagResult.Errors);
            return Result<WorkflowDefinitionDto>.Fail(message);
        }

        // 4. Validate references
        var refError = await ValidateReferencesAsync(request.Graph, cancellationToken);
        if (refError is not null)
            return Result<WorkflowDefinitionDto>.Fail(refError);

        // 5. Update entity (includes Draft status guard)
        try
        {
            workflow.Update(request.Name, request.Description, graph);
        }
        catch (InvalidOperationException ex)
        {
            return Result<WorkflowDefinitionDto>.Fail(ex.Message);
        }

        // 6. Persist
        await _workflowRepo.UpdateAsync(workflow, cancellationToken);

        // 7. Map and return
        var dto = _mapper.Map<WorkflowDefinitionDto>(workflow);
        return Result<WorkflowDefinitionDto>.Ok(dto);
    }

    private static WorkflowGraphVO BuildGraph(WorkflowGraphDto graphDto)
    {
        var nodes = graphDto.Nodes.Select(n => new WorkflowNodeVO
        {
            NodeId = n.NodeId,
            NodeType = Enum.Parse<WorkflowNodeType>(n.NodeType),
            ReferenceId = n.ReferenceId,
            DisplayName = n.DisplayName,
            Config = n.Config
        }).ToList();

        var edges = graphDto.Edges.Select(e => new WorkflowEdgeVO
        {
            EdgeId = e.EdgeId,
            SourceNodeId = e.SourceNodeId,
            TargetNodeId = e.TargetNodeId,
            EdgeType = Enum.Parse<WorkflowEdgeType>(e.EdgeType),
            Condition = e.Condition
        }).ToList();

        return new WorkflowGraphVO { Nodes = nodes, Edges = edges };
    }

    private async Task<string?> ValidateReferencesAsync(
        WorkflowGraphDto graphDto,
        CancellationToken cancellationToken)
    {
        foreach (var node in graphDto.Nodes)
        {
            if (node.NodeType is "Agent" && node.ReferenceId.HasValue)
            {
                var agent = await _agentRepo.GetByIdAsync(node.ReferenceId.Value, cancellationToken);
                if (agent is null)
                    return $"节点引用的 Agent 不存在: referenceId={node.ReferenceId.Value} (节点: {node.NodeId})";
            }
            else if (node.NodeType is "Tool" && node.ReferenceId.HasValue)
            {
                var tool = await _toolRepo.GetByIdAsync(node.ReferenceId.Value, cancellationToken);
                if (tool is null)
                    return $"节点引用的 Tool 不存在: referenceId={node.ReferenceId.Value} (节点: {node.NodeId})";
            }
        }

        return null;
    }
}
