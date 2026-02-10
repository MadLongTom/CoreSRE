using FluentValidation;

namespace CoreSRE.Application.Providers.Commands.RegisterProvider;

/// <summary>
/// 注册 Provider 命令验证器
/// </summary>
public class RegisterProviderCommandValidator : AbstractValidator<RegisterProviderCommand>
{
    public RegisterProviderCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");

        RuleFor(x => x.BaseUrl)
            .NotEmpty().WithMessage("BaseUrl is required.")
            .MaximumLength(500).WithMessage("BaseUrl must not exceed 500 characters.")
            .Must(url => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .WithMessage("BaseUrl must start with http:// or https://.");

        RuleFor(x => x.ApiKey)
            .NotEmpty().WithMessage("ApiKey is required.");
    }
}
