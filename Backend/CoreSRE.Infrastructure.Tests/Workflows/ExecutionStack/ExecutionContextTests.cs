using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;
using CoreSRE.Infrastructure.Services;
using FluentAssertions;
using Xunit;
using ExecutionContext = CoreSRE.Infrastructure.Services.ExecutionContext;

namespace CoreSRE.Infrastructure.Tests.Workflows.ExecutionStack;

/// <summary>
/// T020: ExecutionContext 单元测试 — 验证执行栈、等待队列、运行结果管理。
/// </summary>
public class ExecutionContextTests
{
    private static WorkflowGraphVO CreateSimpleGraph()
    {
        return new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "A", NodeType = WorkflowNodeType.Agent, DisplayName = "A" },
                new WorkflowNodeVO { NodeId = "B", NodeType = WorkflowNodeType.Agent, DisplayName = "B" },
                new WorkflowNodeVO { NodeId = "C", NodeType = WorkflowNodeType.Agent, DisplayName = "C" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "A", TargetNodeId = "B", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "B", TargetNodeId = "C", EdgeType = WorkflowEdgeType.Normal }
            ]
        };
    }

    [Fact]
    public void Push_And_Pop_LIFO_Order()
    {
        // Arrange
        var ctx = new ExecutionContext(CreateSimpleGraph());
        var nodeA = ctx.NodeMap["A"];
        var nodeB = ctx.NodeMap["B"];
        var taskA = new NodeExecutionTask { Node = nodeA, InputData = NodeInputData.Empty };
        var taskB = new NodeExecutionTask { Node = nodeB, InputData = NodeInputData.Empty };

        // Act
        ctx.Push(taskA);
        ctx.Push(taskB);

        // Assert — LIFO: B should be popped first
        var popped1 = ctx.Pop();
        popped1.Node.NodeId.Should().Be("B");

        var popped2 = ctx.Pop();
        popped2.Node.NodeId.Should().Be("A");
    }

    [Fact]
    public void IsStackEmpty_ReturnsCorrectly()
    {
        var ctx = new ExecutionContext(CreateSimpleGraph());

        ctx.IsStackEmpty.Should().BeTrue();

        ctx.Push(new NodeExecutionTask { Node = ctx.NodeMap["A"], InputData = NodeInputData.Empty });
        ctx.IsStackEmpty.Should().BeFalse();

        ctx.Pop();
        ctx.IsStackEmpty.Should().BeTrue();
    }

    [Fact]
    public void AddToWaiting_And_TryPromote_AllPortsSatisfied()
    {
        // Arrange — node C needs 2 input ports
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "A", NodeType = WorkflowNodeType.Agent, DisplayName = "A" },
                new WorkflowNodeVO { NodeId = "B", NodeType = WorkflowNodeType.Agent, DisplayName = "B" },
                new WorkflowNodeVO { NodeId = "C", NodeType = WorkflowNodeType.Agent, DisplayName = "C", InputCount = 2 }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "A", TargetNodeId = "C", EdgeType = WorkflowEdgeType.Normal, TargetPortIndex = 0 },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "B", TargetNodeId = "C", EdgeType = WorkflowEdgeType.Normal, TargetPortIndex = 1 }
            ]
        };
        var ctx = new ExecutionContext(graph);
        var portData0 = new PortDataVO([new WorkflowItemVO(System.Text.Json.JsonDocument.Parse("{}").RootElement)]);
        var portData1 = new PortDataVO([new WorkflowItemVO(System.Text.Json.JsonDocument.Parse("{\"x\":1}").RootElement)]);

        // Act — receive port 0 first
        ctx.AddToWaiting("C", 0, portData0, 2);
        var promoted1 = ctx.TryPromote("C", out var input1);

        // Assert — not yet complete
        promoted1.Should().BeFalse();
        input1.Should().BeNull();

        // Act — receive port 1
        ctx.AddToWaiting("C", 1, portData1, 2);
        var promoted2 = ctx.TryPromote("C", out var input2);

        // Assert — now complete
        promoted2.Should().BeTrue();
        input2.Should().NotBeNull();
        input2!.GetPort(0).Should().NotBeNull();
        input2.GetPort(1).Should().NotBeNull();
    }

    [Fact]
    public void RecordResult_And_GetResults()
    {
        var ctx = new ExecutionContext(CreateSimpleGraph());
        var result = new NodeRunResult
        {
            OutputData = NodeOutputData.Empty,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            IsSuccess = true
        };

        ctx.RecordResult("A", result);

        var results = ctx.GetResults("A");
        results.Should().HaveCount(1);
        results[0].IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void GetResults_UnknownNode_ReturnsEmpty()
    {
        var ctx = new ExecutionContext(CreateSimpleGraph());
        ctx.GetResults("nonexistent").Should().BeEmpty();
    }

    [Fact]
    public void IncrementAndCheckLimit_RespectsMaxExecutions()
    {
        var ctx = new ExecutionContext(CreateSimpleGraph()) { MaxExecutionsPerNode = 3 };

        ctx.IncrementAndCheckLimit("A").Should().BeTrue();  // 1
        ctx.IncrementAndCheckLimit("A").Should().BeTrue();  // 2
        ctx.IncrementAndCheckLimit("A").Should().BeTrue();  // 3
        ctx.IncrementAndCheckLimit("A").Should().BeFalse(); // 4 — exceeds limit

        ctx.GetExecutionCount("A").Should().Be(4);
    }

    [Fact]
    public void EdgeMaps_PrecomputedCorrectly()
    {
        var ctx = new ExecutionContext(CreateSimpleGraph());

        // Outgoing edges
        ctx.OutgoingEdges["A"].Should().HaveCount(1);
        ctx.OutgoingEdges["A"][0].TargetNodeId.Should().Be("B");
        ctx.OutgoingEdges["B"].Should().HaveCount(1);
        ctx.OutgoingEdges["C"].Should().BeEmpty();

        // Incoming edges
        ctx.IncomingEdges["A"].Should().BeEmpty();
        ctx.IncomingEdges["B"].Should().HaveCount(1);
        ctx.IncomingEdges["C"].Should().HaveCount(1);
    }
}
