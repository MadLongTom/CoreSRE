using System.Text.Json;
using CoreSRE.Application.Interfaces;
using CoreSRE.Application.Tools.DTOs;
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

public class WorkflowEngineTests
{
    private readonly Mock<IAgentResolver> _agentResolverMock = new();
    private readonly Mock<IToolInvokerFactory> _toolInvokerFactoryMock = new();
    private readonly Mock<IToolRegistrationRepository> _toolRepoMock = new();
    private readonly Mock<IWorkflowExecutionRepository> _executionRepoMock = new();
    private readonly Mock<IConditionEvaluator> _conditionEvaluatorMock = new();
    private readonly Mock<ILogger<WorkflowEngine>> _loggerMock = new();
    private readonly WorkflowEngine _engine;

    public WorkflowEngineTests()
    {
        _engine = new WorkflowEngine(
            _agentResolverMock.Object,
            _toolInvokerFactoryMock.Object,
            _toolRepoMock.Object,
            _executionRepoMock.Object,
            _conditionEvaluatorMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// 创建 3 节点顺序 DAG: agent-1 → agent-2 → agent-3
    /// </summary>
    private static WorkflowExecution Create3NodeSequentialExecution()
    {
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO
                {
                    NodeId = "agent-1",
                    NodeType = WorkflowNodeType.Agent,
                    ReferenceId = Guid.NewGuid(),
                    DisplayName = "Agent 1"
                },
                new WorkflowNodeVO
                {
                    NodeId = "agent-2",
                    NodeType = WorkflowNodeType.Agent,
                    ReferenceId = Guid.NewGuid(),
                    DisplayName = "Agent 2"
                },
                new WorkflowNodeVO
                {
                    NodeId = "agent-3",
                    NodeType = WorkflowNodeType.Agent,
                    ReferenceId = Guid.NewGuid(),
                    DisplayName = "Agent 3"
                }
            ],
            Edges =
            [
                new WorkflowEdgeVO
                {
                    EdgeId = "e1",
                    SourceNodeId = "agent-1",
                    TargetNodeId = "agent-2",
                    EdgeType = WorkflowEdgeType.Normal
                },
                new WorkflowEdgeVO
                {
                    EdgeId = "e2",
                    SourceNodeId = "agent-2",
                    TargetNodeId = "agent-3",
                    EdgeType = WorkflowEdgeType.Normal
                }
            ]
        };

        var input = JsonDocument.Parse("{\"query\":\"hello\"}").RootElement;
        return WorkflowExecution.Create(Guid.NewGuid(), input, graph);
    }

    /// <summary>
    /// 创建一个包装 mock IChatClient 的 AIAgent（通过 AsAIAgent 扩展方法）。
    /// 这样 agent.GetService&lt;IChatClient&gt;() 能正确返回 mock 实例。
    /// </summary>
    private static AIAgent CreateMockAgent(Mock<IChatClient> mockChatClient)
    {
        return mockChatClient.Object.AsAIAgent(new ChatClientAgentOptions { Name = "test-agent" });
    }

