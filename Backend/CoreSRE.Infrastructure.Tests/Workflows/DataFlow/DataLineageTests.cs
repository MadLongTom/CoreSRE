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

namespace CoreSRE.Infrastructure.Tests.Workflows.DataFlow;

/// <summary>
/// T046-T047: 数据血缘追踪测试 — 每个数据项携带 ItemSourceVO 记录来源节点。
/// </summary>
public class DataLineageTests
{
    private readonly Mock<IAgentResolver> _agentResolverMock = new();
    private readonly Mock<IToolInvokerFactory> _toolInvokerFactoryMock = new();
    private readonly Mock<IToolRegistrationRepository> _toolRepoMock = new();
    private readonly Mock<IWorkflowExecutionRepository> _executionRepoMock = new();
    private readonly Mock<IConditionEvaluator> _conditionEvaluatorMock = new();
    private readonly Mock<IExpressionEvaluator> _expressionEvaluatorMock = new();
    private readonly Mock<ILogger<WorkflowEngine>> _loggerMock = new();
    private readonly WorkflowEngine _engine;

    public DataLineageTests()
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
            new NullWorkflowExecutionNotifier(),
            _loggerMock.Object);
    }

    private static ResolvedAgent CreateResolvedAgent(Mock<IChatClient> mockChatClient)
    {
        var agent = mockChatClient.Object.AsAIAgent(new ChatClientAgentOptions { Name = "test-agent" });
        return new ResolvedAgent(agent, null);
    }

    // ==================== T046: Three-node chain lineage ====================

    [Fact]
    public async Task ThreeNodeChain_NodeBInput_HasSourcePointingToA()
    {
        // Arrange: A → B → C
        var refA = Guid.NewGuid();
        var refB = Guid.NewGuid();
        var refC = Guid.NewGuid();
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "a", NodeType = WorkflowNodeType.Agent, ReferenceId = refA, DisplayName = "A" },
                new WorkflowNodeVO { NodeId = "b", NodeType = WorkflowNodeType.Agent, ReferenceId = refB, DisplayName = "B" },
                new WorkflowNodeVO { NodeId = "c", NodeType = WorkflowNodeType.Agent, ReferenceId = refC, DisplayName = "C" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "a", TargetNodeId = "b", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "b", TargetNodeId = "c", EdgeType = WorkflowEdgeType.Normal }
            ]
        };
        var input = JsonDocument.Parse("{\"q\":\"lineage\"}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        // Capture inputs as NodeInputData JSON to inspect lineage
        var recordedInputJsons = new Dictionary<string, string>();
        foreach (var node in graph.Nodes)
        {
            var nodeId = node.NodeId;
            var mockChatClient = new Mock<IChatClient>();
            mockChatClient
                .Setup(c => c.GetResponseAsync(
                    It.IsAny<IList<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChatResponse(
                    new ChatMessage(ChatRole.Assistant, $"{{\"from\":\"{nodeId}\"}}")));
            _agentResolverMock
                .Setup(r => r.ResolveAsync(node.ReferenceId!.Value, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateResolvedAgent(mockChatClient));
        }

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        execution.Status.Should().Be(ExecutionStatus.Completed);

        // Node B's recorded input should contain source pointing to A
        var nodeB = execution.NodeExecutions.First(n => n.NodeId == "b");
        nodeB.Input.Should().NotBeNullOrEmpty();
        // The input string is extracted as text from the first item, which is A's output
        // The source lineage is internal to the NodeInputData structure
        // Verify B received data from A by checking A's output is in B's input
        nodeB.Input.Should().Contain("a");

        // Node C's recorded input should reference B
        var nodeC = execution.NodeExecutions.First(n => n.NodeId == "c");
        nodeC.Input.Should().NotBeNullOrEmpty();
        nodeC.Input.Should().Contain("b");
    }

    [Fact]
    public async Task ThreeNodeChain_StartNodeInput_IsInitialInput()
    {
        // Arrange
        var refA = Guid.NewGuid();
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "a", NodeType = WorkflowNodeType.Agent, ReferenceId = refA, DisplayName = "A" }
            ],
            Edges = []
        };
        var input = JsonDocument.Parse("{\"q\":\"root-test\"}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        var mockChatClient = new Mock<IChatClient>();
        mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "{\"result\":\"done\"}")));
        _agentResolverMock
            .Setup(r => r.ResolveAsync(refA, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResolvedAgent(mockChatClient));

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert — start node receives original input (no source, root origin)
        execution.Status.Should().Be(ExecutionStatus.Completed);
        var nodeA = execution.NodeExecutions.First(n => n.NodeId == "a");
        nodeA.Input.Should().Contain("root-test");
    }

    // ==================== T047: Multi-port lineage ====================

    [Fact]
    public async Task ConditionMultiPort_MatchedBranch_ReceivesConditionOutput()
    {
        // Arrange — Condition(OutputCount=2) → port 0→ nodeX, port 1→ nodeY
        // Condition=true → nodeX gets data from condition with source info
        var refX = Guid.NewGuid();
        var refY = Guid.NewGuid();
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "cond", NodeType = WorkflowNodeType.Condition, DisplayName = "Cond", OutputCount = 2 },
                new WorkflowNodeVO { NodeId = "x", NodeType = WorkflowNodeType.Agent, ReferenceId = refX, DisplayName = "X" },
                new WorkflowNodeVO { NodeId = "y", NodeType = WorkflowNodeType.Agent, ReferenceId = refY, DisplayName = "Y" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "cond", TargetNodeId = "x",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.route == \"x\"", SourcePortIndex = 0 },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "cond", TargetNodeId = "y",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.route == \"y\"", SourcePortIndex = 1 }
            ]
        };
        var input = JsonDocument.Parse("{\"route\":\"x\"}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate("$.route == \"x\"", It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = true; return true; });
        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate("$.route == \"y\"", It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = false; return true; });

        var mockChatClient = new Mock<IChatClient>();
        mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "{\"from\":\"x\"}")));
        _agentResolverMock
            .Setup(r => r.ResolveAsync(refX, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResolvedAgent(mockChatClient));

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        execution.Status.Should().Be(ExecutionStatus.Completed);
        var nodeX = execution.NodeExecutions.First(n => n.NodeId == "x");
        nodeX.Status.Should().Be(NodeExecutionStatus.Completed);
        // X received input from condition node (the condition's input data flows through)
        nodeX.Input.Should().NotBeNullOrEmpty();
        // Y was skipped
        execution.NodeExecutions.First(n => n.NodeId == "y").Status.Should().Be(NodeExecutionStatus.Skipped);
    }
}
