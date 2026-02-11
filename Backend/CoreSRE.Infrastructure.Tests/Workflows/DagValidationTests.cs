using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace CoreSRE.Infrastructure.Tests.Workflows;

/// <summary>
/// DAG 有效性校验单元测试 — 环检测、孤立节点、自环、重复 ID、无效边引用等
/// </summary>
public class DagValidationTests
{
    // ========== Valid Graphs ==========

    [Fact]
    public void Validate_ValidSequentialGraph_Succeeds()
    {
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "a", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "A" },
                new WorkflowNodeVO { NodeId = "b", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "B" },
                new WorkflowNodeVO { NodeId = "c", NodeType = WorkflowNodeType.Tool, ReferenceId = Guid.NewGuid(), DisplayName = "C" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "a", TargetNodeId = "b", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "b", TargetNodeId = "c", EdgeType = WorkflowEdgeType.Normal }
            ]
        };

        var result = graph.Validate();

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_SingleNode_NoEdges_Succeeds()
    {
        var graph = new WorkflowGraphVO
        {
            Nodes = [new WorkflowNodeVO { NodeId = "only", NodeType = WorkflowNodeType.Condition, DisplayName = "Only" }],
            Edges = []
        };

        var result = graph.Validate();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_DiamondGraph_Succeeds()
    {
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "start", NodeType = WorkflowNodeType.FanOut, DisplayName = "Start" },
                new WorkflowNodeVO { NodeId = "left", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "Left" },
                new WorkflowNodeVO { NodeId = "right", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "Right" },
                new WorkflowNodeVO { NodeId = "end", NodeType = WorkflowNodeType.FanIn, DisplayName = "End" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "start", TargetNodeId = "left", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "start", TargetNodeId = "right", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e3", SourceNodeId = "left", TargetNodeId = "end", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e4", SourceNodeId = "right", TargetNodeId = "end", EdgeType = WorkflowEdgeType.Normal }
            ]
        };

        var result = graph.Validate();

        result.IsValid.Should().BeTrue();
    }

    // ========== Empty Graph ==========

    [Fact]
    public void Validate_EmptyGraph_Fails()
    {
        var graph = new WorkflowGraphVO { Nodes = [], Edges = [] };

        var result = graph.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("至少需要一个节点"));
    }

    // ========== Cycle Detection ==========

    [Fact]
    public void Validate_SimpleCycle_Fails()
    {
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "a", NodeType = WorkflowNodeType.Condition, DisplayName = "A" },
                new WorkflowNodeVO { NodeId = "b", NodeType = WorkflowNodeType.Condition, DisplayName = "B" },
                new WorkflowNodeVO { NodeId = "c", NodeType = WorkflowNodeType.Condition, DisplayName = "C" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "a", TargetNodeId = "b", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "b", TargetNodeId = "c", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e3", SourceNodeId = "c", TargetNodeId = "a", EdgeType = WorkflowEdgeType.Normal }
            ]
        };

        var result = graph.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("环路"));
    }

    [Fact]
    public void Validate_TwoNodeCycle_Fails()
    {
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "x", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "X" },
                new WorkflowNodeVO { NodeId = "y", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "Y" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "x", TargetNodeId = "y", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "y", TargetNodeId = "x", EdgeType = WorkflowEdgeType.Normal }
            ]
        };

        var result = graph.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("环路"));
    }

    // ========== Orphan Nodes ==========

    [Fact]
    public void Validate_OrphanNode_Fails()
    {
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "a", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "A" },
                new WorkflowNodeVO { NodeId = "b", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "B" },
                new WorkflowNodeVO { NodeId = "orphan", NodeType = WorkflowNodeType.Condition, DisplayName = "Orphan" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "a", TargetNodeId = "b", EdgeType = WorkflowEdgeType.Normal }
            ]
        };

        var result = graph.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("孤立节点") && e.Contains("orphan"));
    }

    // ========== Self-Loop ==========

    [Fact]
    public void Validate_SelfLoop_Fails()
    {
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "a", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "A" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "a", TargetNodeId = "a", EdgeType = WorkflowEdgeType.Normal }
            ]
        };

        var result = graph.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("自身节点"));
    }

    // ========== Duplicate Node ID ==========

    [Fact]
    public void Validate_DuplicateNodeId_Fails()
    {
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "dup", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "First" },
                new WorkflowNodeVO { NodeId = "dup", NodeType = WorkflowNodeType.Tool, ReferenceId = Guid.NewGuid(), DisplayName = "Second" }
            ],
            Edges = []
        };

        var result = graph.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("节点 ID 重复") && e.Contains("dup"));
    }

    // ========== Invalid Edge References ==========

    [Fact]
    public void Validate_EdgeReferencesNonExistentSourceNode_Fails()
    {
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "a", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "A" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "nonexistent", TargetNodeId = "a", EdgeType = WorkflowEdgeType.Normal }
            ]
        };

        var result = graph.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("不存在的源节点") && e.Contains("nonexistent"));
    }

    [Fact]
    public void Validate_EdgeReferencesNonExistentTargetNode_Fails()
    {
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "a", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "A" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "a", TargetNodeId = "nonexistent", EdgeType = WorkflowEdgeType.Normal }
            ]
        };

        var result = graph.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("不存在的目标节点") && e.Contains("nonexistent"));
    }

    // ========== Duplicate Edges ==========

    [Fact]
    public void Validate_DuplicateNormalEdge_Fails()
    {
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "a", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "A" },
                new WorkflowNodeVO { NodeId = "b", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "B" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "a", TargetNodeId = "b", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "a", TargetNodeId = "b", EdgeType = WorkflowEdgeType.Normal }
            ]
        };

        var result = graph.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("重复的无条件边"));
    }

    [Fact]
    public void Validate_MultipleConditionalEdges_SamePair_Succeeds()
    {
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "a", NodeType = WorkflowNodeType.Condition, DisplayName = "A" },
                new WorkflowNodeVO { NodeId = "b", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "B" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "a", TargetNodeId = "b", EdgeType = WorkflowEdgeType.Conditional, Condition = "$.x == 1" },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "a", TargetNodeId = "b", EdgeType = WorkflowEdgeType.Conditional, Condition = "$.x == 2" }
            ]
        };

        var result = graph.Validate();

        result.IsValid.Should().BeTrue();
    }

    // ========== Max Nodes Warning ==========

    [Fact]
    public void Validate_OverMaxNodes_WarningButValid()
    {
        var nodes = Enumerable.Range(1, 101)
            .Select(i => new WorkflowNodeVO { NodeId = $"n{i}", NodeType = WorkflowNodeType.Condition, DisplayName = $"N{i}" })
            .ToList();

        var edges = Enumerable.Range(1, 100)
            .Select(i => new WorkflowEdgeVO { EdgeId = $"e{i}", SourceNodeId = $"n{i}", TargetNodeId = $"n{i + 1}", EdgeType = WorkflowEdgeType.Normal })
            .ToList();

        var graph = new WorkflowGraphVO { Nodes = nodes, Edges = edges };

        var result = graph.Validate();

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Contains("101") && w.Contains("100"));
    }

    // ========== Duplicate Edge ID ==========

    [Fact]
    public void Validate_DuplicateEdgeId_Fails()
    {
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "a", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "A" },
                new WorkflowNodeVO { NodeId = "b", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "B" },
                new WorkflowNodeVO { NodeId = "c", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "C" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "a", TargetNodeId = "b", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "b", TargetNodeId = "c", EdgeType = WorkflowEdgeType.Normal }
            ]
        };

        var result = graph.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("边 ID 重复") && e.Contains("e1"));
    }
}
