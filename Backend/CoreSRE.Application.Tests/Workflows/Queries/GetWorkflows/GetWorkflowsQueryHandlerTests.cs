using AutoMapper;
using CoreSRE.Application.Workflows.DTOs;
using CoreSRE.Application.Workflows.Queries.GetWorkflows;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace CoreSRE.Application.Tests.Workflows.Queries.GetWorkflows;

public class GetWorkflowsQueryHandlerTests
{
    private readonly Mock<IWorkflowDefinitionRepository> _repoMock = new();
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly GetWorkflowsQueryHandler _handler;

    public GetWorkflowsQueryHandlerTests()
    {
        _handler = new GetWorkflowsQueryHandler(_repoMock.Object, _mapperMock.Object);
    }

    private static WorkflowDefinition CreateDummyWorkflow(string name = "Test") =>
        WorkflowDefinition.Create(name, null, new WorkflowGraphVO
        {
            Nodes = [new WorkflowNodeVO { NodeId = "n1", NodeType = WorkflowNodeType.Condition, DisplayName = "N1" }]
        });

    [Fact]
    public async Task Handle_NoFilter_ReturnsAllWorkflows()
    {
        // Arrange
        var workflows = new List<WorkflowDefinition> { CreateDummyWorkflow("WF1"), CreateDummyWorkflow("WF2") };
        _repoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflows);

        var summaries = new List<WorkflowSummaryDto>
        {
            new() { Id = Guid.NewGuid(), Name = "WF1", Status = "Draft", NodeCount = 1 },
            new() { Id = Guid.NewGuid(), Name = "WF2", Status = "Draft", NodeCount = 1 }
        };
        _mapperMock
            .Setup(m => m.Map<List<WorkflowSummaryDto>>(workflows))
            .Returns(summaries);

        // Act
        var result = await _handler.Handle(new GetWorkflowsQuery(), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(2);
        _repoMock.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_FilterByStatus_ReturnsFilteredWorkflows()
    {
        // Arrange
        var workflows = new List<WorkflowDefinition> { CreateDummyWorkflow("Draft WF") };
        _repoMock
            .Setup(r => r.GetByStatusAsync(WorkflowStatus.Draft, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflows);

        var summaries = new List<WorkflowSummaryDto>
        {
            new() { Id = Guid.NewGuid(), Name = "Draft WF", Status = "Draft", NodeCount = 1 }
        };
        _mapperMock
            .Setup(m => m.Map<List<WorkflowSummaryDto>>(workflows))
            .Returns(summaries);

        // Act
        var result = await _handler.Handle(
            new GetWorkflowsQuery(WorkflowStatus.Draft), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(1);
        result.Data![0].Status.Should().Be("Draft");
        _repoMock.Verify(
            r => r.GetByStatusAsync(WorkflowStatus.Draft, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_EmptyResult_ReturnsEmptyList()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkflowDefinition>());

        _mapperMock
            .Setup(m => m.Map<List<WorkflowSummaryDto>>(It.IsAny<IEnumerable<WorkflowDefinition>>()))
            .Returns(new List<WorkflowSummaryDto>());

        // Act
        var result = await _handler.Handle(new GetWorkflowsQuery(), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }
}
