using CoreSRE.Domain.Interfaces;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// 空操作通知实现 — 当无需推送时使用（测试场景、向后兼容）。
/// 所有方法返回 Task.CompletedTask。
/// </summary>
public class NullWorkflowExecutionNotifier : IWorkflowExecutionNotifier
{
    public Task ExecutionStartedAsync(Guid executionId, Guid workflowDefinitionId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task NodeExecutionStartedAsync(Guid executionId, string nodeId, string? input, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task NodeExecutionCompletedAsync(Guid executionId, string nodeId, string? output, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task NodeExecutionFailedAsync(Guid executionId, string nodeId, string error, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task NodeExecutionSkippedAsync(Guid executionId, string nodeId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task ExecutionCompletedAsync(Guid executionId, string? output, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task ExecutionFailedAsync(Guid executionId, string error, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
