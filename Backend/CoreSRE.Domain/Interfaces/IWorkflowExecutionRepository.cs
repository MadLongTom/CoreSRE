using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;

namespace CoreSRE.Domain.Interfaces;

/// <summary>
/// WorkflowExecution 专用仓储接口
/// </summary>
public interface IWorkflowExecutionRepository : IRepository<WorkflowExecution>
{
    /// <summary>按工作流定义 ID 查询执行记录</summary>
    Task<IEnumerable<WorkflowExecution>> GetByWorkflowIdAsync(Guid workflowDefinitionId, CancellationToken cancellationToken = default);

    /// <summary>按执行状态过滤查询</summary>
    Task<IEnumerable<WorkflowExecution>> GetByStatusAsync(ExecutionStatus status, CancellationToken cancellationToken = default);
}
