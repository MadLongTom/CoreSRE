using FluentValidation;

namespace CoreSRE.Application.Agents.Queries.ResolveAgentCard;

/// <summary>
/// ResolveAgentCardQuery 验证器
/// </summary>
public class ResolveAgentCardQueryValidator : AbstractValidator<ResolveAgentCardQuery>
{
    public ResolveAgentCardQueryValidator()
    {
        RuleFor(x => x.Url)
            .NotEmpty().WithMessage("URL 不能为空")
            .MaximumLength(2048).WithMessage("URL 长度不能超过 2048 个字符")
            .Must(BeHttpOrHttps).WithMessage("URL 必须以 http:// 或 https:// 开头");
    }

    private static bool BeHttpOrHttps(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
