namespace CoreSRE.Hubs;

/// <summary>
/// SignalR 强类型 Hub 客户端接口 — 定义服务端可以在客户端上调用的方法。
/// 方法名与前端 connection.on("MethodName", handler) 注册的名称完全匹配。
/// </summary>
public interface IWorkflowClient
{
    /// <summary>工作流执行已开始</summary>
    Task ExecutionStarted(Guid executionId, Guid workflowDefinitionId);

    /// <summary>节点开始执行</summary>
    Task NodeExecutionStarted(Guid executionId, string nodeId, string? input);

    /// <summary>节点执行成功完成</summary>
    Task NodeExecutionCompleted(Guid executionId, string nodeId, string? output);

    /// <summary>节点执行失败</summary>
    Task NodeExecutionFailed(Guid executionId, string nodeId, string error);

    /// <summary>节点被跳过</summary>
    Task NodeExecutionSkipped(Guid executionId, string nodeId);

    /// <summary>工作流执行成功完成</summary>
    Task ExecutionCompleted(Guid executionId, string? output);

    /// <summary>工作流执行失败</summary>
    Task ExecutionFailed(Guid executionId, string error);
}
