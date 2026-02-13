using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace CoreSRE.Infrastructure.Tests.Workflows;

/// <summary>
/// 端口索引校验测试 — 验证边的端口索引不超出节点声明的端口数，
/// 以及 Condition 节点必须有 OutputCount >= 2。
/// </summary>
public class PortValidationTests
{
    [Fact]
    public void Validate_SourcePortIndex_ExceedsOutputCount_Fails()
    {
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "a", NodeType = WorkflowNodeType.Agent, DisplayName = "A", OutputCount = 1 },
                new WorkflowNodeVO { NodeId = "b", NodeType = WorkflowNodeType.Agent, DisplayName = "B" }
            ],
            Edges =
            [
                new WorkflowEdgeVO
                {
                    EdgeId = "e1", SourceNodeId = "a", TargetNodeId = "b",
                    EdgeType = WorkflowEdgeType.Normal, SourcePortIndex = 2
                }
            ]
        };

        var result = graph.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("源端口索引") && e.Contains("a"));
    }

    [Fact]
    public void Validate_TargetPortIndex_ExceedsInputCount_Fails()
    {
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "a", NodeType = WorkflowNodeType.Agent, DisplayName = "A" },
                new WorkflowNodeVO { NodeId = "b", NodeType = WorkflowNodeType.Agent, DisplayName = "B", InputCount = 1 }
            ],
            Edges =
            [
                new WorkflowEdgeVO
                {
                    EdgeId = "e1", SourceNodeId = "a", TargetNodeId = "b",
                    EdgeType = WorkflowEdgeType.Normal, TargetPortIndex = 3
                }
            ]
        };

        var result = graph.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("目标端口索引") && e.Contains("b"));
    }

    [Fact]
    public void Validate_ConditionNode_OutputCountLessThan2_WithMultiPortEdges_Fails()
    {
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "cond", NodeType = WorkflowNodeType.Condition, DisplayName = "Cond", OutputCount = 1 },
                new WorkflowNodeVO { NodeId = "true-branch", NodeType = WorkflowNodeType.Agent, DisplayName = "True" },
                new WorkflowNodeVO { NodeId = "false-branch", NodeType = WorkflowNodeType.Agent, DisplayName = "False" }
            ],
            Edges =
            [
                new WorkflowEdgeVO
                {
                    EdgeId = "e1", SourceNodeId = "cond", TargetNodeId = "true-branch",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "true",
                    SourcePortIndex = 0
                },
                new WorkflowEdgeVO
                {
                    EdgeId = "e2", SourceNodeId = "cond", TargetNodeId = "false-branch",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "false",
                    SourcePortIndex = 1  // uses non-zero port → triggers OutputCount check
                }
            ]
        };

        var result = graph.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("条件节点") && e.Contains("cond") && e.Contains(">= 2"));
    }

    [Fact]
    public void Validate_ConditionNode_OutputCount1_LegacyEdges_Succeeds()
    {
        // Legacy condition nodes with OutputCount=1 and all edges on port 0 should pass
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "cond", NodeType = WorkflowNodeType.Condition, DisplayName = "Cond" },
                new WorkflowNodeVO { NodeId = "branch", NodeType = WorkflowNodeType.Agent, DisplayName = "Branch" }
            ],
            Edges =
            [
                new WorkflowEdgeVO
                {
                    EdgeId = "e1", SourceNodeId = "cond", TargetNodeId = "branch",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "true"
                }
            ]
        };

        var result = graph.Validate();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ValidPortIndices_Succeeds()
    {
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "a", NodeType = WorkflowNodeType.Agent, DisplayName = "A", OutputCount = 2 },
                new WorkflowNodeVO { NodeId = "b", NodeType = WorkflowNodeType.Agent, DisplayName = "B", InputCount = 2 }
            ],
            Edges =
            [
                new WorkflowEdgeVO
                {
                    EdgeId = "e1", SourceNodeId = "a", TargetNodeId = "b",
                    EdgeType = WorkflowEdgeType.Normal, SourcePortIndex = 0, TargetPortIndex = 0
                },
                new WorkflowEdgeVO
                {
                    EdgeId = "e2", SourceNodeId = "a", TargetNodeId = "b",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "true",
                    SourcePortIndex = 1, TargetPortIndex = 1
                }
            ]
        };

        var result = graph.Validate();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_DefaultPortIndices_WithDefaultCounts_Succeeds()
    {
        // Legacy graphs: no port fields → all default to 0/1
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "a", NodeType = WorkflowNodeType.Agent, DisplayName = "A" },
                new WorkflowNodeVO { NodeId = "b", NodeType = WorkflowNodeType.Agent, DisplayName = "B" }
            ],
            Edges =
            [
                new WorkflowEdgeVO
                {
                    EdgeId = "e1", SourceNodeId = "a", TargetNodeId = "b",
                    EdgeType = WorkflowEdgeType.Normal
                }
            ]
        };

        var result = graph.Validate();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ConditionNode_OutputCount2_Succeeds()
    {
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "cond", NodeType = WorkflowNodeType.Condition, DisplayName = "Cond", OutputCount = 2 },
                new WorkflowNodeVO { NodeId = "true-branch", NodeType = WorkflowNodeType.Agent, DisplayName = "True" },
                new WorkflowNodeVO { NodeId = "false-branch", NodeType = WorkflowNodeType.Agent, DisplayName = "False" }
            ],
            Edges =
            [
                new WorkflowEdgeVO
                {
                    EdgeId = "e1", SourceNodeId = "cond", TargetNodeId = "true-branch",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "true",
                    SourcePortIndex = 0
                },
                new WorkflowEdgeVO
                {
                    EdgeId = "e2", SourceNodeId = "cond", TargetNodeId = "false-branch",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "false",
                    SourcePortIndex = 1
                }
            ]
        };

        var result = graph.Validate();

        result.IsValid.Should().BeTrue();
    }
}
