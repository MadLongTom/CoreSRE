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
/// T039: 多输出端口选择性分发测试。
/// </summary>
public class MultiPortOutputTests
{
    private readonly Mock<IAgentResolver> _agentResolverMock = new();
    private readonly Mock<IToolInvokerFactory> _toolInvokerFactoryMock = new();
    private readonly Mock<IToolRegistrationRepository> _toolRepoMock = new();
    private readonly Mock<IWorkflowExecutionRepository> _executionRepoMock = new();
    private readonly Mock<IConditionEvaluator> _conditionEvaluatorMock = new();
    private readonly Mock<IExpressionEvaluator> _expressionEvaluatorMock = new();
    private readonly Mock<ILogger<WorkflowEngine>> _loggerMock = new();
    private readonly WorkflowEngine _engine;

    public MultiPortOutputTests()
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

    [Fact]
    public async Task MultiOutput_OnlyPortsWithData_TriggerDownstream()
    {
        // Arrange — Condition with OutputCount=3 routes to port 0 only
        // Port 0 → agentA, port 1 → agentB (no data), port 2 → agentC (no data)
        var refA = Guid.NewGuid();
        var refB = Guid.NewGuid();
        var refC = Guid.NewGuid();
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "cond", NodeType = WorkflowNodeType.Condition, DisplayName = "Cond", OutputCount = 3 },
                new WorkflowNodeVO { NodeId = "a", NodeType = WorkflowNodeType.Agent, ReferenceId = refA, DisplayName = "A" },
                new WorkflowNodeVO { NodeId = "b", NodeType = WorkflowNodeType.Agent, ReferenceId = refB, DisplayName = "B" },
                new WorkflowNodeVO { NodeId = "c", NodeType = WorkflowNodeType.Agent, ReferenceId = refC, DisplayName = "C" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "cond", TargetNodeId = "a",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.match == 0", SourcePortIndex = 0 },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "cond", TargetNodeId = "b",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.match == 1", SourcePortIndex = 1 },
                new WorkflowEdgeVO { EdgeId = "e3", SourceNodeId = "cond", TargetNodeId = "c",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.match == 2", SourcePortIndex = 2 }
            ]
        };
        var input = JsonDocument.Parse("{\"match\":0}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate("$.match == 0", It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = true; return true; });
        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate("$.match == 1", It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = false; return true; });
        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate("$.match == 2", It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = false; return true; });

        SetupAgentResponse(refA, "{\"result\":\"A\"}");

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        execution.Status.Should().Be(ExecutionStatus.Completed);
        execution.NodeExecutions.First(n => n.NodeId == "a").Status.Should().Be(NodeExecutionStatus.Completed);
        execution.NodeExecutions.First(n => n.NodeId == "b").Status.Should().Be(NodeExecutionStatus.Skipped);
        execution.NodeExecutions.First(n => n.NodeId == "c").Status.Should().Be(NodeExecutionStatus.Skipped);
    }

    [Fact]
    public async Task FanOut_CopiesInputToAllDownstream()
    {
        // Arrange — FanOut fans out to 2 agents, then to FanIn
        var refA = Guid.NewGuid();
        var refB = Guid.NewGuid();
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "fo", NodeType = WorkflowNodeType.FanOut, DisplayName = "FO" },
                new WorkflowNodeVO { NodeId = "a", NodeType = WorkflowNodeType.Agent, ReferenceId = refA, DisplayName = "A" },
                new WorkflowNodeVO { NodeId = "b", NodeType = WorkflowNodeType.Agent, ReferenceId = refB, DisplayName = "B" },
                new WorkflowNodeVO { NodeId = "fi", NodeType = WorkflowNodeType.FanIn, DisplayName = "FI" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "fo", TargetNodeId = "a", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "fo", TargetNodeId = "b", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e3", SourceNodeId = "a", TargetNodeId = "fi", EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e4", SourceNodeId = "b", TargetNodeId = "fi", EdgeType = WorkflowEdgeType.Normal }
            ]
        };
        var input = JsonDocument.Parse("{\"data\":\"fanout-test\"}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        var receivedInputs = new Dictionary<string, string>();
        foreach (var node in graph.Nodes.Where(n => n.NodeType == WorkflowNodeType.Agent))
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
                    if (userMsg?.Text is not null) receivedInputs[nodeId] = userMsg.Text;
                    return Task.FromResult(new ChatResponse(
                        new ChatMessage(ChatRole.Assistant, $"{{\"from\":\"{nodeId}\"}}")));
                });
            _agentResolverMock
                .Setup(r => r.ResolveAsync(node.ReferenceId!.Value, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateResolvedAgent(mockChatClient));
        }

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert — both agents receive input from FanOut
        execution.Status.Should().Be(ExecutionStatus.Completed);
        receivedInputs.Should().ContainKey("a");
        receivedInputs.Should().ContainKey("b");
        // Both should have received the same FanOut input (containing the original data)
        receivedInputs["a"].Should().Contain("fanout-test");
        receivedInputs["b"].Should().Contain("fanout-test");
    }
}
