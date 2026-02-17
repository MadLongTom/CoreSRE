using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using FluentValidation;

namespace CoreSRE.Application.DataSources.Commands.UpdateDataSource;

public class UpdateDataSourceCommandValidator : AbstractValidator<UpdateDataSourceCommand>
{
    private static readonly string[] ValidAuthTypes = ["None", "ApiKey", "Bearer", "BasicAuth"];

    public UpdateDataSourceCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");

        RuleFor(x => x.ConnectionConfig)
            .NotNull().WithMessage("ConnectionConfig is required.");

        When(x => x.ConnectionConfig is not null, () =>
        {
            RuleFor(x => x.ConnectionConfig.BaseUrl)
                .NotEmpty().WithMessage("BaseUrl is required.")
                .MaximumLength(2048).WithMessage("BaseUrl must not exceed 2048 characters.");

            RuleFor(x => x.ConnectionConfig.AuthType)
                .Must(a => ValidAuthTypes.Contains(a, StringComparer.OrdinalIgnoreCase))
                .WithMessage($"AuthType must be one of: {string.Join(", ", ValidAuthTypes)}.");

            RuleFor(x => x.ConnectionConfig.TimeoutSeconds)
                .InclusiveBetween(1, 300)
                .When(x => x.ConnectionConfig.TimeoutSeconds > 0)
                .WithMessage("TimeoutSeconds must be between 1 and 300.");
        });
    }
}
