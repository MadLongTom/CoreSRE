namespace CoreSRE.Domain.Interfaces;

/// <summary>
/// 工作流执行通知接口 — 定义执行引擎与推送层之间的契约。
/// 执行引擎在节点生命周期钩子处调用此接口，将状态变更事件推送到观察者。
/// </summary>
public interface IWorkflowExecutionNotifier
{
    /// <summary>工作流执行已开始</summary>
    Task ExecutionStartedAsync(Guid executionId, Guid workflowDefinitionId, CancellationToken cancellationToken = default);

    /// <summary>节点开始执行</summary>
    Task NodeExecutionStartedAsync(Guid executionId, string nodeId, string? input, CancellationToken cancellationToken = default);

    /// <summary>节点执行成功完成</summary>
    Task NodeExecutionCompletedAsync(Guid executionId, string nodeId, string? output, CancellationToken cancellationToken = default);

    /// <summary>节点执行失败</summary>
    Task NodeExecutionFailedAsync(Guid executionId, string nodeId, string error, CancellationToken cancellationToken = default);

    /// <summary>节点被跳过（条件路由未匹配）</summary>
    Task NodeExecutionSkippedAsync(Guid executionId, string nodeId, CancellationToken cancellationToken = default);

    /// <summary>工作流执行成功完成</summary>
    Task ExecutionCompletedAsync(Guid executionId, string? output, CancellationToken cancellationToken = default);

    /// <summary>工作流执行失败</summary>
    Task ExecutionFailedAsync(Guid executionId, string error, CancellationToken cancellationToken = default);
}
