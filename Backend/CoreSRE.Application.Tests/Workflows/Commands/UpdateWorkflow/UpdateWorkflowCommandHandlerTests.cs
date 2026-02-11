using AutoMapper;
using CoreSRE.Application.Workflows.Commands.UpdateWorkflow;
using CoreSRE.Application.Workflows.DTOs;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace CoreSRE.Application.Tests.Workflows.Commands.UpdateWorkflow;

public class UpdateWorkflowCommandHandlerTests
{
    private readonly Mock<IWorkflowDefinitionRepository> _workflowRepoMock = new();
    private readonly Mock<IAgentRegistrationRepository> _agentRepoMock = new();
    private readonly Mock<IToolRegistrationRepository> _toolRepoMock = new();
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly UpdateWorkflowCommandHandler _handler;

    private static readonly Guid WorkflowId = Guid.NewGuid();

    public UpdateWorkflowCommandHandlerTests()
    {
        _handler = new UpdateWorkflowCommandHandler(
            _workflowRepoMock.Object,
            _agentRepoMock.Object,
            _toolRepoMock.Object,
            _mapperMock.Object);
    }

    private static WorkflowDefinition CreateDraftWorkflow(string name = "Original") =>
        WorkflowDefinition.Create(name, "desc", new WorkflowGraphVO
        {
            Nodes = [new WorkflowNodeVO { NodeId = "n1", NodeType = WorkflowNodeType.Condition, DisplayName = "N1" }]
        });

    private static UpdateWorkflowCommand BuildValidCommand(Guid? id = null) =>
        new()
        {
            Id = id ?? WorkflowId,
            Name = "Updated Name",
            Description = "Updated description",
            Graph = new WorkflowGraphDto
            {
                Nodes =
                [
                    new WorkflowNodeDto { NodeId = "n1", NodeType = "Condition", DisplayName = "Updated Node" },
                    new WorkflowNodeDto { NodeId = "n2", NodeType = "Condition", DisplayName = "New Node" }
                ],
                Edges =
                [
                    new WorkflowEdgeDto { EdgeId = "e1", SourceNodeId = "n1", TargetNodeId = "n2", EdgeType = "Normal" }
                ]
            }
        };

    [Fact]
    public async Task Handle_ValidUpdate_ReturnsOk()
    {
        // Arrange
        var workflow = CreateDraftWorkflow();
        _workflowRepoMock
            .Setup(r => r.GetByIdAsync(WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);
        _workflowRepoMock
            .Setup(r => r.ExistsWithNameAsync("Updated Name", WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var expectedDto = new WorkflowDefinitionDto { Id = WorkflowId, Name = "Updated Name", Status = "Draft" };
        _mapperMock
            .Setup(m => m.Map<WorkflowDefinitionDto>(It.IsAny<WorkflowDefinition>()))
            .Returns(expectedDto);

        // Act
        var result = await _handler.Handle(BuildValidCommand(), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Name.Should().Be("Updated Name");
        _workflowRepoMock.Verify(r => r.UpdateAsync(workflow, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotFound_Returns404()
    {
        // Arrange
        _workflowRepoMock
            .Setup(r => r.GetByIdAsync(WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinition?)null);

        // Act
        var result = await _handler.Handle(BuildValidCommand(), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_PublishedWorkflow_Returns400()
    {
        // Arrange
        var workflow = CreateDraftWorkflow();
        workflow.Publish(); // Now it's Published

        _workflowRepoMock
            .Setup(r => r.GetByIdAsync(WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);
        _workflowRepoMock
            .Setup(r => r.ExistsWithNameAsync(It.IsAny<string>(), WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(BuildValidCommand(), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("已发布");
    }

    [Fact]
    public async Task Handle_NameConflict_Returns409()
    {
        // Arrange
        var workflow = CreateDraftWorkflow();
        _workflowRepoMock
            .Setup(r => r.GetByIdAsync(WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);
        _workflowRepoMock
            .Setup(r => r.ExistsWithNameAsync("Updated Name", WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(BuildValidCommand(), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_DagCycle_Returns400()
    {
        // Arrange
        var workflow = CreateDraftWorkflow();
        _workflowRepoMock
            .Setup(r => r.GetByIdAsync(WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);
        _workflowRepoMock
            .Setup(r => r.ExistsWithNameAsync(It.IsAny<string>(), WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var command = BuildValidCommand() with
        {
            Graph = new WorkflowGraphDto
            {
                Nodes =
                [
                    new WorkflowNodeDto { NodeId = "a", NodeType = "Condition", DisplayName = "A" },
                    new WorkflowNodeDto { NodeId = "b", NodeType = "Condition", DisplayName = "B" }
                ],
                Edges =
                [
                    new WorkflowEdgeDto { EdgeId = "e1", SourceNodeId = "a", TargetNodeId = "b", EdgeType = "Normal" },
                    new WorkflowEdgeDto { EdgeId = "e2", SourceNodeId = "b", TargetNodeId = "a", EdgeType = "Normal" }
                ]
            }
        };

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("环路");
    }

    [Fact]
    public async Task Handle_InvalidReference_Returns400()
    {
        // Arrange
        var workflow = CreateDraftWorkflow();
        _workflowRepoMock
            .Setup(r => r.GetByIdAsync(WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);
        _workflowRepoMock
            .Setup(r => r.ExistsWithNameAsync(It.IsAny<string>(), WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var agentRefId = Guid.NewGuid();
        var command = BuildValidCommand() with
        {
            Graph = new WorkflowGraphDto
            {
                Nodes =
                [
                    new WorkflowNodeDto { NodeId = "agent-1", NodeType = "Agent", ReferenceId = agentRefId, DisplayName = "Agent" }
                ]
            }
        };

        _agentRepoMock
            .Setup(r => r.GetByIdAsync(agentRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRegistration?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Agent");
    }
}
