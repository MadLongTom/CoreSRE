using CoreSRE.Application.AlertRules.DTOs;
using FluentValidation;

namespace CoreSRE.Application.AlertRules.Commands.CreateAlertRule;

public class CreateAlertRuleCommandValidator : AbstractValidator<CreateAlertRuleCommand>
{
    private static readonly string[] ValidSeverities = ["P1", "P2", "P3", "P4"];
    private static readonly string[] ValidOperators = ["Eq", "Neq", "Regex", "NotRegex"];

    public CreateAlertRuleCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(256);

        RuleFor(x => x.Matchers)
            .NotEmpty().WithMessage("At least one matcher is required.");

        RuleForEach(x => x.Matchers).ChildRules(m =>
        {
            m.RuleFor(x => x.Label).NotEmpty();
            m.RuleFor(x => x.Operator)
                .Must(op => ValidOperators.Contains(op))
                .WithMessage("Operator must be one of: Eq, Neq, Regex, NotRegex");
            m.RuleFor(x => x.Value).NotEmpty();
        });

        RuleFor(x => x.Severity)
            .Must(s => ValidSeverities.Contains(s))
            .WithMessage("Severity must be one of: P1, P2, P3, P4");

        // SopId 和 TeamAgentId 互斥
        RuleFor(x => x)
            .Must(x => !(x.SopId.HasValue && x.TeamAgentId.HasValue))
            .WithMessage("SopId and TeamAgentId are mutually exclusive.");

        RuleFor(x => x.CooldownMinutes)
            .GreaterThanOrEqualTo(0);
    }
}
