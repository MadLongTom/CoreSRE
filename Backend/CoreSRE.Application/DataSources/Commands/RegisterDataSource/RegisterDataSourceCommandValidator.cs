using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using FluentValidation;

namespace CoreSRE.Application.DataSources.Commands.RegisterDataSource;

public class RegisterDataSourceCommandValidator : AbstractValidator<RegisterDataSourceCommand>
{
    private static readonly string[] ValidCategories = Enum.GetNames<DataSourceCategory>();
    private static readonly string[] ValidProducts = Enum.GetNames<DataSourceProduct>();
    private static readonly string[] ValidAuthTypes = ["None", "ApiKey", "Bearer", "BasicAuth"];

    public RegisterDataSourceCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("Category is required.")
            .Must(c => ValidCategories.Contains(c, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Category must be one of: {string.Join(", ", ValidCategories)}.");

        RuleFor(x => x.Product)
            .NotEmpty().WithMessage("Product is required.")
            .Must(p => ValidProducts.Contains(p, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Product must be one of: {string.Join(", ", ValidProducts)}.");

        // Validate Category ↔ Product mapping
        RuleFor(x => x)
            .Must(x =>
            {
                if (!Enum.TryParse<DataSourceCategory>(x.Category, ignoreCase: true, out var cat)) return false;
                if (!Enum.TryParse<DataSourceProduct>(x.Product, ignoreCase: true, out var prod)) return false;
                return DataSourceRegistration.IsValidCategoryProduct(cat, prod);
            })
            .WithMessage(x => $"Product '{x.Product}' is not valid for category '{x.Category}'.");

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

            RuleFor(x => x.ConnectionConfig.Credential)
                .NotEmpty()
                .When(x => x.ConnectionConfig.AuthType is not "None")
                .WithMessage("Credential is required when AuthType is not None.");
        });
    }
}
