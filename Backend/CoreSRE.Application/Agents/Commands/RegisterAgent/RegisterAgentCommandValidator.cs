using System.Text.Json;
using FluentValidation;

namespace CoreSRE.Application.Agents.Commands.RegisterAgent;

/// <summary>
/// 注册 Agent 命令验证器 — 请求结构校验
/// </summary>
public class RegisterAgentCommandValidator : AbstractValidator<RegisterAgentCommand>
{
    private static readonly string[] ValidAgentTypes = ["A2A", "ChatClient", "Workflow"];
    private static readonly string[] ValidResponseFormats = ["Text", "Json"];
    private static readonly string[] ValidToolModes = ["Auto", "Required", "None"];

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

            // ChatOptions validation
            When(x => x.LlmConfig is not null, () =>
            {
                RuleFor(x => x.LlmConfig!.Temperature)
                    .InclusiveBetween(0f, 2f).WithMessage("Temperature must be between 0 and 2.")
                    .When(x => x.LlmConfig!.Temperature is not null);

                RuleFor(x => x.LlmConfig!.MaxOutputTokens)
                    .GreaterThan(0).WithMessage("MaxOutputTokens must be greater than 0.")
                    .When(x => x.LlmConfig!.MaxOutputTokens is not null);

                RuleFor(x => x.LlmConfig!.TopP)
                    .InclusiveBetween(0f, 1f).WithMessage("TopP must be between 0 and 1.")
                    .When(x => x.LlmConfig!.TopP is not null);

                RuleFor(x => x.LlmConfig!.TopK)
                    .GreaterThan(0).WithMessage("TopK must be greater than 0.")
                    .When(x => x.LlmConfig!.TopK is not null);

                RuleFor(x => x.LlmConfig!.FrequencyPenalty)
                    .InclusiveBetween(-2f, 2f).WithMessage("FrequencyPenalty must be between -2 and 2.")
                    .When(x => x.LlmConfig!.FrequencyPenalty is not null);

                RuleFor(x => x.LlmConfig!.PresencePenalty)
                    .InclusiveBetween(-2f, 2f).WithMessage("PresencePenalty must be between -2 and 2.")
                    .When(x => x.LlmConfig!.PresencePenalty is not null);

                RuleFor(x => x.LlmConfig!.ResponseFormat)
                    .Must(f => ValidResponseFormats.Contains(f))
                    .WithMessage($"ResponseFormat must be one of: {string.Join(", ", ValidResponseFormats)}.")
                    .When(x => !string.IsNullOrWhiteSpace(x.LlmConfig!.ResponseFormat));

                RuleFor(x => x.LlmConfig!.ResponseFormatSchema)
                    .Must(BeValidJson).WithMessage("ResponseFormatSchema must be valid JSON.")
                    .When(x => !string.IsNullOrWhiteSpace(x.LlmConfig!.ResponseFormatSchema));

                RuleFor(x => x.LlmConfig!.ToolMode)
                    .Must(m => ValidToolModes.Contains(m))
                    .WithMessage($"ToolMode must be one of: {string.Join(", ", ValidToolModes)}.")
                    .When(x => !string.IsNullOrWhiteSpace(x.LlmConfig!.ToolMode));
            });
        });

        // Workflow type-specific rules
        When(x => x.AgentType == "Workflow", () =>
        {
            RuleFor(x => x.WorkflowRef)
                .NotNull().WithMessage("WorkflowRef is required for Workflow agents.")
                .NotEqual(Guid.Empty).WithMessage("WorkflowRef must not be empty.");
        });
    }

    private static bool BeValidJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return true;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }
}
