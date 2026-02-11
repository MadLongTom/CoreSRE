using FluentValidation;

namespace CoreSRE.Application.Tools.Commands.UpdateTool;

/// <summary>
/// 更新工具命令验证器 — 请求结构校验（不包含 ToolType，因为 ToolType 不可变更）
/// </summary>
public class UpdateToolCommandValidator : AbstractValidator<UpdateToolCommand>
{
    private static readonly string[] ValidAuthTypes = ["None", "ApiKey", "Bearer", "OAuth2"];
    private static readonly string[] ValidTransportTypes = ["Rest", "StreamableHttp", "Stdio", "Sse", "AutoDetect"];
    private static readonly string[] ValidHttpMethods = ["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"];

    public UpdateToolCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");

        RuleFor(x => x.ConnectionConfig)
            .NotNull().WithMessage("ConnectionConfig is required.");

        RuleFor(x => x.ConnectionConfig.Endpoint)
            .NotEmpty().WithMessage("Endpoint is required.")
            .MaximumLength(2048).WithMessage("Endpoint must not exceed 2048 characters.")
            .When(x => x.ConnectionConfig is not null);

        RuleFor(x => x.ConnectionConfig.TransportType)
            .NotEmpty().WithMessage("TransportType is required.")
            .Must(t => ValidTransportTypes.Contains(t))
            .WithMessage($"TransportType must be one of: {string.Join(", ", ValidTransportTypes)}.")
            .When(x => x.ConnectionConfig is not null);

        // HttpMethod validation (for REST tools)
        RuleFor(x => x.ConnectionConfig.HttpMethod)
            .Must(m => ValidHttpMethods.Contains(m!.ToUpperInvariant()))
            .WithMessage($"HttpMethod must be one of: {string.Join(", ", ValidHttpMethods)}.")
            .When(x => x.ConnectionConfig is not null && !string.IsNullOrEmpty(x.ConnectionConfig.HttpMethod));

        // AuthConfig validation
        RuleFor(x => x.AuthConfig.AuthType)
            .Must(t => ValidAuthTypes.Contains(t))
            .WithMessage($"AuthType must be one of: {string.Join(", ", ValidAuthTypes)}.")
            .When(x => x.AuthConfig is not null);

        // ApiKey/Bearer require Credential
        When(x => x.AuthConfig is not null && x.AuthConfig.AuthType is "ApiKey" or "Bearer", () =>
        {
            RuleFor(x => x.AuthConfig.Credential)
                .NotEmpty().WithMessage("Credential is required for ApiKey/Bearer authentication.");
        });

        // OAuth2 requires ClientId, ClientSecret, TokenEndpoint
        When(x => x.AuthConfig is not null && x.AuthConfig.AuthType == "OAuth2", () =>
        {
            RuleFor(x => x.AuthConfig.ClientId)
                .NotEmpty().WithMessage("ClientId is required for OAuth2 authentication.");

            RuleFor(x => x.AuthConfig.ClientSecret)
                .NotEmpty().WithMessage("ClientSecret is required for OAuth2 authentication.");

            RuleFor(x => x.AuthConfig.TokenEndpoint)
                .NotEmpty().WithMessage("TokenEndpoint is required for OAuth2 authentication.");
        });
    }
}
