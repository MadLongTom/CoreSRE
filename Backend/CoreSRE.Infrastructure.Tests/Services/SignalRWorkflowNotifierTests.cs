using CoreSRE.Hubs;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CoreSRE.Infrastructure.Tests.Services;

/// <summary>
/// T010: SignalRWorkflowNotifier 单元测试。
/// 验证每个方法通过 IHubContext 将事件推送到正确的 SignalR 组。
/// </summary>
public class SignalRWorkflowNotifierTests
{
    private readonly Mock<IHubContext<WorkflowHub, IWorkflowClient>> _hubContextMock = new();
    private readonly Mock<IHubClients<IWorkflowClient>> _hubClientsMock = new();
    private readonly Mock<IWorkflowClient> _groupClientMock = new();
    private readonly SignalRWorkflowNotifier _sut;

    public SignalRWorkflowNotifierTests()
    {
        _hubContextMock.Setup(h => h.Clients).Returns(_hubClientsMock.Object);
        _hubClientsMock
            .Setup(c => c.Group(It.IsAny<string>()))
            .Returns(_groupClientMock.Object);

        _sut = new SignalRWorkflowNotifier(
            _hubContextMock.Object,
            new Mock<ILogger<SignalRWorkflowNotifier>>().Object);
    }

    [Fact]
    public async Task ExecutionStartedAsync_SendsToCorrectGroup()
    {
        var executionId = Guid.NewGuid();
        var workflowDefinitionId = Guid.NewGuid();

        await _sut.ExecutionStartedAsync(executionId, workflowDefinitionId);

        _hubClientsMock.Verify(c => c.Group($"execution:{executionId}"), Times.Once);
        _groupClientMock.Verify(c => c.ExecutionStarted(executionId, workflowDefinitionId), Times.Once);
    }

    [Fact]
    public async Task NodeExecutionStartedAsync_SendsToCorrectGroup()
    {
        var executionId = Guid.NewGuid();

        await _sut.NodeExecutionStartedAsync(executionId, "node-1", "input-data");

        _hubClientsMock.Verify(c => c.Group($"execution:{executionId}"), Times.Once);
        _groupClientMock.Verify(c => c.NodeExecutionStarted(executionId, "node-1", "input-data"), Times.Once);
    }

    [Fact]
    public async Task NodeExecutionCompletedAsync_SendsToCorrectGroup()
    {
        var executionId = Guid.NewGuid();

        await _sut.NodeExecutionCompletedAsync(executionId, "node-1", "output-data");

        _hubClientsMock.Verify(c => c.Group($"execution:{executionId}"), Times.Once);
        _groupClientMock.Verify(c => c.NodeExecutionCompleted(executionId, "node-1", "output-data"), Times.Once);
    }

    [Fact]
    public async Task NodeExecutionFailedAsync_SendsToCorrectGroup()
    {
        var executionId = Guid.NewGuid();

        await _sut.NodeExecutionFailedAsync(executionId, "node-1", "error message");

        _hubClientsMock.Verify(c => c.Group($"execution:{executionId}"), Times.Once);
        _groupClientMock.Verify(c => c.NodeExecutionFailed(executionId, "node-1", "error message"), Times.Once);
    }

    [Fact]
    public async Task NodeExecutionSkippedAsync_SendsToCorrectGroup()
    {
        var executionId = Guid.NewGuid();

        await _sut.NodeExecutionSkippedAsync(executionId, "node-1");

        _hubClientsMock.Verify(c => c.Group($"execution:{executionId}"), Times.Once);
        _groupClientMock.Verify(c => c.NodeExecutionSkipped(executionId, "node-1"), Times.Once);
    }

    [Fact]
    public async Task ExecutionCompletedAsync_SendsToCorrectGroup()
    {
        var executionId = Guid.NewGuid();

        await _sut.ExecutionCompletedAsync(executionId, "final-output");

        _hubClientsMock.Verify(c => c.Group($"execution:{executionId}"), Times.Once);
        _groupClientMock.Verify(c => c.ExecutionCompleted(executionId, "final-output"), Times.Once);
    }

    [Fact]
    public async Task ExecutionFailedAsync_SendsToCorrectGroup()
    {
        var executionId = Guid.NewGuid();

        await _sut.ExecutionFailedAsync(executionId, "fatal error");

        _hubClientsMock.Verify(c => c.Group($"execution:{executionId}"), Times.Once);
        _groupClientMock.Verify(c => c.ExecutionFailed(executionId, "fatal error"), Times.Once);
    }

    [Fact]
    public async Task AllMethods_UseConsistentGroupNameFormat()
    {
        var executionId = Guid.NewGuid();
        var expectedGroup = $"execution:{executionId}";

        await _sut.ExecutionStartedAsync(executionId, Guid.NewGuid());
        await _sut.NodeExecutionStartedAsync(executionId, "n1", null);
        await _sut.NodeExecutionCompletedAsync(executionId, "n1", null);
        await _sut.NodeExecutionFailedAsync(executionId, "n2", "err");
        await _sut.NodeExecutionSkippedAsync(executionId, "n3");
        await _sut.ExecutionCompletedAsync(executionId, null);
        await _sut.ExecutionFailedAsync(executionId, "err");

        _hubClientsMock.Verify(c => c.Group(expectedGroup), Times.Exactly(7));
    }
}
