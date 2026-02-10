using FluentValidation;

namespace CoreSRE.Application.Agents.Commands.RegisterAgent;

/// <summary>
/// 注册 Agent 命令验证器 — 请求结构校验
/// </summary>
public class RegisterAgentCommandValidator : AbstractValidator<RegisterAgentCommand>
{
    private static readonly string[] ValidAgentTypes = ["A2A", "ChatClient", "Workflow"];

    public RegisterAgentCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");

        RuleFor(x => x.AgentType)
            .NotEmpty().WithMessage("AgentType is required.")
            .Must(t => ValidAgentTypes.Contains(t))
            .WithMessage($"AgentType must be one of: {string.Join(", ", ValidAgentTypes)}.");

        // A2A type-specific rules
        When(x => x.AgentType == "A2A", () =>
        {
            RuleFor(x => x.Endpoint)
                .NotEmpty().WithMessage("Endpoint is required for A2A agents.")
                .MaximumLength(2048).WithMessage("Endpoint must not exceed 2048 characters.");

            RuleFor(x => x.AgentCard)
                .NotNull().WithMessage("AgentCard is required for A2A agents.");
        });

        // ChatClient type-specific rules
        When(x => x.AgentType == "ChatClient", () =>
        {
            RuleFor(x => x.LlmConfig)
                .NotNull().WithMessage("LlmConfig is required for ChatClient agents.");

            RuleFor(x => x.LlmConfig!.ModelId)
                .NotEmpty().WithMessage("ModelId is required for ChatClient agents.")
                .When(x => x.LlmConfig is not null);
        });

        // Workflow type-specific rules
        When(x => x.AgentType == "Workflow", () =>
        {
            RuleFor(x => x.WorkflowRef)
                .NotNull().WithMessage("WorkflowRef is required for Workflow agents.")
                .NotEqual(Guid.Empty).WithMessage("WorkflowRef must not be empty.");
        });
    }
}
