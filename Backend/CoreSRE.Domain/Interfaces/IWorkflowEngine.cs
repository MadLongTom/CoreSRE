using CoreSRE.Domain.Entities;

namespace CoreSRE.Domain.Interfaces;

/// <summary>
/// 工作流执行引擎接口。将 DAG 转换为 Agent Framework Workflow 并执行。
/// </summary>
public interface IWorkflowEngine
{
    /// <summary>
    /// 执行工作流。接收已创建的 WorkflowExecution 实体，构建 Workflow 并执行，
    /// 执行过程中实时更新 execution 的节点状态并通过 Repository 持久化。
    /// </summary>
    Task ExecuteAsync(WorkflowExecution execution, CancellationToken cancellationToken = default);
}
