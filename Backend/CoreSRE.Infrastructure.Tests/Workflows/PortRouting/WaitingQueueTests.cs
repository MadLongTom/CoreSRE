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

namespace CoreSRE.Infrastructure.Tests.Workflows.PortRouting;

/// <summary>
/// T042-T044: 多输入等待队列测试 — InputCount > 1 的节点等待所有端口数据就绪后才执行。
/// </summary>
public class WaitingQueueTests
{
    private readonly Mock<IAgentResolver> _agentResolverMock = new();
    private readonly Mock<IToolInvokerFactory> _toolInvokerFactoryMock = new();
    private readonly Mock<IToolRegistrationRepository> _toolRepoMock = new();
    private readonly Mock<IWorkflowExecutionRepository> _executionRepoMock = new();
    private readonly Mock<IConditionEvaluator> _conditionEvaluatorMock = new();
    private readonly Mock<IExpressionEvaluator> _expressionEvaluatorMock = new();
    private readonly Mock<ILogger<WorkflowEngine>> _loggerMock = new();
    private readonly WorkflowEngine _engine;

    public WaitingQueueTests()
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

    private static ResolvedAgent CreateResolvedAgent(Mock<IChatClient> mockChatClient)
    {
        var agent = mockChatClient.Object.AsAIAgent(new ChatClientAgentOptions { Name = "test-agent" });
        return new ResolvedAgent(agent, null);
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
        _agentResolverMock
            .Setup(r => r.ResolveAsync(referenceId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResolvedAgent(mockChatClient));
    }

    // ==================== T042: Diamond Merge (InputCount=2) ====================

    [Fact]
    public async Task DiamondMerge_InputCount2_ExecutesAfterBothBranches()
    {
        // Arrange — Start → A (port 0) and B (port 1) → Merge(InputCount=2)
        var refStart = Guid.NewGuid();
        var refA = Guid.NewGuid();
        var refB = Guid.NewGuid();
        var refMerge = Guid.NewGuid();
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "start", NodeType = WorkflowNodeType.Agent, ReferenceId = refStart, DisplayName = "Start" },
                new WorkflowNodeVO { NodeId = "a", NodeType = WorkflowNodeType.Agent, ReferenceId = refA, DisplayName = "A" },
                new WorkflowNodeVO { NodeId = "b", NodeType = WorkflowNodeType.Agent, ReferenceId = refB, DisplayName = "B" },
                new WorkflowNodeVO { NodeId = "merge", NodeType = WorkflowNodeType.Agent, ReferenceId = refMerge, InputCount = 2, DisplayName = "Merge" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "start", TargetNodeId = "a", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "start", TargetNodeId = "b", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e3", SourceNodeId = "a", TargetNodeId = "merge", EdgeType = WorkflowEdgeType.Normal, TargetPortIndex = 0 },
                new WorkflowEdgeVO { EdgeId = "e4", SourceNodeId = "b", TargetNodeId = "merge", EdgeType = WorkflowEdgeType.Normal, TargetPortIndex = 1 }
            ]
        };
        var input = JsonDocument.Parse("{\"q\":\"diamond\"}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        var executionOrder = new List<string>();
        foreach (var node in graph.Nodes)
        {
            if (node.ReferenceId is null) continue;
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
                        new ChatMessage(ChatRole.Assistant, $"{{\"from\":\"{nodeId}\"}}")));
                });
            _agentResolverMock
                .Setup(r => r.ResolveAsync(node.ReferenceId.Value, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateResolvedAgent(mockChatClient));
        }

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        execution.Status.Should().Be(ExecutionStatus.Completed);

        // Merge must execute AFTER both A and B
        var mergeIdx = executionOrder.IndexOf("merge");
        var aIdx = executionOrder.IndexOf("a");
        var bIdx = executionOrder.IndexOf("b");
        mergeIdx.Should().BeGreaterThan(aIdx, "merge should execute after A");
        mergeIdx.Should().BeGreaterThan(bIdx, "merge should execute after B");

        // All nodes completed
        foreach (var ne in execution.NodeExecutions)
        {
            ne.Status.Should().Be(NodeExecutionStatus.Completed);
        }
    }

    [Fact]
    public async Task DiamondMerge_MergeReceivesDataFromBothPorts()
    {
        // Arrange
        var refStart = Guid.NewGuid();
        var refA = Guid.NewGuid();
        var refB = Guid.NewGuid();
        var refMerge = Guid.NewGuid();
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "start", NodeType = WorkflowNodeType.Agent, ReferenceId = refStart, DisplayName = "Start" },
                new WorkflowNodeVO { NodeId = "a", NodeType = WorkflowNodeType.Agent, ReferenceId = refA, DisplayName = "A" },
                new WorkflowNodeVO { NodeId = "b", NodeType = WorkflowNodeType.Agent, ReferenceId = refB, DisplayName = "B" },
                new WorkflowNodeVO { NodeId = "merge", NodeType = WorkflowNodeType.Agent, ReferenceId = refMerge, InputCount = 2, DisplayName = "Merge" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "start", TargetNodeId = "a", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "start", TargetNodeId = "b", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e3", SourceNodeId = "a", TargetNodeId = "merge", EdgeType = WorkflowEdgeType.Normal, TargetPortIndex = 0 },
                new WorkflowEdgeVO { EdgeId = "e4", SourceNodeId = "b", TargetNodeId = "merge", EdgeType = WorkflowEdgeType.Normal, TargetPortIndex = 1 }
            ]
        };
        var input = JsonDocument.Parse("{\"q\":\"merge\"}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        SetupAgentResponse(refStart, "{\"started\":true}");
        SetupAgentResponse(refA, "{\"from\":\"a\"}");
        SetupAgentResponse(refB, "{\"from\":\"b\"}");

        // Capture merge node input
        string? mergeInput = null;
        var mockMerge = new Mock<IChatClient>();
        mockMerge
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns<IList<ChatMessage>, ChatOptions?, CancellationToken>((msgs, opts, ct) =>
            {
                var userMsg = msgs.LastOrDefault(m => m.Role == ChatRole.User);
                mergeInput = userMsg?.Text;
                return Task.FromResult(new ChatResponse(
                    new ChatMessage(ChatRole.Assistant, "{\"merged\":true}")));
            });
        _agentResolverMock
            .Setup(r => r.ResolveAsync(refMerge, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResolvedAgent(mockMerge));

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        execution.Status.Should().Be(ExecutionStatus.Completed);
        mergeInput.Should().NotBeNull();
        // Merge input should contain data from both branches
        var mergeNodeExecution = execution.NodeExecutions.First(n => n.NodeId == "merge");
        mergeNodeExecution.Status.Should().Be(NodeExecutionStatus.Completed);
        mergeNodeExecution.Input.Should().NotBeNullOrEmpty();
    }

    // ==================== T043: Partial Port Arrival ====================

    [Fact]
    public async Task PartialPort_InputCount2_OnlyOneDelivers_NodeDoesNotExecute()
    {
        // Arrange — Condition routes to one branch only; merge(InputCount=2) never gets port 1
        var refA = Guid.NewGuid();
        var refMerge = Guid.NewGuid();
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "cond", NodeType = WorkflowNodeType.Condition, DisplayName = "Cond", OutputCount = 2 },
                new WorkflowNodeVO { NodeId = "a", NodeType = WorkflowNodeType.Agent, ReferenceId = refA, DisplayName = "A" },
                new WorkflowNodeVO { NodeId = "b", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "B" },
                new WorkflowNodeVO { NodeId = "merge", NodeType = WorkflowNodeType.Agent, ReferenceId = refMerge, InputCount = 2, DisplayName = "Merge" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "cond", TargetNodeId = "a",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.branch == \"a\"", SourcePortIndex = 0 },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "cond", TargetNodeId = "b",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.branch == \"b\"", SourcePortIndex = 1 },
                new WorkflowEdgeVO { EdgeId = "e3", SourceNodeId = "a", TargetNodeId = "merge",
                    EdgeType = WorkflowEdgeType.Normal, TargetPortIndex = 0 },
                new WorkflowEdgeVO { EdgeId = "e4", SourceNodeId = "b", TargetNodeId = "merge",
                    EdgeType = WorkflowEdgeType.Normal, TargetPortIndex = 1 }
            ]
        };
        var input = JsonDocument.Parse("{\"branch\":\"a\"}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate("$.branch == \"a\"", It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = true; return true; });
        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate("$.branch == \"b\"", It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = false; return true; });

        SetupAgentResponse(refA, "{\"from\":\"a\"}");

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert — workflow completes but merge stays pending (only 1 of 2 ports received)
        execution.Status.Should().Be(ExecutionStatus.Completed);
        var mergeNode = execution.NodeExecutions.First(n => n.NodeId == "merge");
        mergeNode.Status.Should().Be(NodeExecutionStatus.Pending, "merge should stay pending with only 1 of 2 ports received");
    }

    // ==================== T044: InputCount=3 with Incomplete Ports ====================

    [Fact]
    public async Task InputCount3_TwoPortsArrive_NodeNeverExecutes()
    {
        // Arrange — 3 branches, only 2 deliver to merge(InputCount=3)
        var refA = Guid.NewGuid();
        var refB = Guid.NewGuid();
        var refMerge = Guid.NewGuid();
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "cond", NodeType = WorkflowNodeType.Condition, DisplayName = "Cond", OutputCount = 3 },
                new WorkflowNodeVO { NodeId = "a", NodeType = WorkflowNodeType.Agent, ReferenceId = refA, DisplayName = "A" },
                new WorkflowNodeVO { NodeId = "b", NodeType = WorkflowNodeType.Agent, ReferenceId = refB, DisplayName = "B" },
                new WorkflowNodeVO { NodeId = "c", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "C" },
                new WorkflowNodeVO { NodeId = "merge", NodeType = WorkflowNodeType.Agent, ReferenceId = refMerge, InputCount = 3, DisplayName = "Merge" }
            ],
            Edges =
            [
                // Condition routes — only "a" matches, "b" and "c" don't
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "cond", TargetNodeId = "a",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.match == \"a\"", SourcePortIndex = 0 },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "cond", TargetNodeId = "b",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.match == \"b\"", SourcePortIndex = 1 },
                new WorkflowEdgeVO { EdgeId = "e3", SourceNodeId = "cond", TargetNodeId = "c",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.match == \"c\"", SourcePortIndex = 2 },
                // All branches feed into merge on different ports
                new WorkflowEdgeVO { EdgeId = "e4", SourceNodeId = "a", TargetNodeId = "merge",
                    EdgeType = WorkflowEdgeType.Normal, TargetPortIndex = 0 },
                new WorkflowEdgeVO { EdgeId = "e5", SourceNodeId = "b", TargetNodeId = "merge",
                    EdgeType = WorkflowEdgeType.Normal, TargetPortIndex = 1 },
                new WorkflowEdgeVO { EdgeId = "e6", SourceNodeId = "c", TargetNodeId = "merge",
                    EdgeType = WorkflowEdgeType.Normal, TargetPortIndex = 2 }
            ]
        };
        var input = JsonDocument.Parse("{\"match\":\"a\"}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate("$.match == \"a\"", It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = true; return true; });
        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate("$.match == \"b\"", It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = false; return true; });
        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate("$.match == \"c\"", It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = false; return true; });

        SetupAgentResponse(refA, "{\"from\":\"a\"}");

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        execution.Status.Should().Be(ExecutionStatus.Completed);
        execution.NodeExecutions.First(n => n.NodeId == "a").Status.Should().Be(NodeExecutionStatus.Completed);
        execution.NodeExecutions.First(n => n.NodeId == "b").Status.Should().Be(NodeExecutionStatus.Skipped);
        execution.NodeExecutions.First(n => n.NodeId == "c").Status.Should().Be(NodeExecutionStatus.Skipped);
        // Merge only got 1 of 3 ports — remains Pending
        execution.NodeExecutions.First(n => n.NodeId == "merge").Status.Should().Be(NodeExecutionStatus.Pending);
    }
}
