using FluentValidation;

namespace CoreSRE.Application.Sandboxes.Commands.CreateSandbox;

public class CreateSandboxCommandValidator : AbstractValidator<CreateSandboxCommand>
{
    private static readonly string[] ValidSandboxTypes =
        ["SimpleBox", "CodeBox", "InteractiveBox", "BrowserBox", "ComputerBox"];

    public CreateSandboxCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(128);

        RuleFor(x => x.SandboxType)
            .Must(t => ValidSandboxTypes.Contains(t))
            .WithMessage($"SandboxType must be one of: {string.Join(", ", ValidSandboxTypes)}.");

        RuleFor(x => x.CpuCores)
            .InclusiveBetween(1, 8);

        RuleFor(x => x.MemoryMib)
            .InclusiveBetween(128, 16384);

        RuleFor(x => x.AutoStopMinutes)
            .InclusiveBetween(0, 1440);
    }
}
