using System.Text.Json;
using System.Threading.Channels;
using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Workflows.Commands.ExecuteWorkflow;
using CoreSRE.Application.Workflows.DTOs;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace CoreSRE.Application.Tests.Workflows.Commands.ExecuteWorkflow;

public class ExecuteWorkflowCommandHandlerTests
{
    private readonly Mock<IWorkflowDefinitionRepository> _workflowRepoMock = new();
    private readonly Mock<IAgentRegistrationRepository> _agentRepoMock = new();
    private readonly Mock<IToolRegistrationRepository> _toolRepoMock = new();
    private readonly Mock<IWorkflowExecutionRepository> _executionRepoMock = new();
    private readonly Channel<ExecuteWorkflowRequest> _channel;
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly ExecuteWorkflowCommandHandler _handler;

    // Dummy entities for reference validation
    private static readonly AgentRegistration DummyAgent = AgentRegistration.CreateA2A(
        "dummy-agent", null, "https://dummy.example.com", new AgentCardVO());

    private static readonly ToolRegistration DummyTool = ToolRegistration.CreateRestApi(
        "dummy-tool", null, "https://dummy.example.com",
        new AuthConfigVO { AuthType = AuthType.None }, "POST");

    private static readonly Guid AgentRefId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ToolRefId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public ExecuteWorkflowCommandHandlerTests()
    {
        _channel = Channel.CreateUnbounded<ExecuteWorkflowRequest>();
        _handler = new ExecuteWorkflowCommandHandler(
            _workflowRepoMock.Object,
            _agentRepoMock.Object,
            _toolRepoMock.Object,
            _executionRepoMock.Object,
            _channel,
            _mapperMock.Object);
    }

