namespace CoreSRE.Application.Workflows.Commands.ExecuteWorkflow;

/// <summary>
/// 工作流执行请求（投递到 Channel 的消息）
/// </summary>
public record ExecuteWorkflowRequest(Guid ExecutionId);
