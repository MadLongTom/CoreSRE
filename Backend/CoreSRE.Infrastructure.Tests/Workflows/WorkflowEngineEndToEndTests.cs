using System.Text.Json;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using CoreSRE.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CoreSRE.Infrastructure.Tests.Workflows;

/// <summary>
/// US4: End-to-End Smoke Tests — validates full lifecycle:
/// create graph → create execution → run engine → verify status, node inputs, and graph snapshot.
/// </summary>
public class WorkflowEngineEndToEndTests
{
    private readonly Mock<IAgentResolver> _agentResolverMock = new();
    private readonly Mock<IToolInvokerFactory> _toolInvokerFactoryMock = new();
    private readonly Mock<IToolRegistrationRepository> _toolRepoMock = new();
    private readonly Mock<IWorkflowExecutionRepository> _executionRepoMock = new();
    private readonly Mock<IConditionEvaluator> _conditionEvaluatorMock = new();
    private readonly Mock<IExpressionEvaluator> _expressionEvaluatorMock = new();
    private readonly Mock<ILogger<WorkflowEngine>> _loggerMock = new();
    private readonly WorkflowEngine _engine;

    public WorkflowEngineEndToEndTests()
    {
        _expressionEvaluatorMock
            .Setup(e => e.Evaluate(It.IsAny<string>(), It.IsAny<ExpressionContext>()))
            .Returns((string template, ExpressionContext _) => template);
        _expressionEvaluatorMock
            .Setup(e => e.EvaluateCondition(It.IsAny<string>(), It.IsAny<ExpressionContext>()))
            .Throws(new ExpressionEvaluationException("fallback", new Exception()));

        _engine = new WorkflowEngine(
            _agentResolverMock.Object,
            _toolInvokerFactoryMock.Object,
            _toolRepoMock.Object,
            _executionRepoMock.Object,
            _conditionEvaluatorMock.Object,
            _expressionEvaluatorMock.Object,
            _loggerMock.Object);
    }

    // ========== T029: EndToEnd_CreatePublishExecuteQuery_AllSucceed ==========

    [Fact]
    public async Task EndToEnd_CreatePublishExecuteQuery_AllSucceed()
    {
        // Arrange — build 3-node sequential agent workflow with MockChatClient
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "agent-1", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "Agent 1" },
                new WorkflowNodeVO { NodeId = "agent-2", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "Agent 2" },
                new WorkflowNodeVO { NodeId = "agent-3", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "Agent 3" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "agent-1", TargetNodeId = "agent-2", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "agent-2", TargetNodeId = "agent-3", EdgeType = WorkflowEdgeType.Normal }
            ]
        };

        var input = JsonDocument.Parse("{\"query\":\"hello world\"}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        // Setup mock agents that return structured responses
        foreach (var node in graph.Nodes)
        {
            var mockClient = new MockChatClient(node.DisplayName);
            var agent = mockClient.AsAIAgent(new ChatClientAgentOptions { Name = node.DisplayName });
            var resolved = new ResolvedAgent(agent, null);
            _agentResolverMock
                .Setup(r => r.ResolveAsync(node.ReferenceId!.Value, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resolved);
        }

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert — status Completed
        execution.Status.Should().Be(ExecutionStatus.Completed);
        execution.CompletedAt.Should().NotBeNull();
        execution.ErrorMessage.Should().BeNull();

        // Assert — all 3 NodeExecutionVO.Input fields populated
        execution.NodeExecutions.Should().HaveCount(3);
        foreach (var ne in execution.NodeExecutions)
        {
            ne.Status.Should().Be(NodeExecutionStatus.Completed, $"node {ne.NodeId} should be completed");
            ne.Input.Should().NotBeNull($"node {ne.NodeId} should have recorded input");
            ne.Output.Should().NotBeNull($"node {ne.NodeId} should have output");
        }

        // Assert — graph snapshot present on entity
        execution.GraphSnapshot.Should().NotBeNull();
        execution.GraphSnapshot.Nodes.Should().HaveCount(3);
        execution.GraphSnapshot.Edges.Should().HaveCount(2);

        // Assert — first node input contains workflow input
        var node1 = execution.NodeExecutions.First(n => n.NodeId == "agent-1");
        node1.Input.Should().Contain("hello world");
    }

    // ========== T030: EndToEnd_MockAgentChain_DataFlowsCorrectly ==========

    [Fact]
    public async Task EndToEnd_MockAgentChain_DataFlowsCorrectly()
    {
        // Arrange — 3-node sequential with mock agents
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "agent-1", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "Agent 1" },
                new WorkflowNodeVO { NodeId = "agent-2", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "Agent 2" },
                new WorkflowNodeVO { NodeId = "agent-3", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "Agent 3" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "agent-1", TargetNodeId = "agent-2", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "agent-2", TargetNodeId = "agent-3", EdgeType = WorkflowEdgeType.Normal }
            ]
        };

        var input = JsonDocument.Parse("{\"task\":\"analyze\"}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        // Setup mock agents
        foreach (var node in graph.Nodes)
        {
            var mockClient = new MockChatClient(node.DisplayName);
            var agent = mockClient.AsAIAgent(new ChatClientAgentOptions { Name = node.DisplayName });
            var resolved = new ResolvedAgent(agent, null);
            _agentResolverMock
                .Setup(r => r.ResolveAsync(node.ReferenceId!.Value, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resolved);
        }

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert — verify data flows through chain
        execution.Status.Should().Be(ExecutionStatus.Completed);

        var node1 = execution.NodeExecutions.First(n => n.NodeId == "agent-1");
        var node2 = execution.NodeExecutions.First(n => n.NodeId == "agent-2");
        var node3 = execution.NodeExecutions.First(n => n.NodeId == "agent-3");

        // Node 1 receives the workflow input
        node1.Input.Should().Contain("analyze");

        // Node 2 receives node 1's output (mock response from Agent 1)
        node2.Input.Should().NotBeNull();
        node2.Input.Should().Contain("Agent 1", "node 2 input should contain node 1's mock output with agent name");

        // Node 3 receives node 2's output (mock response from Agent 2)
        node3.Input.Should().NotBeNull();
        node3.Input.Should().Contain("Agent 2", "node 3 input should contain node 2's mock output with agent name");
    }
}