    private static WorkflowDefinition CreatePublishedWorkflow()
    {
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO
                {
                    NodeId = "agent-1",
                    NodeType = WorkflowNodeType.Agent,
                    ReferenceId = AgentRefId,
                    DisplayName = "Agent Node"
                },
                new WorkflowNodeVO
                {
                    NodeId = "tool-1",
                    NodeType = WorkflowNodeType.Tool,
                    ReferenceId = ToolRefId,
                    DisplayName = "Tool Node"
                }
            ],
            Edges =
            [
                new WorkflowEdgeVO
                {
                    EdgeId = "e1",
                    SourceNodeId = "agent-1",
                    TargetNodeId = "tool-1",
                    EdgeType = WorkflowEdgeType.Normal
                }
            ]
        };

        var workflow = WorkflowDefinition.Create("Test Workflow", "desc", graph);
        workflow.Publish(); // transition to Published
        return workflow;
    }

    private static WorkflowDefinition CreateDraftWorkflow()
    {
        var graph = new WorkflowGraphVO
        {
            Nodes =
            [
                new WorkflowNodeVO
                {
                    NodeId = "agent-1",
                    NodeType = WorkflowNodeType.Agent,
                    ReferenceId = AgentRefId,
                    DisplayName = "Agent Node"
                }
            ],
            Edges = []
        };
        return WorkflowDefinition.Create("Draft Workflow", "desc", graph);
    }

    private void SetupReferencesExist()
    {
        _agentRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DummyAgent);
        _toolRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DummyTool);
    }

    [Fact]
    public async Task Handle_NonExistentWorkflow_Returns404()
    {
        // Arrange
        var command = new ExecuteWorkflowCommand { WorkflowDefinitionId = Guid.NewGuid() };
        _workflowRepoMock
            .Setup(r => r.GetByIdAsync(command.WorkflowDefinitionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinition?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_DraftWorkflow_Returns400()
    {
        // Arrange
        var workflow = CreateDraftWorkflow();
        var command = new ExecuteWorkflowCommand { WorkflowDefinitionId = workflow.Id };
        _workflowRepoMock
            .Setup(r => r.GetByIdAsync(workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Published");
    }

    [Fact]
    public async Task Handle_InvalidAgentReference_Returns400()
    {
        // Arrange
        var workflow = CreatePublishedWorkflow();
        var command = new ExecuteWorkflowCommand { WorkflowDefinitionId = workflow.Id };
        _workflowRepoMock
            .Setup(r => r.GetByIdAsync(workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        // Agent reference doesn't exist
        _agentRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRegistration?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Agent");
    }

    [Fact]
    public async Task Handle_InvalidToolReference_Returns400()
    {
        // Arrange
        var workflow = CreatePublishedWorkflow();
        var command = new ExecuteWorkflowCommand { WorkflowDefinitionId = workflow.Id };
        _workflowRepoMock
            .Setup(r => r.GetByIdAsync(workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        // Agent exists but Tool doesn't
        _agentRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DummyAgent);
        _toolRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ToolRegistration?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Tool");
    }

    [Fact]
    public async Task Handle_PublishedWorkflow_CreatesExecutionAndEnqueues()
    {
        // Arrange
        var workflow = CreatePublishedWorkflow();
        var command = new ExecuteWorkflowCommand
        {
            WorkflowDefinitionId = workflow.Id,
            Input = JsonDocument.Parse("{\"query\":\"test\"}").RootElement
        };
        _workflowRepoMock
            .Setup(r => r.GetByIdAsync(workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        SetupReferencesExist();

        var expectedDto = new WorkflowExecutionDto
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflow.Id,
            Status = "Pending"
        };
        _mapperMock
            .Setup(m => m.Map<WorkflowExecutionDto>(It.IsAny<WorkflowExecution>()))
            .Returns(expectedDto);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();

        // Verify execution was persisted
        _executionRepoMock.Verify(
            r => r.AddAsync(It.IsAny<WorkflowExecution>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify request was enqueued to channel
        _channel.Reader.TryRead(out var request).Should().BeTrue();
        request.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_NullInput_DefaultsToEmptyJson()
    {
        // Arrange
        var workflow = CreatePublishedWorkflow();
        var command = new ExecuteWorkflowCommand
        {
            WorkflowDefinitionId = workflow.Id,
            Input = null
        };
        _workflowRepoMock
            .Setup(r => r.GetByIdAsync(workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        SetupReferencesExist();

        WorkflowExecution? capturedExecution = null;
        _executionRepoMock
            .Setup(r => r.AddAsync(It.IsAny<WorkflowExecution>(), It.IsAny<CancellationToken>()))
            .Returns<WorkflowExecution, CancellationToken>((exec, _) =>
            {
                capturedExecution = exec;
                return Task.FromResult(exec);
            });

        _mapperMock
            .Setup(m => m.Map<WorkflowExecutionDto>(It.IsAny<WorkflowExecution>()))
            .Returns(new WorkflowExecutionDto());

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        capturedExecution.Should().NotBeNull();
        capturedExecution!.Input.GetRawText().Should().Be("{}");
    }

    [Fact]
    public async Task Handle_PublishedWorkflow_SnapshotsGraph()
    {
        // Arrange
        var workflow = CreatePublishedWorkflow();
        var command = new ExecuteWorkflowCommand { WorkflowDefinitionId = workflow.Id };
        _workflowRepoMock
            .Setup(r => r.GetByIdAsync(workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        SetupReferencesExist();

        WorkflowExecution? capturedExecution = null;
        _executionRepoMock
            .Setup(r => r.AddAsync(It.IsAny<WorkflowExecution>(), It.IsAny<CancellationToken>()))
            .Returns<WorkflowExecution, CancellationToken>((exec, _) =>
            {
                capturedExecution = exec;
                return Task.FromResult(exec);
            });

        _mapperMock
            .Setup(m => m.Map<WorkflowExecutionDto>(It.IsAny<WorkflowExecution>()))
            .Returns(new WorkflowExecutionDto());

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert — graph snapshot should contain same nodes as workflow definition
        capturedExecution.Should().NotBeNull();
        capturedExecution!.GraphSnapshot.Nodes.Should().HaveCount(workflow.Graph.Nodes.Count);
        capturedExecution.NodeExecutions.Should().HaveCount(workflow.Graph.Nodes.Count);
        capturedExecution.Status.Should().Be(ExecutionStatus.Pending);
    }
}
