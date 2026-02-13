using AutoMapper;
using CoreSRE.Application.Workflows.DTOs;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;
using FluentAssertions;
using System.Text.Json;
using Xunit;

namespace CoreSRE.Application.Tests.Workflows;

/// <summary>
/// Tests for US2: Execution Graph Snapshot in API — WorkflowExecution maps GraphSnapshot to WorkflowExecutionDto.
/// </summary>
public class WorkflowExecutionDtoMappingTests
{
    private readonly IMapper _mapper;

    public WorkflowExecutionDtoMappingTests()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<WorkflowMappingProfile>();
        });
        config.AssertConfigurationIsValid();
        _mapper = config.CreateMapper();
    }

    // ========== T016: MapWorkflowExecution_IncludesGraphSnapshot ==========

    [Fact]
    public void MapWorkflowExecution_IncludesGraphSnapshot()
    {
        // Arrange — 3-node graph
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "a", NodeType = WorkflowNodeType.Agent, DisplayName = "A", ReferenceId = Guid.NewGuid() },
                new WorkflowNodeVO { NodeId = "b", NodeType = WorkflowNodeType.Agent, DisplayName = "B", ReferenceId = Guid.NewGuid() },
                new WorkflowNodeVO { NodeId = "c", NodeType = WorkflowNodeType.Agent, DisplayName = "C", ReferenceId = Guid.NewGuid() }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "a", TargetNodeId = "b", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "b", TargetNodeId = "c", EdgeType = WorkflowEdgeType.Normal }
            ]
        };

        var input = JsonDocument.Parse("{}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        // Act
        var dto = _mapper.Map<WorkflowExecutionDto>(execution);

        // Assert
        dto.GraphSnapshot.Should().NotBeNull();
        dto.GraphSnapshot!.Nodes.Should().HaveCount(3);
        dto.GraphSnapshot.Edges.Should().HaveCount(2);
    }

    // ========== T017: MapWorkflowExecution_NullGraphSnapshot_MapsToNull ==========

    [Fact]
    public void MapWorkflowExecution_EmptyGraphSnapshot_MapsGracefully()
    {
        // Arrange — empty graph (default WorkflowGraphVO)
        var graph = new WorkflowGraphVO();
        var input = JsonDocument.Parse("{}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        // Act
        var dto = _mapper.Map<WorkflowExecutionDto>(execution);

        // Assert — should not throw, graph snapshot present but empty
        dto.GraphSnapshot.Should().NotBeNull();
        dto.GraphSnapshot!.Nodes.Should().BeEmpty();
        dto.GraphSnapshot.Edges.Should().BeEmpty();
    }

    // ========== T018: MapWorkflowExecution_GraphSnapshotPreservesNodeDetails ==========

    [Fact]
    public void MapWorkflowExecution_GraphSnapshotPreservesNodeDetails()
    {
        // Arrange
        var refId = Guid.NewGuid();
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO
                {
                    NodeId = "my-agent",
                    NodeType = WorkflowNodeType.Agent,
                    DisplayName = "My Agent",
                    ReferenceId = refId,
                    Config = "{\"key\":\"value\"}"
                }
            ],
            Edges = []
        };

        var input = JsonDocument.Parse("{}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        // Act
        var dto = _mapper.Map<WorkflowExecutionDto>(execution);

        // Assert — mapped nodes preserve all details
        var node = dto.GraphSnapshot!.Nodes.Single();
        node.NodeId.Should().Be("my-agent");
        node.NodeType.Should().Be("Agent");
        node.DisplayName.Should().Be("My Agent");
        node.ReferenceId.Should().Be(refId);
        node.Config.Should().Be("{\"key\":\"value\"}");
    }
}
