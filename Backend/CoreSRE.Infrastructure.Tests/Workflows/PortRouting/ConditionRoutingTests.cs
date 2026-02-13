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
/// T038: Condition 节点 OutputCount=2 路由测试 — true 流向端口 0，false 流向端口 1。
/// </summary>
public class ConditionRoutingTests
{
    private readonly Mock<IAgentResolver> _agentResolverMock = new();
    private readonly Mock<IToolInvokerFactory> _toolInvokerFactoryMock = new();
    private readonly Mock<IToolRegistrationRepository> _toolRepoMock = new();
    private readonly Mock<IWorkflowExecutionRepository> _executionRepoMock = new();
    private readonly Mock<IConditionEvaluator> _conditionEvaluatorMock = new();
    private readonly Mock<IExpressionEvaluator> _expressionEvaluatorMock = new();
    private readonly Mock<ILogger<WorkflowEngine>> _loggerMock = new();
    private readonly WorkflowEngine _engine;

    public ConditionRoutingTests()
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
    public async Task Condition_OutputCount2_TrueCondition_RoutesToPort0Target()
    {
        // Arrange — Condition OutputCount=2, port 0 → trueAgent, port 1 → falseAgent
        var trueRef = Guid.NewGuid();
        var falseRef = Guid.NewGuid();
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "cond", NodeType = WorkflowNodeType.Condition, DisplayName = "Cond", OutputCount = 2 },
                new WorkflowNodeVO { NodeId = "true-agent", NodeType = WorkflowNodeType.Agent, ReferenceId = trueRef, DisplayName = "True" },
                new WorkflowNodeVO { NodeId = "false-agent", NodeType = WorkflowNodeType.Agent, ReferenceId = falseRef, DisplayName = "False" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "cond", TargetNodeId = "true-agent",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.val == \"yes\"", SourcePortIndex = 0 },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "cond", TargetNodeId = "false-agent",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.val == \"no\"", SourcePortIndex = 1 }
            ]
        };
        var input = JsonDocument.Parse("{\"val\":\"yes\"}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate("$.val == \"yes\"", It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = true; return true; });
        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate("$.val == \"no\"", It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = false; return true; });

        SetupAgentResponse(trueRef, "{\"branch\":\"true\"}");

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        execution.Status.Should().Be(ExecutionStatus.Completed);
        var trueNode = execution.NodeExecutions.First(n => n.NodeId == "true-agent");
        trueNode.Status.Should().Be(NodeExecutionStatus.Completed);
        var falseNode = execution.NodeExecutions.First(n => n.NodeId == "false-agent");
        falseNode.Status.Should().Be(NodeExecutionStatus.Skipped);
    }

    [Fact]
    public async Task Condition_OutputCount2_FalseCondition_RoutesToPort1Target()
    {
        // Arrange — condition=false → port 1 → falseAgent
        var trueRef = Guid.NewGuid();
        var falseRef = Guid.NewGuid();
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "cond", NodeType = WorkflowNodeType.Condition, DisplayName = "Cond", OutputCount = 2 },
                new WorkflowNodeVO { NodeId = "true-agent", NodeType = WorkflowNodeType.Agent, ReferenceId = trueRef, DisplayName = "True" },
                new WorkflowNodeVO { NodeId = "false-agent", NodeType = WorkflowNodeType.Agent, ReferenceId = falseRef, DisplayName = "False" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "cond", TargetNodeId = "true-agent",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.active == true", SourcePortIndex = 0 },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "cond", TargetNodeId = "false-agent",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.active == false", SourcePortIndex = 1 }
            ]
        };
        var input = JsonDocument.Parse("{\"active\":false}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate("$.active == true", It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = false; return true; });
        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate("$.active == false", It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = true; return true; });

        SetupAgentResponse(falseRef, "{\"branch\":\"false\"}");

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        execution.Status.Should().Be(ExecutionStatus.Completed);
        var trueNode = execution.NodeExecutions.First(n => n.NodeId == "true-agent");
        trueNode.Status.Should().Be(NodeExecutionStatus.Skipped);
        var falseNode = execution.NodeExecutions.First(n => n.NodeId == "false-agent");
        falseNode.Status.Should().Be(NodeExecutionStatus.Completed);
    }

    [Fact]
    public async Task Condition_OutputCount2_ChainAfterBranch_OnlyMatchingChainExecutes()
    {
        // Arrange — cond → trueAgent → chainAgent (only if true), cond → falseAgent
        var trueRef = Guid.NewGuid();
        var chainRef = Guid.NewGuid();
        var falseRef = Guid.NewGuid();
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO { NodeId = "cond", NodeType = WorkflowNodeType.Condition, DisplayName = "Cond", OutputCount = 2 },
                new WorkflowNodeVO { NodeId = "trueA", NodeType = WorkflowNodeType.Agent, ReferenceId = trueRef, DisplayName = "TrueA" },
                new WorkflowNodeVO { NodeId = "chainA", NodeType = WorkflowNodeType.Agent, ReferenceId = chainRef, DisplayName = "ChainA" },
                new WorkflowNodeVO { NodeId = "falseA", NodeType = WorkflowNodeType.Agent, ReferenceId = falseRef, DisplayName = "FalseA" }
            ],
            Edges =
            [
                new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "cond", TargetNodeId = "trueA",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.go == true", SourcePortIndex = 0 },
                new WorkflowEdgeVO { EdgeId = "e2", SourceNodeId = "cond", TargetNodeId = "falseA",
                    EdgeType = WorkflowEdgeType.Conditional, Condition = "$.go == false", SourcePortIndex = 1 },
                new WorkflowEdgeVO { EdgeId = "e3", SourceNodeId = "trueA", TargetNodeId = "chainA",
                    EdgeType = WorkflowEdgeType.Normal }
            ]
        };
        var input = JsonDocument.Parse("{\"go\":true}").RootElement;
        var execution = WorkflowExecution.Create(Guid.NewGuid(), input, graph);

        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate("$.go == true", It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = true; return true; });
        _conditionEvaluatorMock
            .Setup(e => e.TryEvaluate("$.go == false", It.IsAny<string>(), out It.Ref<bool>.IsAny))
            .Returns((string _, string _, out bool result) => { result = false; return true; });

        SetupAgentResponse(trueRef, "{\"from\":\"trueA\"}");
        SetupAgentResponse(chainRef, "{\"from\":\"chainA\"}");

        // Act
        await _engine.ExecuteAsync(execution, CancellationToken.None);

        // Assert
        execution.Status.Should().Be(ExecutionStatus.Completed);
        execution.NodeExecutions.First(n => n.NodeId == "trueA").Status.Should().Be(NodeExecutionStatus.Completed);
        execution.NodeExecutions.First(n => n.NodeId == "chainA").Status.Should().Be(NodeExecutionStatus.Completed);
        execution.NodeExecutions.First(n => n.NodeId == "falseA").Status.Should().Be(NodeExecutionStatus.Skipped);
    }
}
