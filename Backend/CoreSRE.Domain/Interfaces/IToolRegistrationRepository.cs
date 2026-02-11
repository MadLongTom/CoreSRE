using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;

namespace CoreSRE.Domain.Interfaces;

/// <summary>
/// ToolRegistration 专用仓储接口，扩展通用仓储以支持按类型/状态过滤和分页查询。
/// </summary>
public interface IToolRegistrationRepository : IRepository<ToolRegistration>
{
    /// <summary>
    /// 按工具类型查询。type 为 null 时返回全部。
    /// </summary>
    Task<IEnumerable<ToolRegistration>> GetByTypeAsync(ToolType? type, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按工具名称精确查询。
    /// </summary>
    Task<ToolRegistration?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// 分页查询支持按类型、状态、关键词筛选。
    /// </summary>
    /// <param name="type">可选工具类型过滤</param>
    /// <param name="status">可选状态过滤</param>
    /// <param name="search">可选名称/描述关键词搜索（大小写不敏感）</param>
    /// <param name="page">页码（从 1 开始）</param>
    /// <param name="pageSize">每页数量</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>分页结果（items + totalCount）</returns>
    Task<(IReadOnlyList<ToolRegistration> Items, int TotalCount)> GetPagedAsync(
        ToolType? type,
        ToolStatus? status,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
