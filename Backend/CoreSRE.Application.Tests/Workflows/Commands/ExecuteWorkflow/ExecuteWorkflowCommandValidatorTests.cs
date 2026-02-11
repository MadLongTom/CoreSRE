using CoreSRE.Application.Workflows.Commands.ExecuteWorkflow;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace CoreSRE.Application.Tests.Workflows.Commands.ExecuteWorkflow;

public class ExecuteWorkflowCommandValidatorTests
{
    private readonly ExecuteWorkflowCommandValidator _validator = new();

    private static ExecuteWorkflowCommand BuildValidCommand() =>
        new()
        {
            WorkflowDefinitionId = Guid.NewGuid()
        };

    [Fact]
    public void Validate_ValidCommand_NoErrors()
    {
        var command = BuildValidCommand();
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyWorkflowDefinitionId_HasError()
    {
        var command = new ExecuteWorkflowCommand { WorkflowDefinitionId = Guid.Empty };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.WorkflowDefinitionId);
    }

    [Fact]
    public void Validate_NullInput_NoErrors()
    {
        // null Input is valid — handler defaults to {}
        var command = new ExecuteWorkflowCommand
        {
            WorkflowDefinitionId = Guid.NewGuid(),
            Input = null
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithInput_NoErrors()
    {
        var jsonDoc = System.Text.Json.JsonDocument.Parse("{\"key\":\"value\"}");
        var command = new ExecuteWorkflowCommand
        {
            WorkflowDefinitionId = Guid.NewGuid(),
            Input = jsonDoc.RootElement
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
