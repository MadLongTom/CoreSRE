using FluentValidation;

namespace CoreSRE.Application.Workflows.Commands.DeleteWorkflow;

public class DeleteWorkflowCommandValidator : AbstractValidator<DeleteWorkflowCommand>
{
    public DeleteWorkflowCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEqual(Guid.Empty).WithMessage("Id must not be empty.");
    }
}
