using AutoMapper;
using CoreSRE.Application.Workflows.DTOs;
using CoreSRE.Application.Workflows.Queries.GetWorkflowById;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace CoreSRE.Application.Tests.Workflows.Queries.GetWorkflowById;

public class GetWorkflowByIdQueryHandlerTests
{
    private readonly Mock<IWorkflowDefinitionRepository> _repoMock = new();
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly GetWorkflowByIdQueryHandler _handler;

    public GetWorkflowByIdQueryHandlerTests()
    {
        _handler = new GetWorkflowByIdQueryHandler(_repoMock.Object, _mapperMock.Object);
    }

    [Fact]
    public async Task Handle_Found_ReturnsFullDetail()
    {
        // Arrange
        var id = Guid.NewGuid();
        var workflow = WorkflowDefinition.Create("Test WF", "Desc",
            new WorkflowGraphVO
            {
                Nodes = [new WorkflowNodeVO { NodeId = "n1", NodeType = WorkflowNodeType.Agent, DisplayName = "Agent" }]
            });

        _repoMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var dto = new WorkflowDefinitionDto
        {
            Id = id,
            Name = "Test WF",
            Description = "Desc",
            Status = "Draft",
            Graph = new WorkflowGraphDto
            {
                Nodes = [new WorkflowNodeDto { NodeId = "n1", NodeType = "Agent", DisplayName = "Agent" }]
            }
        };
        _mapperMock
            .Setup(m => m.Map<WorkflowDefinitionDto>(workflow))
            .Returns(dto);

        // Act
        var result = await _handler.Handle(new GetWorkflowByIdQuery(id), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Name.Should().Be("Test WF");
        result.Data.Graph.Nodes.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_NotFound_Returns404()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repoMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinition?)null);

        // Act
        var result = await _handler.Handle(new GetWorkflowByIdQuery(id), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(404);
    }
}
