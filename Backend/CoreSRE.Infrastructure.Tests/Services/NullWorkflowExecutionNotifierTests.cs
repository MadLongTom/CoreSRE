using CoreSRE.Domain.Interfaces;
using CoreSRE.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace CoreSRE.Infrastructure.Tests.Services;

public class NullWorkflowExecutionNotifierTests
{
    private readonly IWorkflowExecutionNotifier _sut = new NullWorkflowExecutionNotifier();

    [Fact]
    public async Task ExecutionStartedAsync_ReturnsCompletedTask()
    {
        var act = () => _sut.ExecutionStartedAsync(Guid.NewGuid(), Guid.NewGuid());
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NodeExecutionStartedAsync_ReturnsCompletedTask()
    {
        var act = () => _sut.NodeExecutionStartedAsync(Guid.NewGuid(), "node-1", "input");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NodeExecutionCompletedAsync_ReturnsCompletedTask()
    {
        var act = () => _sut.NodeExecutionCompletedAsync(Guid.NewGuid(), "node-1", "output");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NodeExecutionFailedAsync_ReturnsCompletedTask()
    {
        var act = () => _sut.NodeExecutionFailedAsync(Guid.NewGuid(), "node-1", "error");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NodeExecutionSkippedAsync_ReturnsCompletedTask()
    {
        var act = () => _sut.NodeExecutionSkippedAsync(Guid.NewGuid(), "node-1");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecutionCompletedAsync_ReturnsCompletedTask()
    {
        var act = () => _sut.ExecutionCompletedAsync(Guid.NewGuid(), "output");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecutionFailedAsync_ReturnsCompletedTask()
    {
        var act = () => _sut.ExecutionFailedAsync(Guid.NewGuid(), "error");
        await act.Should().NotThrowAsync();
    }
}
