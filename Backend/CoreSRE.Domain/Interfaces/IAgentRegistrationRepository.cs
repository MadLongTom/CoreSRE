using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;

namespace CoreSRE.Domain.Interfaces;

/// <summary>
/// AgentRegistration 专用仓储接口，扩展通用仓储以支持按类型过滤。
/// </summary>
public interface IAgentRegistrationRepository : IRepository<AgentRegistration>
{
    /// <summary>
    /// 按 Agent 类型查询。type 为 null 时返回全部。
    /// </summary>
    Task<IEnumerable<AgentRegistration>> GetByTypeAsync(AgentType? type, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按 skill name/description 关键词搜索 A2A Agent（大小写不敏感）。
    /// </summary>
    /// <param name="searchTerm">搜索关键词</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>匹配的 Agent 列表（仅 A2A 类型，AgentCard 非 null）</returns>
    Task<IReadOnlyList<AgentRegistration>> SearchBySkillAsync(
        string searchTerm,
        CancellationToken cancellationToken = default);
}
