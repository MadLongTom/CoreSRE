using FluentValidation;

namespace CoreSRE.Application.Skills.Commands.RegisterSkill;

public class RegisterSkillCommandValidator : AbstractValidator<RegisterSkillCommand>
{
    private static readonly string[] ValidScopes = ["Builtin", "User", "Project"];

    public RegisterSkillCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(128);

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required.");

        RuleFor(x => x.Scope)
            .Must(s => ValidScopes.Contains(s))
            .WithMessage($"Scope must be one of: {string.Join(", ", ValidScopes)}.");
    }
}
