using CoreSRE.Domain.Enums;
using FluentValidation;

namespace CoreSRE.Application.Workflows.Commands.UpdateWorkflow;

public class UpdateWorkflowCommandValidator : AbstractValidator<UpdateWorkflowCommand>
{
    private static readonly string[] ValidNodeTypes =
        Enum.GetNames<WorkflowNodeType>();

    private static readonly string[] ValidEdgeTypes =
        Enum.GetNames<WorkflowEdgeType>();

    public UpdateWorkflowCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEqual(Guid.Empty).WithMessage("Id must not be empty.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");

        RuleFor(x => x.Graph)
            .NotNull().WithMessage("Graph is required.");

        When(x => x.Graph is not null, () =>
        {
            RuleFor(x => x.Graph.Nodes)
                .NotEmpty().WithMessage("Graph must contain at least one node.");

            RuleForEach(x => x.Graph.Nodes).ChildRules(node =>
            {
                node.RuleFor(n => n.NodeId)
                    .NotEmpty().WithMessage("NodeId is required.");

                node.RuleFor(n => n.DisplayName)
                    .NotEmpty().WithMessage("DisplayName is required.");

                node.RuleFor(n => n.NodeType)
                    .NotEmpty().WithMessage("NodeType is required.")
                    .Must(t => ValidNodeTypes.Contains(t))
                    .WithMessage($"NodeType must be one of: {string.Join(", ", ValidNodeTypes)}.");
            });

            RuleForEach(x => x.Graph.Edges).ChildRules(edge =>
            {
                edge.RuleFor(e => e.EdgeId)
                    .NotEmpty().WithMessage("EdgeId is required.");

                edge.RuleFor(e => e.SourceNodeId)
                    .NotEmpty().WithMessage("SourceNodeId is required.");

                edge.RuleFor(e => e.TargetNodeId)
                    .NotEmpty().WithMessage("TargetNodeId is required.");

                edge.RuleFor(e => e.EdgeType)
                    .NotEmpty().WithMessage("EdgeType is required.")
                    .Must(t => ValidEdgeTypes.Contains(t))
                    .WithMessage($"EdgeType must be one of: {string.Join(", ", ValidEdgeTypes)}.");

                edge.RuleFor(e => e.Condition)
                    .NotEmpty().WithMessage("Condition is required for Conditional edges.")
                    .When(e => e.EdgeType == nameof(WorkflowEdgeType.Conditional));
            });
        });
    }
}
