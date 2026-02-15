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

namespace CoreSRE.Infrastructure.Tests.Workflows.ExecutionStack;

/// <summary>
/// T034-T036: 向后兼容测试 — 验证旧式工作流（无端口字段）在新引擎下行为一致。
/// </summary>
public class BackwardCompatTests
{
    private readonly Mock<IAgentResolver> _agentResolverMock = new();
    private readonly Mock<IToolInvokerFactory> _toolInvokerFactoryMock = new();
    private readonly Mock<IToolRegistrationRepository> _toolRepoMock = new();
    private readonly Mock<IWorkflowExecutionRepository> _executionRepoMock = new();
    private readonly Mock<IConditionEvaluator> _conditionEvaluatorMock = new();
    private readonly Mock<IExpressionEvaluator> _expressionEvaluatorMock = new();
    private readonly Mock<ILogger<WorkflowEngine>> _loggerMock = new();
    private readonly WorkflowEngine _engine;

    public BackwardCompatTests()
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
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));

        var agent = CreateResolvedAgent(mockChatClient);
        _agentResolverMock
            .Setup(r => r.ResolveAsync(referenceId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
    }

    // ==================== T034: 线性 Agent 链（无端口字段）====================

    [Fact]
    public async Task LinearAgentChain_NoPortFields_ExecutesInOrder()
    {
        // Arrange — 3 nodes with NO InputCount/OutputCount/PortIndex specified (all defaults)
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "a1", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "A1" },
                new WorkflowNodeVO { NodeId = "a2", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "A2" },
                new WorkflowNodeVO { NodeId = "a3", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "A3" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "a1", TargetNodeId = "a2", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "a2", TargetNodeId = "a3", EdgeType = WorkflowEdgeType.Normal }
            ]
        };
        var input = JsonDocument.Parse("{\"query\":\"backward-compat\"}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        var executionOrder = new List<string>();
        foreach (var node in execution.GraphSnapshot.Nodes)
        {
            var nodeId = node.NodeId;
            var mockChatClient = new Mock<IChatClient>();
            mockChatClient
                .Setup(c => c.GetResponseAsync(
                    It.IsAny<IList<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .Returns<IList<ChatMessage>, ChatOptions?, CancellationToken>((msgs, opts, ct) =>
                {
                    executionOrder.Add(nodeId);
                    return Task.FromResult(new ChatResponse(
                        new ChatMessage(ChatRole.Assistant, $"{{\"result\":\"{nodeId}\"}}")));
                });
            _agentResolverMock
                .Setup(r => r.ResolveAsync(node.ReferenceId!.Value, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateResolvedAgent(mockChatClient));
        }

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        executionOrder.Should().ContainInOrder("a1", "a2", "a3");
        execution.Status.Should().Be(ExecutionStatus.Completed);
        foreach (var ne in execution.NodeExecutions)
        {
            ne.Status.Should().Be(NodeExecutionStatus.Completed);
            ne.Input.Should().NotBeNullOrEmpty("each node should have recorded input");
            ne.Output.Should().NotBeNullOrEmpty("each node should have recorded output");
        }
    }

    [Fact]
    public async Task LinearAgentChain_NoPortFields_PreviousOutputBecomesNextInput()
    {
        // Arrange
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "a1", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "A1" },
                new WorkflowNodeVO { NodeId = "a2", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "A2" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "a1", TargetNodeId = "a2", EdgeType = WorkflowEdgeType.Normal }
            ]
        };
        var input = JsonDocument.Parse("{\"query\":\"hello\"}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        var capturedInputs = new Dictionary<string, string>();
        foreach (var node in execution.GraphSnapshot.Nodes)
        {
            var nodeId = node.NodeId;
            var mockChatClient = new Mock<IChatClient>();
            mockChatClient
                .Setup(c => c.GetResponseAsync(
                    It.IsAny<IList<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .Returns<IList<ChatMessage>, ChatOptions?, CancellationToken>((msgs, opts, ct) =>
                {
                    var userMsg = msgs.LastOrDefault(m => m.Role == ChatRole.User);
                    if (userMsg?.Text is not null) capturedInputs[nodeId] = userMsg.Text;
                    return Task.FromResult(new ChatResponse(
                        new ChatMessage(ChatRole.Assistant, $"{{\"from\":\"{nodeId}\"}}")));
                });
            _agentResolverMock
                .Setup(r => r.ResolveAsync(node.ReferenceId!.Value, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateResolvedAgent(mockChatClient));
        }

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        capturedInputs["a1"].Should().Contain("hello");
        capturedInputs["a2"].Should().Contain("a1"); // a2 receives a1's output
        execution.Status.Should().Be(ExecutionStatus.Completed);
    }

    // ==================== T035: FanOut→FanIn（无端口字段）====================

    [Fact]
    public async Task FanOutFanIn_NoPortFields_DispatchesAndAggregates()
    {
        // Arrange — traditional FanOut/FanIn without port fields
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "fo", NodeType = WorkflowNodeType.FanOut, DisplayName = "FO" },
                new WorkflowNodeVO { NodeId = "pa", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "PA" },
                new WorkflowNodeVO { NodeId = "pb", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "PB" },
                new WorkflowNodeVO { NodeId = "fi", NodeType = WorkflowNodeType.FanIn, DisplayName = "FI" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "fo", TargetNodeId = "pa", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "fo", TargetNodeId = "pb", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e3", SourceNodeId = "pa", TargetNodeId = "fi", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e4", SourceNodeId = "pb", TargetNodeId = "fi", EdgeType = WorkflowEdgeType.Normal }
            ]
        };
        var input = JsonDocument.Parse("{\"data\":\"test\"}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        foreach (var node in execution.GraphSnapshot.Nodes.Where(n => n.NodeType == WorkflowNodeType.Agent))
        {
            SetupAgentResponse(node.ReferenceId!.Value, $"{{\"result\":\"{node.NodeId}\"}}");
        }

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        execution.Status.Should().Be(ExecutionStatus.Completed);

        // FanIn output should be a JSON array
        var output = execution.Output!.Value.GetRawText();
        var outputArray = JsonDocument.Parse(output).RootElement;
        outputArray.ValueKind.Should().Be(JsonValueKind.Array);
        outputArray.GetArrayLength().Should().Be(2);

        // All nodes should be completed
        foreach (var ne in execution.NodeExecutions)
        {
            ne.Status.Should().Be(NodeExecutionStatus.Completed);
        }
    }

    // ==================== T036: Condition（无端口字段）====================

    [Fact]
    public async Task Condition_NoPortFields_RoutesToMatchingBranch()
    {
        // Arrange — traditional Condition without port fields
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "cond", NodeType = WorkflowNodeType.Condition, DisplayName = "Cond" },
                new WorkflowNodeVO { NodeId = "yes", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "Yes" },
                new WorkflowNodeVO { NodeId = "no", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "No" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "cond", TargetNodeId = "yes",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.flag == true" },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "cond", TargetNodeId = "no",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.flag == false" }
            ]
        };
        var input = JsonDocument.Parse("{\"flag\":true}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        // Setup condition evaluator
        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate("$.flag == true", It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = true; return true; });
        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate("$.flag == false", It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = false; return true; });

        SetupAgentResponse(execution.GraphSnapshot.Nodes.First(n => n.NodeId == "yes").ReferenceId!.Value, "{\"branch\":\"yes\"}");

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        execution.Status.Should().Be(ExecutionStatus.Completed);

        var condNode = execution.NodeExecutions.First(n => n.NodeId == "cond");
        condNode.Status.Should().Be(NodeExecutionStatus.Completed);

        var yesNode = execution.NodeExecutions.First(n => n.NodeId == "yes");
        yesNode.Status.Should().Be(NodeExecutionStatus.Completed);

        var noNode = execution.NodeExecutions.First(n => n.NodeId == "no");
        noNode.Status.Should().Be(NodeExecutionStatus.Skipped);
    }

    [Fact]
    public async Task Condition_NoPortFields_NoMatch_WorkflowFails()
    {
        // Arrange
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "cond", NodeType = WorkflowNodeType.Condition, DisplayName = "Cond" },
                new WorkflowNodeVO { NodeId = "branch", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "Branch" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "cond", TargetNodeId = "branch",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.x == 999" }
            ]
        };
        var input = JsonDocument.Parse("{\"x\":0}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate(It.IsAny<string>(), It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = false; return true; });

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        execution.Status.Should().Be(ExecutionStatus.Failed);
        execution.ErrorMessage.Should().Contain("无匹配的条件分支");
    }
}
