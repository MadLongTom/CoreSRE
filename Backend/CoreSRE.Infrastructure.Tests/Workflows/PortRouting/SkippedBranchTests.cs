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
/// T040: 跳过分支链测试 — 端口无数据时，整个下游链不执行。
/// </summary>
public class SkippedBranchTests
{
    private readonly Mock<IAgentResolver> _agentResolverMock = new();
    private readonly Mock<IToolInvokerFactory> _toolInvokerFactoryMock = new();
    private readonly Mock<IToolRegistrationRepository> _toolRepoMock = new();
    private readonly Mock<IWorkflowExecutionRepository> _executionRepoMock = new();
    private readonly Mock<IConditionEvaluator> _conditionEvaluatorMock = new();
    private readonly Mock<IExpressionEvaluator> _expressionEvaluatorMock = new();
    private readonly Mock<ILogger<WorkflowEngine>> _loggerMock = new();
    private readonly WorkflowEngine _engine;

    public SkippedBranchTests()
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
    public async Task SkippedBranch_EntireDownstreamChainNotExecuted()
    {
        // Arrange — cond(OutputCount=2) → port 0: a1 → a2, port 1: a3 → a4
        // Condition=true → skip port 1 → a3, a4 never execute
        var refA1 = Guid.NewGuid();
        var refA2 = Guid.NewGuid();
        var refA3 = Guid.NewGuid();
        var refA4 = Guid.NewGuid();
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "cond", NodeType = WorkflowNodeType.Condition, DisplayName = "Cond", OutputCount = 2 },
                new WorkflowNodeVO { NodeId = "a1", NodeType = WorkflowNodeType.Agent, ReferenceId = refA1, DisplayName = "A1" },
                new WorkflowNodeVO { NodeId = "a2", NodeType = WorkflowNodeType.Agent, ReferenceId = refA2, DisplayName = "A2" },
                new WorkflowNodeVO { NodeId = "a3", NodeType = WorkflowNodeType.Agent, ReferenceId = refA3, DisplayName = "A3" },
                new WorkflowNodeVO { NodeId = "a4", NodeType = WorkflowNodeType.Agent, ReferenceId = refA4, DisplayName = "A4" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "cond", TargetNodeId = "a1",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.ok == true", SourcePortIndex = 0 },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "cond", TargetNodeId = "a3",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.ok == false", SourcePortIndex = 1 },
                new WorkflowEdgeVO { EdgeId = "e3", SourceNodeId = "a1", TargetNodeId = "a2",
                    EdgeType = WorkflowEdgeType.Normal },
                new WorkflowEdgeVO { EdgeId = "e4", SourceNodeId = "a3", TargetNodeId = "a4",
                    EdgeType = WorkflowEdgeType.Normal }
            ]
        };
        var input = JsonDocument.Parse("{\"ok\":true}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate("$.ok == true", It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = true; return true; });
        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate("$.ok == false", It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = false; return true; });

        SetupAgentResponse(refA1, "{\"from\":\"a1\"}");
        SetupAgentResponse(refA2, "{\"from\":\"a2\"}");
        // Do NOT set up a3 or a4 — they should never be called

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        execution.Status.Should().Be(ExecutionStatus.Completed);
        execution.NodeExecutions.First(n => n.NodeId == "a1").Status.Should().Be(NodeExecutionStatus.Completed);
        execution.NodeExecutions.First(n => n.NodeId == "a2").Status.Should().Be(NodeExecutionStatus.Completed);
        execution.NodeExecutions.First(n => n.NodeId == "a3").Status.Should().Be(NodeExecutionStatus.Skipped);
        // a4 should remain Pending (never reached, never skipped explicitly)
        var a4 = execution.NodeExecutions.FirstOrDefault(n => n.NodeId == "a4");
        a4.Should().NotBeNull();
        a4!.Status.Should().Be(NodeExecutionStatus.Pending);
    }
}
