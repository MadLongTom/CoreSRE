using FluentValidation;

namespace CoreSRE.Application.Tools.Commands.ImportOpenApi;

/// <summary>
/// OpenAPI 导入命令验证器
/// </summary>
public class ImportOpenApiCommandValidator : AbstractValidator<ImportOpenApiCommand>
{
    public ImportOpenApiCommandValidator()
    {
        RuleFor(x => x.Document)
            .NotNull().WithMessage("Document stream is required.")
            .Must(s => s != Stream.Null && s.CanRead)
            .WithMessage("Document stream must be readable and not empty.");
    }
}
