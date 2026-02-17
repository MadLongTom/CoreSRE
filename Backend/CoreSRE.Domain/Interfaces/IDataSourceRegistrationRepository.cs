using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;

namespace CoreSRE.Domain.Interfaces;

/// <summary>
/// DataSourceRegistration 仓储接口
/// </summary>
public interface IDataSourceRegistrationRepository : IRepository<DataSourceRegistration>
{
    /// <summary>按 Category 过滤查询</summary>
    Task<IReadOnlyList<DataSourceRegistration>> GetByCategoryAsync(
        DataSourceCategory category,
        CancellationToken ct = default);

    /// <summary>按名称查询（精确匹配）</summary>
    Task<DataSourceRegistration?> GetByNameAsync(
        string name,
        CancellationToken ct = default);

    /// <summary>分页查询（支持 Category/Status 过滤 + 关键词搜索）</summary>
    Task<(IReadOnlyList<DataSourceRegistration> Items, int TotalCount)> GetPagedAsync(
        DataSourceCategory? category = null,
        DataSourceStatus? status = null,
        string? search = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    /// <summary>按 ID 批量查询</summary>
    Task<IReadOnlyList<DataSourceRegistration>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken ct = default);
}