    /// <summary>
    /// 设置 mock Agent 返回指定文本
    /// </summary>
    private void SetupAgentResponse(Guid referenceId, string responseText)
    {
        var mockChatClient = new Mock<IChatClient>();
        mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, responseText)));

        var agent = CreateMockAgent(mockChatClient);

        _agentResolverMock
            .Setup(r => r.ResolveAsync(referenceId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
    }

    /// <summary>
    /// 设置所有节点的 Agent mock 返回对应文本
    /// </summary>
    private void SetupAllAgentResponses(WorkflowExecution execution, Func<string, string> responseFactory)
    {
        foreach (var node in execution.GraphSnapshot.Nodes)
        {
            if (node.NodeType == WorkflowNodeType.Agent && node.ReferenceId.HasValue)
            {
                SetupAgentResponse(node.ReferenceId.Value, responseFactory(node.NodeId));
            }
        }
    }

    // ==================== 拓扑排序测试 ====================

    [Fact]
    public void TopologicalSort_SequentialDAG_ReturnsCorrectOrder()
    {
        var graph = new WorkflowGraphVO
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

        var sorted = WorkflowEngine.TopologicalSort(graph);

        sorted.Select(n => n.NodeId).Should().ContainInOrder("A", "B", "C");
    }

    [Fact]
    public void TopologicalSort_SingleNode_ReturnsNode()
    {
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "A", NodeType = WorkflowNodeType.Agent, DisplayName = "A" }
            ],
            Edges = []
        };

        var sorted = WorkflowEngine.TopologicalSort(graph);

        sorted.Should().HaveCount(1);
        sorted[0].NodeId.Should().Be("A");
    }

    // ==================== 顺序执行测试 ====================

    [Fact]
    public async Task ExecuteAsync_3NodeSequential_ExecutesInOrder()
    {
        // Arrange
        var execution = Create3NodeSequentialExecution();
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
                        new ChatMessage(ChatRole.Assistant, $"output-{nodeId}")));
                });

            var agent = CreateMockAgent(mockChatClient);

            _agentResolverMock
                .Setup(r => r.ResolveAsync(node.ReferenceId!.Value, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(agent);
        }

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        executionOrder.Should().ContainInOrder("agent-1", "agent-2", "agent-3");
        execution.Status.Should().Be(ExecutionStatus.Completed);
        execution.Output.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_3NodeSequential_PassesOutputAsNextInput()
    {
        // Arrange
        var execution = Create3NodeSequentialExecution();
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
                    if (userMsg?.Text is not null)
                        capturedInputs[nodeId] = userMsg.Text;

                    return Task.FromResult(new ChatResponse(
                        new ChatMessage(ChatRole.Assistant, $"{{\"result\":\"{nodeId}\"}}") ));
                });

            var agent = CreateMockAgent(mockChatClient);

            _agentResolverMock
                .Setup(r => r.ResolveAsync(node.ReferenceId!.Value, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(agent);
        }

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        capturedInputs["agent-1"].Should().Contain("hello");
        capturedInputs["agent-2"].Should().Contain("agent-1");
        capturedInputs["agent-3"].Should().Contain("agent-2");
    }

    [Fact]
    public async Task ExecuteAsync_NodeFailure_StopsSubsequentNodes()
    {
        // Arrange
        var execution = Create3NodeSequentialExecution();
        var executedNodes = new List<string>();

        foreach (var node in execution.GraphSnapshot.Nodes)
        {
            var nodeId = node.NodeId;
            var mockChatClient = new Mock<IChatClient>();

            if (nodeId == "agent-2")
            {
                mockChatClient
                    .Setup(c => c.GetResponseAsync(
                        It.IsAny<IList<ChatMessage>>(),
                        It.IsAny<ChatOptions?>(),
                        It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidOperationException("Agent 2 failed"));
            }
            else
            {
                mockChatClient
                    .Setup(c => c.GetResponseAsync(
                        It.IsAny<IList<ChatMessage>>(),
                        It.IsAny<ChatOptions?>(),
                        It.IsAny<CancellationToken>()))
                    .Returns<IList<ChatMessage>, ChatOptions?, CancellationToken>((msgs, opts, ct) =>
                    {
                        executedNodes.Add(nodeId);
                        return Task.FromResult(new ChatResponse(
                            new ChatMessage(ChatRole.Assistant, $"output-{nodeId}")));
                    });
            }

            var agent = CreateMockAgent(mockChatClient);

            _agentResolverMock
                .Setup(r => r.ResolveAsync(node.ReferenceId!.Value, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(agent);
        }

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        executedNodes.Should().ContainSingle("agent-1");
        execution.Status.Should().Be(ExecutionStatus.Failed);
        execution.ErrorMessage.Should().Contain("agent-2");

        var node2 = execution.NodeExecutions.First(n => n.NodeId == "agent-2");
        node2.Status.Should().Be(NodeExecutionStatus.Failed);

        var node3 = execution.NodeExecutions.First(n => n.NodeId == "agent-3");
        node3.Status.Should().Be(NodeExecutionStatus.Pending);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulExecution_AllNodesCompleted()
    {
        // Arrange
        var execution = Create3NodeSequentialExecution();
        SetupAllAgentResponses(execution, nodeId => $"{{\"done\":\"{nodeId}\"}}");

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        execution.Status.Should().Be(ExecutionStatus.Completed);
        execution.CompletedAt.Should().NotBeNull();
        execution.ErrorMessage.Should().BeNull();

        foreach (var ne in execution.NodeExecutions)
        {
            ne.Status.Should().Be(NodeExecutionStatus.Completed);
            ne.CompletedAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulExecution_PersistsStatePerNode()
    {
        // Arrange
        var execution = Create3NodeSequentialExecution();
        SetupAllAgentResponses(execution, nodeId => $"output-{nodeId}");

        var updateCount = 0;
        _executionRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<WorkflowExecution>(), It.IsAny<CancellationToken>()))
            .Callback(() => updateCount++)
            .Returns(Task.CompletedTask);

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert — expect: 1 (Start) + 3×(StartNode + CompleteNode) + 1 (Complete) = 8 updates
        updateCount.Should().BeGreaterThanOrEqualTo(8);
    }

    [Fact]
    public async Task ExecuteAsync_SingleToolNode_InvokesTool()
    {
        // Arrange
        var toolRefId = Guid.NewGuid();
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO
                {
                    NodeId = "tool-1",
                    NodeType = WorkflowNodeType.Tool,
                    ReferenceId = toolRefId,
                    DisplayName = "Tool 1"
                }
            ],
            Edges = []
        };

        var input = JsonDocument.Parse("{\"param\":\"value\"}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        var dummyTool = ToolRegistration.CreateRestApi(
            "test-tool", null, "https://api.example.com",
            new AuthConfigVO { AuthType = AuthType.None }, "POST");

        _toolRepoMock
            .Setup(r => r.GetByIdAsync(toolRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dummyTool);

        var mockInvoker = new Mock<IToolInvoker>();
        mockInvoker
            .Setup(i => i.InvokeAsync(
                It.IsAny<ToolRegistration>(),
                It.IsAny<string?>(),
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<IDictionary<string, string>?>(),
                It.IsAny<IDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolInvocationResultDto
            {
                Success = true,
                Data = JsonDocument.Parse("{\"result\":\"done\"}").RootElement
            });

        _toolInvokerFactoryMock
            .Setup(f => f.GetInvoker(It.IsAny<ToolType>()))
            .Returns(mockInvoker.Object);

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        execution.Status.Should().Be(ExecutionStatus.Completed);
        execution.NodeExecutions[0].Status.Should().Be(NodeExecutionStatus.Completed);
        execution.NodeExecutions[0].Output.Should().Contain("done");
    }

    // ==================== FanOut/FanIn 并行执行测试 ====================

    /// <summary>
    /// 创建 FanOut/FanIn 并行 DAG: FanOut → agent-a, agent-b, agent-c → FanIn
    /// </summary>
    private static WorkflowExecution CreateFanOutFanInExecution()
    {
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "fanout", NodeType = WorkflowNodeType.FanOut, DisplayName = "FanOut" },
                new WorkflowNodeVO { NodeId = "agent-a", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "Agent A" },
                new WorkflowNodeVO { NodeId = "agent-b", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "Agent B" },
                new WorkflowNodeVO { NodeId = "agent-c", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "Agent C" },
                new WorkflowNodeVO { NodeId = "fanin", NodeType = WorkflowNodeType.FanIn, DisplayName = "FanIn" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "fanout", TargetNodeId = "agent-a", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "fanout", TargetNodeId = "agent-b", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e3", SourceNodeId = "fanout", TargetNodeId = "agent-c", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e4", SourceNodeId = "agent-a", TargetNodeId = "fanin", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e5", SourceNodeId = "agent-b", TargetNodeId = "fanin", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e6", SourceNodeId = "agent-c", TargetNodeId = "fanin", EdgeType = WorkflowEdgeType.Normal }
            ]
        };

        var input = JsonDocument.Parse("{\"query\":\"analyze\"}").RootElement;
        return WorkflowExecution.Create(Guid.NewGuid(), input, graph);
    }

    [Fact]
    public async Task ExecuteAsync_FanOutFanIn_DispatchesToAllParallelNodes()
    {
        // Arrange
        var execution = CreateFanOutFanInExecution();
        var executedNodes = new List<string>();

        foreach (var node in execution.GraphSnapshot.Nodes.Where(n => n.NodeType == WorkflowNodeType.Agent))
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
                    lock (executedNodes) { executedNodes.Add(nodeId); }
                    return Task.FromResult(new ChatResponse(
                        new ChatMessage(ChatRole.Assistant, $"{{\"result\":\"{nodeId}\"}}")));
                });

            var agent = CreateMockAgent(mockChatClient);
            _agentResolverMock
                .Setup(r => r.ResolveAsync(node.ReferenceId!.Value, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(agent);
        }

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        executedNodes.Should().HaveCount(3);
        executedNodes.Should().Contain("agent-a");
        executedNodes.Should().Contain("agent-b");
        executedNodes.Should().Contain("agent-c");
        execution.Status.Should().Be(ExecutionStatus.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_FanOutFanIn_AggregatesOutputsAsJsonArray()
    {
        // Arrange
        var execution = CreateFanOutFanInExecution();
        SetupAllAgentResponses(execution, nodeId => $"{{\"result\":\"{nodeId}\"}}");

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        execution.Status.Should().Be(ExecutionStatus.Completed);
        var output = execution.Output!.Value.GetRawText();
        var outputArray = JsonDocument.Parse(output).RootElement;
        outputArray.ValueKind.Should().Be(JsonValueKind.Array);
        outputArray.GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_FanOutFanIn_PartialFailure_CompletesOtherNodes()
    {
        // Arrange
        var execution = CreateFanOutFanInExecution();

        foreach (var node in execution.GraphSnapshot.Nodes.Where(n => n.NodeType == WorkflowNodeType.Agent))
        {
            if (node.NodeId == "agent-b")
            {
                var failMock = new Mock<IChatClient>();
                failMock
                    .Setup(c => c.GetResponseAsync(
                        It.IsAny<IList<ChatMessage>>(),
                        It.IsAny<ChatOptions?>(),
                        It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidOperationException("Agent B failed"));

                var failAgent = CreateMockAgent(failMock);
                _agentResolverMock
                    .Setup(r => r.ResolveAsync(node.ReferenceId!.Value, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(failAgent);
            }
            else
            {
                SetupAgentResponse(node.ReferenceId!.Value, $"{{\"result\":\"{node.NodeId}\"}}");
            }
        }

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        var agentA = execution.NodeExecutions.First(n => n.NodeId == "agent-a");
        agentA.Status.Should().Be(NodeExecutionStatus.Completed);

        var agentB = execution.NodeExecutions.First(n => n.NodeId == "agent-b");
        agentB.Status.Should().Be(NodeExecutionStatus.Failed);

        var agentC = execution.NodeExecutions.First(n => n.NodeId == "agent-c");
        agentC.Status.Should().Be(NodeExecutionStatus.Completed);

        // Workflow fails per FR-013: any parallel branch failure → workflow Failed
        execution.Status.Should().Be(ExecutionStatus.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_FanOutFanIn_AllFailed_WorkflowFails()
    {
        // Arrange
        var execution = CreateFanOutFanInExecution();

        foreach (var node in execution.GraphSnapshot.Nodes.Where(n => n.NodeType == WorkflowNodeType.Agent))
        {
            var mockChatClient = new Mock<IChatClient>();
            mockChatClient
                .Setup(c => c.GetResponseAsync(
                    It.IsAny<IList<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException($"{node.NodeId} failed"));

            var agent = CreateMockAgent(mockChatClient);
            _agentResolverMock
                .Setup(r => r.ResolveAsync(node.ReferenceId!.Value, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(agent);
        }

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        foreach (var ne in execution.NodeExecutions.Where(n =>
            n.NodeId is "agent-a" or "agent-b" or "agent-c"))
        {
            ne.Status.Should().Be(NodeExecutionStatus.Failed);
        }
        execution.Status.Should().Be(ExecutionStatus.Failed);
    }

    // ==================== 条件分支执行测试 ====================

    /// <summary>
    /// 创建条件分支 DAG: condition → ($.severity == "high") → agent-high
    ///                              → ($.severity == "low")  → agent-low
    /// </summary>
    private static WorkflowExecution CreateConditionalExecution()
    {
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "condition", NodeType = WorkflowNodeType.Condition, DisplayName = "Condition" },
                new WorkflowNodeVO { NodeId = "agent-high", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "Agent High" },
                new WorkflowNodeVO { NodeId = "agent-low", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "Agent Low" }
            ],
            Edges =
            [
                new WorkflowEdgeVO
                {
                    EdgeId = "e1", SourceNodeId = "condition", TargetNodeId = "agent-high",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.severity == \"high\""
                },
                new WorkflowEdgeVO
                {
                    EdgeId = "e2", SourceNodeId = "condition", TargetNodeId = "agent-low",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.severity == \"low\""
                }
            ]
        };

        var input = JsonDocument.Parse("{\"severity\":\"high\"}").RootElement;
        return WorkflowExecution.Create(Guid.NewGuid(), input, graph);
    }

    [Fact]
    public async Task ExecuteAsync_Conditional_MatchingBranchExecutes_OtherSkipped()
    {
        // Arrange
        var execution = CreateConditionalExecution();

        // Setup condition evaluator: "high" matches, "low" doesn't
        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate("$.severity == \"high\"", It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = true; return true; });
        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate("$.severity == \"low\"", It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = false; return true; });

        var agentHighNode = execution.GraphSnapshot.Nodes.First(n => n.NodeId == "agent-high");
        SetupAgentResponse(agentHighNode.ReferenceId!.Value, "{\"handled\":\"high\"}");

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        execution.Status.Should().Be(ExecutionStatus.Completed);

        var conditionNode = execution.NodeExecutions.First(n => n.NodeId == "condition");
        conditionNode.Status.Should().Be(NodeExecutionStatus.Completed);

        var highNode = execution.NodeExecutions.First(n => n.NodeId == "agent-high");
        highNode.Status.Should().Be(NodeExecutionStatus.Completed);

        var lowNode = execution.NodeExecutions.First(n => n.NodeId == "agent-low");
        lowNode.Status.Should().Be(NodeExecutionStatus.Skipped);
    }

    [Fact]
    public async Task ExecuteAsync_Conditional_NoBranchMatches_WorkflowFails()
    {
        // Arrange — input severity is "medium", neither "high" nor "low"
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "condition", NodeType = WorkflowNodeType.Condition, DisplayName = "Condition" },
                new WorkflowNodeVO { NodeId = "agent-high", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "Agent High" },
                new WorkflowNodeVO { NodeId = "agent-low", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "Agent Low" }
            ],
            Edges =
            [
                new WorkflowEdgeVO
                {
                    EdgeId = "e1", SourceNodeId = "condition", TargetNodeId = "agent-high",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.severity == \"high\""
                },
                new WorkflowEdgeVO
                {
                    EdgeId = "e2", SourceNodeId = "condition", TargetNodeId = "agent-low",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.severity == \"low\""
                }
            ]
        };
        var input = JsonDocument.Parse("{\"severity\":\"medium\"}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        // Both conditions return false
        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate(It.IsAny<string>(), It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = false; return true; });

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        execution.Status.Should().Be(ExecutionStatus.Failed);
        execution.ErrorMessage.Should().Contain("无匹配的条件分支");
    }

    [Fact]
    public async Task ExecuteAsync_Conditional_ParseError_ConditionNodeFails()
    {
        // Arrange
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "condition", NodeType = WorkflowNodeType.Condition, DisplayName = "Condition" },
                new WorkflowNodeVO { NodeId = "agent-branch", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "Agent Branch" }
            ],
            Edges =
            [
                new WorkflowEdgeVO
                {
                    EdgeId = "e1", SourceNodeId = "condition", TargetNodeId = "agent-branch",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "invalid expression"
                }
            ]
        };
        var input = JsonDocument.Parse("{\"data\":\"test\"}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        // TryEvaluate returns false (parse error)
        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate("invalid expression", It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = false; return false; });

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        execution.Status.Should().Be(ExecutionStatus.Failed);

        var conditionNode = execution.NodeExecutions.First(n => n.NodeId == "condition");
        conditionNode.Status.Should().Be(NodeExecutionStatus.Failed);
    }
}
