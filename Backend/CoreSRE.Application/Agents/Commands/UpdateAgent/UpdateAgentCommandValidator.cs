using System.Text.Json;
using FluentValidation;

namespace CoreSRE.Application.Agents.Commands.UpdateAgent;

/// <summary>
/// 更新 Agent 命令验证器。
/// 校验请求结构（名称必填/最大长度）及 ChatOptions 参数范围和 Schema 合法性。
/// </summary>
public class UpdateAgentCommandValidator : AbstractValidator<UpdateAgentCommand>
{
    private static readonly string[] ValidResponseFormats = ["Text", "Json"];
    private static readonly string[] ValidToolModes = ["Auto", "Required", "None"];

    public UpdateAgentCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Agent ID is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");

        // ChatOptions validation when LlmConfig is present
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
