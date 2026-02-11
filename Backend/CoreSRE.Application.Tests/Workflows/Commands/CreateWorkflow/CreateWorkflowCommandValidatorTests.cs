using CoreSRE.Application.Workflows.Commands.CreateWorkflow;
using CoreSRE.Application.Workflows.DTOs;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace CoreSRE.Application.Tests.Workflows.Commands.CreateWorkflow;

public class CreateWorkflowCommandValidatorTests
{
    private readonly CreateWorkflowCommandValidator _validator = new();

    private static CreateWorkflowCommand BuildValidCommand() =>
        new()
        {
            Name = "Valid Workflow",
            Description = "A valid workflow",
            Graph = new WorkflowGraphDto
            {
                Nodes =
                [
                    new WorkflowNodeDto
                    {
                        NodeId = "node-1",
                        NodeType = "Agent",
                        DisplayName = "Agent Node"
                    }
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
    public void Validate_NameExactly200_NoError()
    {
        var command = BuildValidCommand() with { Name = new string('x', 200) };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.Name);
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

    [Fact]
    public void Validate_NodeIdEmpty_HasError()
    {
        var command = BuildValidCommand() with
        {
            Graph = new WorkflowGraphDto
            {
                Nodes = [new WorkflowNodeDto { NodeId = "", NodeType = "Agent", DisplayName = "Test" }]
            }
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor("Graph.Nodes[0].NodeId");
    }

    [Fact]
    public void Validate_DisplayNameEmpty_HasError()
    {
        var command = BuildValidCommand() with
        {
            Graph = new WorkflowGraphDto
            {
                Nodes = [new WorkflowNodeDto { NodeId = "n1", NodeType = "Agent", DisplayName = "" }]
            }
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor("Graph.Nodes[0].DisplayName");
    }

    [Fact]
    public void Validate_InvalidNodeType_HasError()
    {
        var command = BuildValidCommand() with
        {
            Graph = new WorkflowGraphDto
            {
                Nodes = [new WorkflowNodeDto { NodeId = "n1", NodeType = "InvalidType", DisplayName = "Test" }]
            }
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor("Graph.Nodes[0].NodeType");
    }

    [Fact]
    public void Validate_EdgeIdEmpty_HasError()
    {
        var command = BuildValidCommand() with
        {
            Graph = new WorkflowGraphDto
            {
                Nodes =
                [
                    new WorkflowNodeDto { NodeId = "a", NodeType = "Agent", DisplayName = "A" },
                    new WorkflowNodeDto { NodeId = "b", NodeType = "Agent", DisplayName = "B" }
                ],
                Edges = [new WorkflowEdgeDto { EdgeId = "", SourceNodeId = "a", TargetNodeId = "b", EdgeType = "Normal" }]
            }
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor("Graph.Edges[0].EdgeId");
    }

    [Fact]
    public void Validate_SourceNodeIdEmpty_HasError()
    {
        var command = BuildValidCommand() with
        {
            Graph = new WorkflowGraphDto
            {
                Nodes = [new WorkflowNodeDto { NodeId = "a", NodeType = "Agent", DisplayName = "A" }],
                Edges = [new WorkflowEdgeDto { EdgeId = "e1", SourceNodeId = "", TargetNodeId = "a", EdgeType = "Normal" }]
            }
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor("Graph.Edges[0].SourceNodeId");
    }

    [Fact]
    public void Validate_InvalidEdgeType_HasError()
    {
        var command = BuildValidCommand() with
        {
            Graph = new WorkflowGraphDto
            {
                Nodes =
                [
                    new WorkflowNodeDto { NodeId = "a", NodeType = "Agent", DisplayName = "A" },
                    new WorkflowNodeDto { NodeId = "b", NodeType = "Agent", DisplayName = "B" }
                ],
                Edges = [new WorkflowEdgeDto { EdgeId = "e1", SourceNodeId = "a", TargetNodeId = "b", EdgeType = "Invalid" }]
            }
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor("Graph.Edges[0].EdgeType");
    }

    [Fact]
    public void Validate_ConditionalEdgeMissingCondition_HasError()
    {
        var command = BuildValidCommand() with
        {
            Graph = new WorkflowGraphDto
            {
                Nodes =
                [
                    new WorkflowNodeDto { NodeId = "a", NodeType = "Agent", DisplayName = "A" },
                    new WorkflowNodeDto { NodeId = "b", NodeType = "Agent", DisplayName = "B" }
                ],
                Edges = [new WorkflowEdgeDto { EdgeId = "e1", SourceNodeId = "a", TargetNodeId = "b", EdgeType = "Conditional", Condition = null }]
            }
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor("Graph.Edges[0].Condition");
    }

    [Fact]
    public void Validate_ConditionalEdgeWithCondition_NoError()
    {
        var command = BuildValidCommand() with
        {
            Graph = new WorkflowGraphDto
            {
                Nodes =
                [
                    new WorkflowNodeDto { NodeId = "a", NodeType = "Condition", DisplayName = "A" },
                    new WorkflowNodeDto { NodeId = "b", NodeType = "Agent", DisplayName = "B" }
                ],
                Edges = [new WorkflowEdgeDto { EdgeId = "e1", SourceNodeId = "a", TargetNodeId = "b", EdgeType = "Conditional", Condition = "status == 'critical'" }]
            }
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("Agent")]
    [InlineData("Tool")]
    [InlineData("Condition")]
    [InlineData("FanOut")]
    [InlineData("FanIn")]
    public void Validate_ValidNodeTypes_NoError(string nodeType)
    {
        var command = BuildValidCommand() with
        {
            Graph = new WorkflowGraphDto
            {
                Nodes = [new WorkflowNodeDto { NodeId = "n1", NodeType = nodeType, DisplayName = "Test" }]
            }
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor("Graph.Nodes[0].NodeType");
    }
}
