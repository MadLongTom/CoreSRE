using FluentValidation;

namespace CoreSRE.Application.Workflows.Commands.ExecuteWorkflow;

/// <summary>
/// ExecuteWorkflowCommand 验证器
/// </summary>
public class ExecuteWorkflowCommandValidator : AbstractValidator<ExecuteWorkflowCommand>
{
    public ExecuteWorkflowCommandValidator()
    {
        RuleFor(x => x.WorkflowDefinitionId)
            .NotEmpty()
            .WithMessage("WorkflowDefinitionId 不能为空。");
    }
}
