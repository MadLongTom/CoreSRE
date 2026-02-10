using FluentValidation;

namespace CoreSRE.Application.Providers.Commands.UpdateProvider;

/// <summary>
/// 更新 LLM Provider 命令验证器
/// </summary>
public class UpdateProviderCommandValidator : AbstractValidator<UpdateProviderCommand>
{
    public UpdateProviderCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("名称不能为空")
            .MaximumLength(200).WithMessage("名称不能超过 200 个字符");

        RuleFor(x => x.BaseUrl)
            .NotEmpty().WithMessage("Base URL 不能为空")
            .MaximumLength(500).WithMessage("Base URL 不能超过 500 个字符")
            .Must(url => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .When(x => !string.IsNullOrEmpty(x.BaseUrl))
            .WithMessage("Base URL 必须以 http:// 或 https:// 开头");
    }
}
