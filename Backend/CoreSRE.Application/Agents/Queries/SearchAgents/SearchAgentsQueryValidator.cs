using FluentValidation;

namespace CoreSRE.Application.Agents.Queries.SearchAgents;

/// <summary>
/// SearchAgentsQuery 验证器 — 查询参数校验
/// </summary>
public class SearchAgentsQueryValidator : AbstractValidator<SearchAgentsQuery>
{
    public SearchAgentsQueryValidator()
    {
        RuleFor(x => x.Query)
            .NotEmpty().WithMessage("'Query' must not be empty.")
            .MaximumLength(500).WithMessage("The length of 'Query' must be 500 characters or fewer.")
            .Must(q => !string.IsNullOrWhiteSpace(q)).WithMessage("'Query' must not be whitespace only.");
    }
}
