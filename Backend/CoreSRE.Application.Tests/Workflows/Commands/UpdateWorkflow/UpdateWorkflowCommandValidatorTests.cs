using CoreSRE.Application.Workflows.Commands.UpdateWorkflow;
using CoreSRE.Application.Workflows.DTOs;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace CoreSRE.Application.Tests.Workflows.Commands.UpdateWorkflow;

public class UpdateWorkflowCommandValidatorTests
{
    private readonly UpdateWorkflowCommandValidator _validator = new();

    private static UpdateWorkflowCommand BuildValidCommand() =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "Valid Workflow",
            Description = "A valid workflow",
            Graph = new WorkflowGraphDto
            {
                Nodes =
                [
                    new WorkflowNodeDto { NodeId = "node-1", NodeType = "Agent", DisplayName = "Agent Node" }
                ],
                Edges = []
            }
        };

    [Fact]
    public void Validate_ValidCommand_NoErrors()
    {
        var command = BuildValidCommand();
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_IdEmpty_HasError()
    {
        var command = BuildValidCommand() with { Id = Guid.Empty };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_NameEmpty_HasError(string? name)
    {
        var command = BuildValidCommand() with { Name = name! };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_NameTooLong_HasError()
    {
        var command = BuildValidCommand() with { Name = new string('x', 201) };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_GraphNull_HasError()
    {
        var command = BuildValidCommand() with { Graph = null! };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Graph);
    }

    [Fact]
    public void Validate_NodesEmpty_HasError()
    {
        var command = BuildValidCommand() with
        {
            Graph = new WorkflowGraphDto { Nodes = [], Edges = [] }
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor("Graph.Nodes");
    }
}
