using CoreSRE.Application.Workflows.Commands.DeleteWorkflow;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace CoreSRE.Application.Tests.Workflows.Commands.DeleteWorkflow;

public class DeleteWorkflowCommandValidatorTests
{
    private readonly DeleteWorkflowCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidId_NoErrors()
    {
        var command = new DeleteWorkflowCommand(Guid.NewGuid());
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyId_HasError()
    {
        var command = new DeleteWorkflowCommand(Guid.Empty);
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Id);
    }
}
