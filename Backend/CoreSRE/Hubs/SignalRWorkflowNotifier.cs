using CoreSRE.Domain.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace CoreSRE.Hubs;

/// <summary>
/// SignalR 工作流执行通知实现 — 通过 IHubContext 将执行事件推送到 SignalR 组。
/// 每个执行通过组名 "execution:{executionId}" 隔离。
/// 包含错误处理：SignalR 推送失败不会影响工作流执行引擎。
/// </summary>
public class SignalRWorkflowNotifier : IWorkflowExecutionNotifier
{
    private readonly IHubContext<WorkflowHub, IWorkflowClient> _hubContext;
    private readonly ILogger<SignalRWorkflowNotifier> _logger;

    public SignalRWorkflowNotifier(
        IHubContext<WorkflowHub, IWorkflowClient> hubContext,
        ILogger<SignalRWorkflowNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task ExecutionStartedAsync(Guid executionId, Guid workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        var group = GroupName(executionId);
        _logger.LogDebug("SignalR push: ExecutionStarted → {Group}", group);
        await SafeSendAsync(group, () =>
            _hubContext.Clients.Group(group).ExecutionStarted(executionId, workflowDefinitionId));
    }

    public async Task NodeExecutionStartedAsync(Guid executionId, string nodeId, string? input, CancellationToken cancellationToken = default)
    {
        var group = GroupName(executionId);
        _logger.LogDebug("SignalR push: NodeExecutionStarted({NodeId}) → {Group}", nodeId, group);
        await SafeSendAsync(group, () =>
            _hubContext.Clients.Group(group).NodeExecutionStarted(executionId, nodeId, input));
    }

    public async Task NodeExecutionCompletedAsync(Guid executionId, string nodeId, string? output, CancellationToken cancellationToken = default)
    {
        var group = GroupName(executionId);
        _logger.LogDebug("SignalR push: NodeExecutionCompleted({NodeId}) → {Group}", nodeId, group);
        await SafeSendAsync(group, () =>
            _hubContext.Clients.Group(group).NodeExecutionCompleted(executionId, nodeId, output));
    }

    public async Task NodeExecutionFailedAsync(Guid executionId, string nodeId, string error, CancellationToken cancellationToken = default)
    {
        var group = GroupName(executionId);
        _logger.LogDebug("SignalR push: NodeExecutionFailed({NodeId}) → {Group}", nodeId, group);
        await SafeSendAsync(group, () =>
            _hubContext.Clients.Group(group).NodeExecutionFailed(executionId, nodeId, error));
    }

    public async Task NodeExecutionSkippedAsync(Guid executionId, string nodeId, CancellationToken cancellationToken = default)
    {
        var group = GroupName(executionId);
        _logger.LogDebug("SignalR push: NodeExecutionSkipped({NodeId}) → {Group}", nodeId, group);
        await SafeSendAsync(group, () =>
            _hubContext.Clients.Group(group).NodeExecutionSkipped(executionId, nodeId));
    }

    public async Task ExecutionCompletedAsync(Guid executionId, string? output, CancellationToken cancellationToken = default)
    {
        var group = GroupName(executionId);
        _logger.LogDebug("SignalR push: ExecutionCompleted → {Group}", group);
        await SafeSendAsync(group, () =>
            _hubContext.Clients.Group(group).ExecutionCompleted(executionId, output));
    }

    public async Task ExecutionFailedAsync(Guid executionId, string error, CancellationToken cancellationToken = default)
    {
        var group = GroupName(executionId);
        _logger.LogDebug("SignalR push: ExecutionFailed → {Group}", group);
        await SafeSendAsync(group, () =>
            _hubContext.Clients.Group(group).ExecutionFailed(executionId, error));
    }

    private static string GroupName(Guid executionId) => $"execution:{executionId}";

    /// <summary>
    /// 安全发送 SignalR 消息：捕获并记录异常，不会向上传播导致执行引擎失败。
    /// </summary>
    private async Task SafeSendAsync(string group, Func<Task> sendAction)
    {
        try
        {
            await sendAction();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR push failed for group {Group}, execution continues", group);
        }
    }
}
