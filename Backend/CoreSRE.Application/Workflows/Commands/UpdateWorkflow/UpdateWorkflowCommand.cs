using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Workflows.DTOs;
using MediatR;

namespace CoreSRE.Application.Workflows.Commands.UpdateWorkflow;

public record UpdateWorkflowCommand : IRequest<Result<WorkflowDefinitionDto>>
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public WorkflowGraphDto Graph { get; init; } = new();
    /// <summary>可选的目标状态，传入 "Published" 触发发布，"Draft" 触发取消发布</summary>
    public string? Status { get; init; }
}
