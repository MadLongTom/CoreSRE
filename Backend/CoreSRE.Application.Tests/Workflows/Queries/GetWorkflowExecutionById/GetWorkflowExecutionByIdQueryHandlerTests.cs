using System.Text.Json;
using AutoMapper;
using CoreSRE.Application.Workflows.DTOs;
using CoreSRE.Application.Workflows.Queries.GetWorkflowExecutionById;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace CoreSRE.Application.Tests.Workflows.Queries.GetWorkflowExecutionById;

public class GetWorkflowExecutionByIdQueryHandlerTests
{
    private readonly Mock<IWorkflowDefinitionRepository> _workflowRepoMock = new();
    private readonly Mock<IWorkflowExecutionRepository> _executionRepoMock = new();
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly GetWorkflowExecutionByIdQueryHandler _handler;

    public GetWorkflowExecutionByIdQueryHandlerTests()
    {
        _handler = new GetWorkflowExecutionByIdQueryHandler(
            _workflowRepoMock.Object,
            _executionRepoMock.Object,
            _mapperMock.Object);
    }

    [Fact]
    public async Task Handle_Found_ReturnsFullExecutionDetail()
    {
        // Arrange
        var workflowId = Guid.NewGuid();
        var workflow = WorkflowDefinition.Create("Test", null,
            new WorkflowGraphVO { Nodes = [new WorkflowNodeVO { NodeId = "n1", NodeType = WorkflowNodeType.Agent, DisplayName = "N" }] });

        _workflowRepoMock.Setup(r => r.GetByIdAsync(workflowId, It.IsAny<CancellationToken>())).ReturnsAsync(workflow);

        var graph = new WorkflowGraphVO
        {
            Nodes = [new WorkflowNodeVO { NodeId = "n1", NodeType = WorkflowNodeType.Agent, DisplayName = "N" }],
            Edges = []
        };
        var execution = WorkflowExecution.Create(workflowId, JsonDocument.Parse("{}").RootElement, graph);
        var executionId = execution.Id;

        _executionRepoMock.Setup(r => r.GetByIdAsync(executionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        var dto = new WorkflowExecutionDto
        {
            Id = executionId,
            WorkflowDefinitionId = workflowId,
            Status = "Pending",
            NodeExecutions = [new NodeExecutionDto { NodeId = "n1", Status = "Pending" }]
        };
        _mapperMock.Setup(m => m.Map<WorkflowExecutionDto>(execution)).Returns(dto);

        // Act
        var result = await _handler.Handle(
            new GetWorkflowExecutionByIdQuery(workflowId, executionId), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().Be(executionId);
        result.Data.NodeExecutions.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_ExecutionNotFound_Returns404()
    {
        // Arrange
        var workflowId = Guid.NewGuid();
        var workflow = WorkflowDefinition.Create("Test", null,
            new WorkflowGraphVO { Nodes = [new WorkflowNodeVO { NodeId = "n1", NodeType = WorkflowNodeType.Agent, DisplayName = "N" }] });

        _workflowRepoMock.Setup(r => r.GetByIdAsync(workflowId, It.IsAny<CancellationToken>())).ReturnsAsync(workflow);
        _executionRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowExecution?)null);

        // Act
        var result = await _handler.Handle(
            new GetWorkflowExecutionByIdQuery(workflowId, Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_WorkflowNotFound_Returns404()
    {
        // Arrange
        _workflowRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinition?)null);

        // Act
        var result = await _handler.Handle(
            new GetWorkflowExecutionByIdQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(404);
    }
}
