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

namespace CoreSRE.Infrastructure.Tests.Services;

/// <summary>
/// T011: WorkflowEngine 通知器集成测试。
/// 验证执行引擎在节点生命周期钩子处正确调用 IWorkflowExecutionNotifier。
/// </summary>
public class WorkflowEngineNotifierTests
{
    private readonly Mock<IAgentResolver> _agentResolverMock = new();
    private readonly Mock<IToolInvokerFactory> _toolInvokerFactoryMock = new();
    private readonly Mock<IToolRegistrationRepository> _toolRepoMock = new();
    private readonly Mock<IWorkflowExecutionRepository> _executionRepoMock = new();
    private readonly Mock<IConditionEvaluator> _conditionEvaluatorMock = new();
    private readonly Mock<IExpressionEvaluator> _expressionEvaluatorMock = new();
    private readonly Mock<IWorkflowExecutionNotifier> _notifierMock = new();
    private readonly Mock<ILogger<WorkflowEngine>> _loggerMock = new();
    private readonly WorkflowEngine _engine;

    public WorkflowEngineNotifierTests()
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
            _notifierMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// 创建单节点 Agent DAG 执行
    /// </summary>
    private static WorkflowExecution CreateSingleAgentExecution()
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
                }
            ],
            Edges = []
        };

        return WorkflowExecution.Create(Guid.NewGuid(), JsonDocument.Parse("\"hello\"").RootElement, graph);
    }

    /// <summary>
    /// 创建 2 节点顺序 DAG: agent-1 → agent-2
    /// </summary>
    private static WorkflowExecution Create2NodeSequentialExecution()
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
                }
            ],
            Edges =
            [
                new WorkflowEdgeVO
                {
                    SourceNodeId = "agent-1",
                    TargetNodeId = "agent-2",
                    SourcePortIndex = 0,
                    TargetPortIndex = 0
                }
            ]
        };

        return WorkflowExecution.Create(Guid.NewGuid(), JsonDocument.Parse("\"hello\"").RootElement, graph);
    }

    private void SetupMockAgent(string response)
    {
        var mockChatClient = new Mock<IChatClient>();
        mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, response)));

        var agent = mockChatClient.Object.AsAIAgent(new ChatClientAgentOptions { Name = "test-agent" });
        var resolvedAgent = new ResolvedAgent(agent, null);
        _agentResolverMock
            .Setup(r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedAgent);
    }

    [Fact]
    public async Task ExecuteAsync_SingleNode_CallsNotifierInCorrectOrder()
    {
        var execution = CreateSingleAgentExecution();
        SetupMockAgent("response-1");

        var callOrder = new List<string>();
        _notifierMock
            .Setup(n => n.ExecutionStartedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("ExecutionStarted"))
            .Returns(Task.CompletedTask);
        _notifierMock
            .Setup(n => n.NodeExecutionStartedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("NodeStarted"))
            .Returns(Task.CompletedTask);
        _notifierMock
            .Setup(n => n.NodeExecutionCompletedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("NodeCompleted"))
            .Returns(Task.CompletedTask);
        _notifierMock
            .Setup(n => n.ExecutionCompletedAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("ExecutionCompleted"))
            .Returns(Task.CompletedTask);

        await _engine.ExecuteAsync(execution, CancellationToken.None);

        callOrder.Should().ContainInOrder("ExecutionStarted", "NodeStarted", "NodeCompleted", "ExecutionCompleted");
    }

    [Fact]
    public async Task ExecuteAsync_TwoNodes_CallsNodeNotificationsForEach()
    {
        var execution = Create2NodeSequentialExecution();
        SetupMockAgent("response");

        var nodeStartedIds = new List<string>();
        var nodeCompletedIds = new List<string>();

        _notifierMock
            .Setup(n => n.NodeExecutionStartedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, string?, CancellationToken>((_, nodeId, _, _) => nodeStartedIds.Add(nodeId))
            .Returns(Task.CompletedTask);
        _notifierMock
            .Setup(n => n.NodeExecutionCompletedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, string?, CancellationToken>((_, nodeId, _, _) => nodeCompletedIds.Add(nodeId))
            .Returns(Task.CompletedTask);

        await _engine.ExecuteAsync(execution, CancellationToken.None);

        nodeStartedIds.Should().BeEquivalentTo(["agent-1", "agent-2"]);
        nodeCompletedIds.Should().BeEquivalentTo(["agent-1", "agent-2"]);

        _notifierMock.Verify(n => n.ExecutionStartedAsync(
            execution.Id, execution.WorkflowDefinitionId, It.IsAny<CancellationToken>()), Times.Once);
        _notifierMock.Verify(n => n.ExecutionCompletedAsync(
            execution.Id, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NodeFails_CallsFailNotifications()
    {
        var execution = CreateSingleAgentExecution();

        var mockChatClient = new Mock<IChatClient>();
        mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("agent error"));

        var agent = mockChatClient.Object.AsAIAgent(new ChatClientAgentOptions { Name = "test-agent" });
        var resolvedAgent = new ResolvedAgent(agent, null);
        _agentResolverMock
            .Setup(r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedAgent);

        await _engine.ExecuteAsync(execution, CancellationToken.None);

        _notifierMock.Verify(n => n.ExecutionStartedAsync(
            execution.Id, execution.WorkflowDefinitionId, It.IsAny<CancellationToken>()), Times.Once);
        _notifierMock.Verify(n => n.NodeExecutionStartedAsync(
            execution.Id, "agent-1", It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _notifierMock.Verify(n => n.NodeExecutionFailedAsync(
            execution.Id, "agent-1", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _notifierMock.Verify(n => n.ExecutionFailedAsync(
            execution.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_PassesCorrectExecutionId()
    {
        var execution = CreateSingleAgentExecution();
        SetupMockAgent("output");

        await _engine.ExecuteAsync(execution, CancellationToken.None);

        _notifierMock.Verify(n => n.ExecutionStartedAsync(
            execution.Id, execution.WorkflowDefinitionId, It.IsAny<CancellationToken>()), Times.Once);
        _notifierMock.Verify(n => n.NodeExecutionStartedAsync(
            execution.Id, "agent-1", It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _notifierMock.Verify(n => n.NodeExecutionCompletedAsync(
            execution.Id, "agent-1", It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _notifierMock.Verify(n => n.ExecutionCompletedAsync(
            execution.Id, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
