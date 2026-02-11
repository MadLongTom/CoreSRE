using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;

namespace CoreSRE.Domain.Interfaces;

/// <summary>
/// WorkflowDefinition 专用仓储接口
/// </summary>
public interface IWorkflowDefinitionRepository : IRepository<WorkflowDefinition>
{
    /// <summary>按名称查询（用于唯一性检查）</summary>
    Task<WorkflowDefinition?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>按状态过滤查询</summary>
    Task<IEnumerable<WorkflowDefinition>> GetByStatusAsync(WorkflowStatus status, CancellationToken cancellationToken = default);

    /// <summary>检查名称是否已存在（排除指定 ID，用于更新场景）</summary>
    Task<bool> ExistsWithNameAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>检查工作流是否被 AgentRegistration 引用</summary>
    Task<bool> IsReferencedByAgentAsync(Guid workflowId, CancellationToken cancellationToken = default);
}
