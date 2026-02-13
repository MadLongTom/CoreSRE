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
/// Tests for US1: Node Input Traceability — every node execution records its input data
/// in NodeExecutionVO.Input before processing begins.
/// </summary>
public class NodeInputRecordingTests
{
    private readonly Mock<IAgentResolver> _agentResolverMock = new();
    private readonly Mock<IToolInvokerFactory> _toolInvokerFactoryMock = new();
    private readonly Mock<IToolRegistrationRepository> _toolRepoMock = new();
    private readonly Mock<IWorkflowExecutionRepository> _executionRepoMock = new();
    private readonly Mock<IConditionEvaluator> _conditionEvaluatorMock = new();
    private readonly Mock<IExpressionEvaluator> _expressionEvaluatorMock = new();
    private readonly Mock<ILogger<WorkflowEngine>> _loggerMock = new();
    private readonly WorkflowEngine _engine;

    public NodeInputRecordingTests()
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

    private static AIAgent CreateMockAgent(Mock<IChatClient> mockChatClient)
    {
        return mockChatClient.Object.AsAIAgent(new ChatClientAgentOptions { Name = "test-agent" });
    }

    private static ResolvedAgent CreateResolvedAgent(Mock<IChatClient> mockChatClient)
    {
        return new ResolvedAgent(CreateMockAgent(mockChatClient), null);
    }

    private void SetupAgentResponse(Guid referenceId, string responseText)
    {
        var mockChatClient = new Mock<IChatClient>();
        mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, responseText)));

        var agent = CreateResolvedAgent(mockChatClient);

        _agentResolverMock
            .Setup(r => r.ResolveAsync(referenceId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
    }

    // ========== T005: SequentialExecution_RecordsInputForAllNodes ==========

    [Fact]
    public async Task SequentialExecution_RecordsInputForAllNodes()
    {
        // Arrange — 3-node sequential workflow
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

        var input = JsonDocument.Parse("{\"query\":\"hello\"}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        foreach (var node in graph.Nodes)
        {
            SetupAgentResponse(node.ReferenceId!.Value, $"{{\"result\":\"{node.NodeId}\"}}");
        }

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert — every node should have non-null Input
        execution.Status.Should().Be(ExecutionStatus.Completed);
        foreach (var ne in execution.NodeExecutions)
        {
            ne.Input.Should().NotBeNull($"node {ne.NodeId} should have recorded input");
        }

        // Verify data chain: node-1 gets workflow input, node-2 gets node-1 output, etc.
        var node1 = execution.NodeExecutions.First(n => n.NodeId == "agent-1");
        node1.Input.Should().Contain("hello");

        var node2 = execution.NodeExecutions.First(n => n.NodeId == "agent-2");
        node2.Input.Should().Contain("agent-1");

        var node3 = execution.NodeExecutions.First(n => n.NodeId == "agent-3");
        node3.Input.Should().Contain("agent-2");
    }

    // ========== T006: FirstNode_RecordsWorkflowLevelInput ==========

    [Fact]
    public async Task FirstNode_RecordsWorkflowLevelInput()
    {
        // Arrange — single node workflow
        var refId = Guid.NewGuid();
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "single", NodeType = WorkflowNodeType.Agent, ReferenceId = refId, DisplayName = "Single Agent" }
            ],
            Edges = []
        };

        var input = JsonDocument.Parse("{\"task\":\"summarize\"}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);
        SetupAgentResponse(refId, "{\"summary\":\"done\"}");

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert — first node input should contain the workflow-level input
        execution.Status.Should().Be(ExecutionStatus.Completed);
        var nodeExec = execution.NodeExecutions.Single();
        nodeExec.Input.Should().NotBeNull();
        nodeExec.Input.Should().Contain("summarize");
    }

    // ========== T007: FailedNode_StillRecordsInput ==========

    [Fact]
    public async Task FailedNode_StillRecordsInput()
    {
        // Arrange — single node that throws
        var refId = Guid.NewGuid();
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "failing", NodeType = WorkflowNodeType.Agent, ReferenceId = refId, DisplayName = "Failing Agent" }
            ],
            Edges = []
        };

        var input = JsonDocument.Parse("{\"data\":\"test-input\"}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        var mockChatClient = new Mock<IChatClient>();
        mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Agent crashed"));

        var agent = CreateResolvedAgent(mockChatClient);
        _agentResolverMock
            .Setup(r => r.ResolveAsync(refId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert — node should be Failed but Input should still be populated
        execution.Status.Should().Be(ExecutionStatus.Failed);
        var nodeExec = execution.NodeExecutions.Single();
        nodeExec.Status.Should().Be(NodeExecutionStatus.Failed);
        nodeExec.Input.Should().NotBeNull();
        nodeExec.Input.Should().Contain("test-input");
    }

    // ========== T008: ConditionNode_RecordsInputBeforeEvaluation ==========

    [Fact]
    public async Task ConditionNode_RecordsInputBeforeEvaluation()
    {
        // Arrange — workflow with condition node
        var agentRefId = Guid.NewGuid();
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "condition", NodeType = WorkflowNodeType.Condition, DisplayName = "Condition" },
                new WorkflowNodeVO { NodeId = "agent-branch", NodeType = WorkflowNodeType.Agent, ReferenceId = agentRefId, DisplayName = "Agent Branch" }
            ],
            Edges =
            [
                new WorkflowEdgeVO
                {
                    EdgeId = "e1", SourceNodeId = "condition", TargetNodeId = "agent-branch",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.level == \"high\""
                }
            ]
        };

        var input = JsonDocument.Parse("{\"level\":\"high\"}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        // Setup condition evaluation
        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate("$.level == \"high\"", It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = true; return true; });

        SetupAgentResponse(agentRefId, "{\"handled\":\"high\"}");

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert — condition node should have recorded its input (lastOutput)
        execution.Status.Should().Be(ExecutionStatus.Completed);
        var condNode = execution.NodeExecutions.First(n => n.NodeId == "condition");
        condNode.Input.Should().NotBeNull();
        condNode.Input.Should().Contain("high");
    }
}
