using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Workflows.DTOs;
using MediatR;

namespace CoreSRE.Application.Workflows.Commands.CreateWorkflow;

public record CreateWorkflowCommand : IRequest<Result<WorkflowDefinitionDto>>
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public WorkflowGraphDto Graph { get; init; } = new();
}
