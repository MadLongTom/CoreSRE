using CoreSRE.Application.Workflows.Commands.DeleteWorkflow;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace CoreSRE.Application.Tests.Workflows.Commands.DeleteWorkflow;

public class DeleteWorkflowCommandHandlerTests
{
    private readonly Mock<IWorkflowDefinitionRepository> _repoMock = new();
    private readonly DeleteWorkflowCommandHandler _handler;

    private static readonly Guid WorkflowId = Guid.NewGuid();

    public DeleteWorkflowCommandHandlerTests()
    {
        _handler = new DeleteWorkflowCommandHandler(_repoMock.Object);
    }

    private static WorkflowDefinition CreateDraftWorkflow() =>
        WorkflowDefinition.Create("Test WF", null, new WorkflowGraphVO
        {
            Nodes = [new WorkflowNodeVO { NodeId = "n1", NodeType = WorkflowNodeType.Condition, DisplayName = "N1" }]
        });

    [Fact]
    public async Task Handle_DraftNotReferenced_DeletesSuccessfully()
    {
        // Arrange
        var workflow = CreateDraftWorkflow();
        _repoMock
            .Setup(r => r.GetByIdAsync(WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);
        _repoMock
            .Setup(r => r.IsReferencedByAgentAsync(WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(new DeleteWorkflowCommand(WorkflowId), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _repoMock.Verify(r => r.DeleteAsync(WorkflowId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotFound_Returns404()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdAsync(WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinition?)null);

        // Act
        var result = await _handler.Handle(new DeleteWorkflowCommand(WorkflowId), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_PublishedWorkflow_Returns400()
    {
        // Arrange
        var workflow = CreateDraftWorkflow();
        workflow.Publish();

        _repoMock
            .Setup(r => r.GetByIdAsync(WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        // Act
        var result = await _handler.Handle(new DeleteWorkflowCommand(WorkflowId), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("已发布");
    }

    [Fact]
    public async Task Handle_ReferencedByAgent_Returns400()
    {
        // Arrange
        var workflow = CreateDraftWorkflow();
        _repoMock
            .Setup(r => r.GetByIdAsync(WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);
        _repoMock
            .Setup(r => r.IsReferencedByAgentAsync(WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(new DeleteWorkflowCommand(WorkflowId), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Agent");
        result.Message.Should().Contain("引用");
    }
}
