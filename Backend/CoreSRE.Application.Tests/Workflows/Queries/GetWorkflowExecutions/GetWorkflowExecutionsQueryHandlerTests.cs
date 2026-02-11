using System.Text.Json;
using AutoMapper;
using CoreSRE.Application.Workflows.DTOs;
using CoreSRE.Application.Workflows.Queries.GetWorkflowExecutions;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace CoreSRE.Application.Tests.Workflows.Queries.GetWorkflowExecutions;

public class GetWorkflowExecutionsQueryHandlerTests
{
    private readonly Mock<IWorkflowDefinitionRepository> _workflowRepoMock = new();
    private readonly Mock<IWorkflowExecutionRepository> _executionRepoMock = new();
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly GetWorkflowExecutionsQueryHandler _handler;

    public GetWorkflowExecutionsQueryHandlerTests()
    {
        _handler = new GetWorkflowExecutionsQueryHandler(
            _workflowRepoMock.Object,
            _executionRepoMock.Object,
            _mapperMock.Object);
    }

    private static WorkflowExecution CreateExecution(Guid workflowId, ExecutionStatus status)
    {
        var graph = new WorkflowGraphVO
        {
            Nodes = [new WorkflowNodeVO { NodeId = "n1", NodeType = WorkflowNodeType.Agent, DisplayName = "N1" }],
            Edges = []
        };
        var input = JsonDocument.Parse("{}").RootElement;
        var execution = WorkflowExecution.Create(workflowId, input, graph);

        if (status >= ExecutionStatus.Running)
        {
            execution.Start();
        }
        if (status == ExecutionStatus.Completed)
        {
            execution.StartNode("n1");
            execution.CompleteNode("n1", "{}");
            execution.Complete(JsonDocument.Parse("{}").RootElement);
        }
        else if (status == ExecutionStatus.Failed)
        {
            execution.Fail("test failure");
        }

        return execution;
    }

    [Fact]
    public async Task Handle_ValidWorkflow_ReturnsExecutionsList()
    {
        // Arrange
        var workflowId = Guid.NewGuid();
        var workflow = WorkflowDefinition.Create("Test", null,
            new WorkflowGraphVO { Nodes = [new WorkflowNodeVO { NodeId = "n1", NodeType = WorkflowNodeType.Agent, DisplayName = "N" }] });

        _workflowRepoMock.Setup(r => r.GetByIdAsync(workflowId, It.IsAny<CancellationToken>())).ReturnsAsync(workflow);

        var executions = new[]
        {
            CreateExecution(workflowId, ExecutionStatus.Completed),
            CreateExecution(workflowId, ExecutionStatus.Running)
        };
        _executionRepoMock.Setup(r => r.GetByWorkflowIdAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(executions);

        var dtos = new List<WorkflowExecutionSummaryDto>
        {
            new() { Id = Guid.NewGuid(), Status = "Completed" },
            new() { Id = Guid.NewGuid(), Status = "Running" }
        };
        _mapperMock.Setup(m => m.Map<List<WorkflowExecutionSummaryDto>>(It.IsAny<IEnumerable<WorkflowExecution>>()))
            .Returns(dtos);

        // Act
        var result = await _handler.Handle(new GetWorkflowExecutionsQuery(workflowId), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_FilterByStatus_ReturnsFilteredList()
    {
        // Arrange
        var workflowId = Guid.NewGuid();
        var workflow = WorkflowDefinition.Create("Test", null,
            new WorkflowGraphVO { Nodes = [new WorkflowNodeVO { NodeId = "n1", NodeType = WorkflowNodeType.Agent, DisplayName = "N" }] });

        _workflowRepoMock.Setup(r => r.GetByIdAsync(workflowId, It.IsAny<CancellationToken>())).ReturnsAsync(workflow);

        var executions = new[]
        {
            CreateExecution(workflowId, ExecutionStatus.Running),
            CreateExecution(workflowId, ExecutionStatus.Running),
            CreateExecution(workflowId, ExecutionStatus.Completed)
        };
        _executionRepoMock.Setup(r => r.GetByWorkflowIdAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(executions);

        var filteredDtos = new List<WorkflowExecutionSummaryDto>
        {
            new() { Id = Guid.NewGuid(), Status = "Running" },
            new() { Id = Guid.NewGuid(), Status = "Running" }
        };
        _mapperMock.Setup(m => m.Map<List<WorkflowExecutionSummaryDto>>(It.IsAny<IEnumerable<WorkflowExecution>>()))
            .Returns(filteredDtos);

        // Act
        var result = await _handler.Handle(
            new GetWorkflowExecutionsQuery(workflowId, "Running"), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_NoExecutions_ReturnsEmptyList()
    {
        // Arrange
        var workflowId = Guid.NewGuid();
        var workflow = WorkflowDefinition.Create("Test", null,
            new WorkflowGraphVO { Nodes = [new WorkflowNodeVO { NodeId = "n1", NodeType = WorkflowNodeType.Agent, DisplayName = "N" }] });

        _workflowRepoMock.Setup(r => r.GetByIdAsync(workflowId, It.IsAny<CancellationToken>())).ReturnsAsync(workflow);
        _executionRepoMock.Setup(r => r.GetByWorkflowIdAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<WorkflowExecution>());

        _mapperMock.Setup(m => m.Map<List<WorkflowExecutionSummaryDto>>(It.IsAny<IEnumerable<WorkflowExecution>>()))
            .Returns([]);

        // Act
        var result = await _handler.Handle(new GetWorkflowExecutionsQuery(workflowId), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WorkflowNotFound_Returns404()
    {
        // Arrange
        var workflowId = Guid.NewGuid();
        _workflowRepoMock.Setup(r => r.GetByIdAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinition?)null);

        // Act
        var result = await _handler.Handle(new GetWorkflowExecutionsQuery(workflowId), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(404);
    }
}
