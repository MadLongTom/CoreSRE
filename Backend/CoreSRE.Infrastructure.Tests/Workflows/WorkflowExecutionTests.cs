using System.Text.Json;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace CoreSRE.Infrastructure.Tests.Workflows;

/// <summary>
/// WorkflowExecution 聚合根单元测试 — 工厂方法、状态转换、节点操作、守卫
/// </summary>
public class WorkflowExecutionTests
{
    private static WorkflowGraphVO CreateThreeNodeGraph() => new()
    {
        Nodes =
        [
            new WorkflowNodeVO { NodeId = "agent-a", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "Agent A" },
            new WorkflowNodeVO { NodeId = "agent-b", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "Agent B" },
            new WorkflowNodeVO { NodeId = "agent-c", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "Agent C" }
        ],
        Edges =
        [
            new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "agent-a", TargetNodeId = "agent-b", EdgeType = WorkflowEdgeType.Normal },
            new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "agent-b", TargetNodeId = "agent-c", EdgeType = WorkflowEdgeType.Normal }
        ]
    };

    private static JsonElement EmptyJsonInput() =>
        JsonSerializer.Deserialize<JsonElement>("{}");

    // ========== Create Factory ==========

    [Fact]
    public void Create_ValidInput_ReturnsPendingExecution()
    {
        var workflowId = Guid.NewGuid();
        var input = EmptyJsonInput();
        var graph = CreateThreeNodeGraph();

        var execution = WorkflowExecution.Create(workflowId, input, graph);

        execution.WorkflowDefinitionId.Should().Be(workflowId);
        execution.Status.Should().Be(ExecutionStatus.Pending);
        execution.Input.ValueKind.Should().Be(JsonValueKind.Object);
        execution.Output.Should().BeNull();
        execution.StartedAt.Should().BeNull();
        execution.CompletedAt.Should().BeNull();
        execution.ErrorMessage.Should().BeNull();
        execution.GraphSnapshot.Should().BeSameAs(graph);
        execution.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_InitializesAllNodeExecutions()
    {
        var graph = CreateThreeNodeGraph();
        var execution = WorkflowExecution.Create(Guid.NewGuid(), EmptyJsonInput(), graph);

        execution.NodeExecutions.Should().HaveCount(3);
        execution.NodeExecutions.Should().AllSatisfy(n =>
        {
            n.Status.Should().Be(NodeExecutionStatus.Pending);
            n.Input.Should().BeNull();
            n.Output.Should().BeNull();
            n.ErrorMessage.Should().BeNull();
            n.StartedAt.Should().BeNull();
            n.CompletedAt.Should().BeNull();
        });

        execution.NodeExecutions.Select(n => n.NodeId)
            .Should().BeEquivalentTo(["agent-a", "agent-b", "agent-c"]);
    }

    [Fact]
    public void Create_NullGraph_Throws()
    {
        var act = () => WorkflowExecution.Create(Guid.NewGuid(), EmptyJsonInput(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ========== Start ==========

    [Fact]
    public void Start_PendingStatus_TransitionsToRunning()
    {
        var execution = WorkflowExecution.Create(Guid.NewGuid(), EmptyJsonInput(), CreateThreeNodeGraph());

        execution.Start();

        execution.Status.Should().Be(ExecutionStatus.Running);
        execution.StartedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Start_AlreadyRunning_Throws()
    {
        var execution = WorkflowExecution.Create(Guid.NewGuid(), EmptyJsonInput(), CreateThreeNodeGraph());
        execution.Start();

        var act = () => execution.Start();

        act.Should().Throw<InvalidOperationException>();
    }

    // ========== StartNode ==========

    [Fact]
    public void StartNode_ValidNode_TransitionsToRunning()
    {
        var execution = WorkflowExecution.Create(Guid.NewGuid(), EmptyJsonInput(), CreateThreeNodeGraph());
        execution.Start();

        execution.StartNode("agent-a", null);

        var node = execution.NodeExecutions.Single(n => n.NodeId == "agent-a");
        node.Status.Should().Be(NodeExecutionStatus.Running);
        node.StartedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void StartNode_UnknownNodeId_Throws()
    {
        var execution = WorkflowExecution.Create(Guid.NewGuid(), EmptyJsonInput(), CreateThreeNodeGraph());
        execution.Start();

        var act = () => execution.StartNode("nonexistent", null);

        act.Should().Throw<InvalidOperationException>();
    }

    // ========== CompleteNode ==========

    [Fact]
    public void CompleteNode_RunningNode_TransitionsToCompleted()
    {
        var execution = WorkflowExecution.Create(Guid.NewGuid(), EmptyJsonInput(), CreateThreeNodeGraph());
        execution.Start();
        execution.StartNode("agent-a", null);

        execution.CompleteNode("agent-a", "{\"result\": \"ok\"}");

        var node = execution.NodeExecutions.Single(n => n.NodeId == "agent-a");
        node.Status.Should().Be(NodeExecutionStatus.Completed);
        node.Output.Should().Be("{\"result\": \"ok\"}");
        node.CompletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void CompleteNode_UnknownNodeId_Throws()
    {
        var execution = WorkflowExecution.Create(Guid.NewGuid(), EmptyJsonInput(), CreateThreeNodeGraph());
        execution.Start();

        var act = () => execution.CompleteNode("nonexistent", "{}");

        act.Should().Throw<InvalidOperationException>();
    }

    // ========== FailNode ==========

    [Fact]
    public void FailNode_RunningNode_TransitionsToFailed()
    {
        var execution = WorkflowExecution.Create(Guid.NewGuid(), EmptyJsonInput(), CreateThreeNodeGraph());
        execution.Start();
        execution.StartNode("agent-b", null);

        execution.FailNode("agent-b", "Agent不可用");

        var node = execution.NodeExecutions.Single(n => n.NodeId == "agent-b");
        node.Status.Should().Be(NodeExecutionStatus.Failed);
        node.ErrorMessage.Should().Be("Agent不可用");
        node.CompletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    // ========== SkipNode ==========

    [Fact]
    public void SkipNode_PendingNode_TransitionsToSkipped()
    {
        var execution = WorkflowExecution.Create(Guid.NewGuid(), EmptyJsonInput(), CreateThreeNodeGraph());
        execution.Start();

        execution.SkipNode("agent-c");

        var node = execution.NodeExecutions.Single(n => n.NodeId == "agent-c");
        node.Status.Should().Be(NodeExecutionStatus.Skipped);
    }

    [Fact]
    public void SkipNode_UnknownNodeId_Throws()
    {
        var execution = WorkflowExecution.Create(Guid.NewGuid(), EmptyJsonInput(), CreateThreeNodeGraph());
        execution.Start();

        var act = () => execution.SkipNode("nonexistent");

        act.Should().Throw<InvalidOperationException>();
    }

    // ========== Complete (Workflow) ==========

    [Fact]
    public void Complete_RunningStatus_TransitionsToCompleted()
    {
        var execution = WorkflowExecution.Create(Guid.NewGuid(), EmptyJsonInput(), CreateThreeNodeGraph());
        execution.Start();
        var output = JsonSerializer.Deserialize<JsonElement>("{\"report\": \"done\"}");

        execution.Complete(output);

        execution.Status.Should().Be(ExecutionStatus.Completed);
        execution.Output.Should().NotBeNull();
        execution.Output!.Value.GetProperty("report").GetString().Should().Be("done");
        execution.CompletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Complete_PendingStatus_Throws()
    {
        var execution = WorkflowExecution.Create(Guid.NewGuid(), EmptyJsonInput(), CreateThreeNodeGraph());

        var act = () => execution.Complete(EmptyJsonInput());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Complete_AlreadyCompleted_Throws()
    {
        var execution = WorkflowExecution.Create(Guid.NewGuid(), EmptyJsonInput(), CreateThreeNodeGraph());
        execution.Start();
        execution.Complete(EmptyJsonInput());

        var act = () => execution.Complete(EmptyJsonInput());

        act.Should().Throw<InvalidOperationException>();
    }

    // ========== Fail (Workflow) ==========

    [Fact]
    public void Fail_RunningStatus_TransitionsToFailed()
    {
        var execution = WorkflowExecution.Create(Guid.NewGuid(), EmptyJsonInput(), CreateThreeNodeGraph());
        execution.Start();

        execution.Fail("节点超时");

        execution.Status.Should().Be(ExecutionStatus.Failed);
        execution.ErrorMessage.Should().Be("节点超时");
        execution.CompletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Fail_PendingStatus_Throws()
    {
        var execution = WorkflowExecution.Create(Guid.NewGuid(), EmptyJsonInput(), CreateThreeNodeGraph());

        var act = () => execution.Fail("error");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Fail_AlreadyFailed_Throws()
    {
        var execution = WorkflowExecution.Create(Guid.NewGuid(), EmptyJsonInput(), CreateThreeNodeGraph());
        execution.Start();
        execution.Fail("first error");

        var act = () => execution.Fail("second error");

        act.Should().Throw<InvalidOperationException>();
    }
}
