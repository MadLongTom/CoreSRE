using FluentValidation;

namespace CoreSRE.Application.Skills.Commands.RegisterSkill;

public class RegisterSkillCommandValidator : AbstractValidator<RegisterSkillCommand>
{
    private static readonly string[] ValidScopes = ["Builtin", "User", "Project"];

    public RegisterSkillCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(64).WithMessage("Name must be at most 64 characters.")
            .Matches(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?$")
            .WithMessage("Name must contain only lowercase letters, numbers, and hyphens. Must not start/end with hyphen.")
            .Must(n => !n.Contains("--"))
            .WithMessage("Name must not contain consecutive hyphens.");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required.")
            .MaximumLength(1024).WithMessage("Description must be at most 1024 characters.");

        RuleFor(x => x.Compatibility)
            .MaximumLength(500).WithMessage("Compatibility must be at most 500 characters.")
            .When(x => x.Compatibility is not null);

        RuleFor(x => x.License)
            .MaximumLength(256).WithMessage("License must be at most 256 characters.")
            .When(x => x.License is not null);

        RuleFor(x => x.Scope)
            .Must(s => ValidScopes.Contains(s))
            .WithMessage($"Scope must be one of: {string.Join(", ", ValidScopes)}.");
    }
}
