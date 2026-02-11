using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Workflows.Commands.CreateWorkflow;
using CoreSRE.Application.Workflows.DTOs;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace CoreSRE.Application.Tests.Workflows.Commands.CreateWorkflow;

public class CreateWorkflowCommandHandlerTests
{
    private readonly Mock<IWorkflowDefinitionRepository> _workflowRepoMock = new();
    private readonly Mock<IAgentRegistrationRepository> _agentRepoMock = new();
    private readonly Mock<IToolRegistrationRepository> _toolRepoMock = new();
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly CreateWorkflowCommandHandler _handler;

    // Dummy entities for reference validation (handler only checks null vs non-null)
    private static readonly AgentRegistration DummyAgent = AgentRegistration.CreateA2A(
        "dummy-agent", null, "https://dummy.example.com", new AgentCardVO());

    private static readonly ToolRegistration DummyTool = ToolRegistration.CreateRestApi(
        "dummy-tool", null, "https://dummy.example.com",
        new AuthConfigVO { AuthType = AuthType.None }, "POST");

    public CreateWorkflowCommandHandlerTests()
    {
        _handler = new CreateWorkflowCommandHandler(
            _workflowRepoMock.Object,
            _agentRepoMock.Object,
            _toolRepoMock.Object,
            _mapperMock.Object);
    }

    private static CreateWorkflowCommand BuildValidCommand(
        string name = "Test Workflow",
        string? description = "A test workflow") =>
        new()
        {
            Name = name,
            Description = description,
            Graph = new WorkflowGraphDto
            {
                Nodes =
                [
                    new WorkflowNodeDto
                    {
                        NodeId = "agent-1",
                        NodeType = "Agent",
                        ReferenceId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        DisplayName = "Agent Node"
                    },
                    new WorkflowNodeDto
                    {
                        NodeId = "tool-1",
                        NodeType = "Tool",
                        ReferenceId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                        DisplayName = "Tool Node"
                    }
                ],
                Edges =
                [
                    new WorkflowEdgeDto
                    {
                        EdgeId = "e1",
                        SourceNodeId = "agent-1",
                        TargetNodeId = "tool-1",
                        EdgeType = "Normal"
                    }
                ]
            }
        };

