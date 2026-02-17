using System.Text.Json;
using FluentValidation;

namespace CoreSRE.Application.Agents.Commands.RegisterAgent;

/// <summary>
/// 注册 Agent 命令验证器 — 请求结构校验
/// </summary>
public class RegisterAgentCommandValidator : AbstractValidator<RegisterAgentCommand>
{
    private static readonly string[] ValidAgentTypes = ["A2A", "ChatClient", "Workflow", "Team"];
    private static readonly string[] ValidResponseFormats = ["Text", "Json"];
    private static readonly string[] ValidToolModes = ["Auto", "Required", "None"];
    private static readonly string[] ValidTeamModes = ["Sequential", "Concurrent", "RoundRobin", "Handoffs", "Selector", "MagneticOne"];

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

        // Team type-specific rules
        When(x => x.AgentType == "Team", () =>
        {
            RuleFor(x => x.TeamConfig)
                .NotNull().WithMessage("TeamConfig is required for Team agents.");

            When(x => x.TeamConfig is not null, () =>
            {
                RuleFor(x => x.TeamConfig!.Mode)
                    .NotEmpty().WithMessage("TeamConfig.Mode is required.")
                    .Must(m => ValidTeamModes.Contains(m))
                    .WithMessage($"TeamConfig.Mode must be one of: {string.Join(", ", ValidTeamModes)}.");

                RuleFor(x => x.TeamConfig!.ParticipantIds)
                    .NotEmpty().WithMessage("ParticipantIds must not be empty.");

                RuleFor(x => x.TeamConfig!)
                    .Must(tc => tc.ParticipantIds.All(id => id != Guid.Empty))
                    .WithMessage("ParticipantIds must not contain empty GUIDs.")
                    .When(x => x.TeamConfig!.ParticipantIds.Count > 0);

                RuleFor(x => x.TeamConfig!.MaxIterations)
                    .GreaterThan(0).WithMessage("MaxIterations must be greater than 0.");

                // Sequential / Concurrent / RoundRobin: at least 2 participants
                When(x => x.TeamConfig!.Mode is "Sequential" or "Concurrent" or "RoundRobin", () =>
                {
                    RuleFor(x => x.TeamConfig!.ParticipantIds)
                        .Must(ids => ids.Count >= 2)
                        .WithMessage(x => $"{x.TeamConfig!.Mode} Team requires at least 2 participants.");
                });

                // Selector: at least 2 participants + provider/model
                When(x => x.TeamConfig!.Mode == "Selector", () =>
                {
                    RuleFor(x => x.TeamConfig!.ParticipantIds)
                        .Must(ids => ids.Count >= 2)
                        .WithMessage("Selector Team requires at least 2 participants.");

                    RuleFor(x => x.TeamConfig!.SelectorProviderId)
                        .NotNull().WithMessage("SelectorProviderId is required for Selector mode.");

                    RuleFor(x => x.TeamConfig!.SelectorModelId)
                        .NotEmpty().WithMessage("SelectorModelId is required for Selector mode.");
                });

                // Handoffs: InitialAgentId + routes
                When(x => x.TeamConfig!.Mode == "Handoffs", () =>
                {
                    RuleFor(x => x.TeamConfig!.InitialAgentId)
                        .NotNull().WithMessage("InitialAgentId is required for Handoffs mode.");

                    RuleFor(x => x.TeamConfig!)
                        .Must(tc => tc.InitialAgentId.HasValue && tc.ParticipantIds.Contains(tc.InitialAgentId.Value))
                        .WithMessage("InitialAgentId must be one of the ParticipantIds.")
                        .When(x => x.TeamConfig!.InitialAgentId.HasValue);

                    RuleFor(x => x.TeamConfig!.HandoffRoutes)
                        .NotNull().WithMessage("HandoffRoutes is required for Handoffs mode.");

                    RuleFor(x => x.TeamConfig!.HandoffRoutes)
                        .Must(r => r!.Count > 0).WithMessage("HandoffRoutes must not be empty.")
                        .When(x => x.TeamConfig!.HandoffRoutes is not null);
                });

                // MagneticOne: orchestrator provider/model
                When(x => x.TeamConfig!.Mode == "MagneticOne", () =>
                {
                    RuleFor(x => x.TeamConfig!.OrchestratorProviderId)
                        .NotNull().WithMessage("OrchestratorProviderId is required for MagneticOne mode.");

                    RuleFor(x => x.TeamConfig!.OrchestratorModelId)
                        .NotEmpty().WithMessage("OrchestratorModelId is required for MagneticOne mode.");
                });
            });
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