    private void SetupReferencesExist()
    {
        _agentRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DummyAgent);
        _toolRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DummyTool);
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesDraftWorkflow()
    {
        // Arrange
        var command = BuildValidCommand();
        _workflowRepoMock
            .Setup(r => r.ExistsWithNameAsync(command.Name, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        SetupReferencesExist();

        var expectedDto = new WorkflowDefinitionDto
        {
            Id = Guid.NewGuid(),
            Name = command.Name,
            Description = command.Description,
            Status = "Draft"
        };
        _mapperMock
            .Setup(m => m.Map<WorkflowDefinitionDto>(It.IsAny<WorkflowDefinition>()))
            .Returns(expectedDto);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Name.Should().Be(command.Name);
        result.Data.Status.Should().Be("Draft");

        _workflowRepoMock.Verify(
            r => r.AddAsync(It.IsAny<WorkflowDefinition>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NameConflict_Returns409()
    {
        // Arrange
        var command = BuildValidCommand();
        _workflowRepoMock
            .Setup(r => r.ExistsWithNameAsync(command.Name, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(409);
        result.Message.Should().Contain(command.Name);

        _workflowRepoMock.Verify(
            r => r.AddAsync(It.IsAny<WorkflowDefinition>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_InvalidAgentReference_Returns400()
    {
        // Arrange
        var command = BuildValidCommand();
        _workflowRepoMock
            .Setup(r => r.ExistsWithNameAsync(command.Name, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Agent not found
        _agentRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRegistration?)null);

        // Tool exists
        _toolRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DummyTool);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Agent");
        result.Message.Should().Contain("agent-1");
    }

    [Fact]
    public async Task Handle_InvalidToolReference_Returns400()
    {
        // Arrange
        var command = BuildValidCommand();
        _workflowRepoMock
            .Setup(r => r.ExistsWithNameAsync(command.Name, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Agent exists
        _agentRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DummyAgent);

        // Tool not found
        _toolRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ToolRegistration?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Tool");
        result.Message.Should().Contain("tool-1");
    }

    [Fact]
    public async Task Handle_DagCycleDetected_Returns400()
    {
        // Arrange — create a cycle: A → B → A
        var command = new CreateWorkflowCommand
        {
            Name = "Cycle Workflow",
            Graph = new WorkflowGraphDto
            {
                Nodes =
                [
                    new WorkflowNodeDto { NodeId = "a", NodeType = "Condition", DisplayName = "Node A" },
                    new WorkflowNodeDto { NodeId = "b", NodeType = "Condition", DisplayName = "Node B" }
                ],
                Edges =
                [
                    new WorkflowEdgeDto { EdgeId = "e1", SourceNodeId = "a", TargetNodeId = "b", EdgeType = "Normal" },
                    new WorkflowEdgeDto { EdgeId = "e2", SourceNodeId = "b", TargetNodeId = "a", EdgeType = "Normal" }
                ]
            }
        };

        _workflowRepoMock
            .Setup(r => r.ExistsWithNameAsync(command.Name, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("环路");
    }

    [Fact]
    public async Task Handle_OrphanNode_Returns400()
    {
        // Arrange — 3 nodes but only 2 connected
        var command = new CreateWorkflowCommand
        {
            Name = "Orphan Workflow",
            Graph = new WorkflowGraphDto
            {
                Nodes =
                [
                    new WorkflowNodeDto { NodeId = "a", NodeType = "Condition", DisplayName = "Node A" },
                    new WorkflowNodeDto { NodeId = "b", NodeType = "Condition", DisplayName = "Node B" },
                    new WorkflowNodeDto { NodeId = "c", NodeType = "Condition", DisplayName = "Orphan Node" }
                ],
                Edges =
                [
                    new WorkflowEdgeDto { EdgeId = "e1", SourceNodeId = "a", TargetNodeId = "b", EdgeType = "Normal" }
                ]
            }
        };

        _workflowRepoMock
            .Setup(r => r.ExistsWithNameAsync(command.Name, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("孤立节点");
    }

    [Fact]
    public async Task Handle_NullGraph_ThrowsOrReturnsFailure()
    {
        // This scenario would normally be caught by FluentValidation
        // before reaching the handler, but we test defensive coding
        var command = new CreateWorkflowCommand
        {
            Name = "No Graph",
            Graph = new WorkflowGraphDto { Nodes = [], Edges = [] }
        };

        _workflowRepoMock
            .Setup(r => r.ExistsWithNameAsync(command.Name, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert — empty nodes triggers DAG validation error
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ConditionNodesSkipReferenceValidation()
    {
        // Arrange — Condition/FanOut/FanIn nodes don't need a referenceId
        var command = new CreateWorkflowCommand
        {
            Name = "Condition Workflow",
            Graph = new WorkflowGraphDto
            {
                Nodes =
                [
                    new WorkflowNodeDto { NodeId = "cond-1", NodeType = "Condition", DisplayName = "Check Status" },
                    new WorkflowNodeDto { NodeId = "fan-out", NodeType = "FanOut", DisplayName = "Fan Out" },
                    new WorkflowNodeDto { NodeId = "fan-in", NodeType = "FanIn", DisplayName = "Fan In" }
                ],
                Edges =
                [
                    new WorkflowEdgeDto { EdgeId = "e1", SourceNodeId = "cond-1", TargetNodeId = "fan-out", EdgeType = "Normal" },
                    new WorkflowEdgeDto { EdgeId = "e2", SourceNodeId = "fan-out", TargetNodeId = "fan-in", EdgeType = "Normal" }
                ]
            }
        };

        _workflowRepoMock
            .Setup(r => r.ExistsWithNameAsync(command.Name, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var expectedDto = new WorkflowDefinitionDto
        {
            Id = Guid.NewGuid(),
            Name = command.Name,
            Status = "Draft"
        };
        _mapperMock
            .Setup(m => m.Map<WorkflowDefinitionDto>(It.IsAny<WorkflowDefinition>()))
            .Returns(expectedDto);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        // No agent/tool repo calls expected for Condition/FanOut/FanIn nodes
        _agentRepoMock.Verify(
            r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _toolRepoMock.Verify(
            r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
