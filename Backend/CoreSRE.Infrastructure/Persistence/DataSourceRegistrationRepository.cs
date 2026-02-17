using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CoreSRE.Infrastructure.Persistence;

/// <summary>
/// DataSourceRegistration 仓储实现
/// </summary>
public class DataSourceRegistrationRepository : Repository<DataSourceRegistration>, IDataSourceRegistrationRepository
{
    public DataSourceRegistrationRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DataSourceRegistration>> GetByCategoryAsync(
        DataSourceCategory category,
        CancellationToken ct = default)
    {
        return await _dbSet
            .Where(d => d.Category == category)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<DataSourceRegistration?> GetByNameAsync(
        string name,
        CancellationToken ct = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(d => d.Name == name, ct);
    }

    /// <inheritdoc/>
    public async Task<(IReadOnlyList<DataSourceRegistration> Items, int TotalCount)> GetPagedAsync(
        DataSourceCategory? category = null,
        DataSourceStatus? status = null,
        string? search = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = _dbSet.AsQueryable();

        if (category is not null)
            query = query.Where(d => d.Category == category.Value);

        if (status is not null)
            query = query.Where(d => d.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var lowerSearch = search.ToLower();
            query = query.Where(d =>
                d.Name.ToLower().Contains(lowerSearch) ||
                (d.Description != null && d.Description.ToLower().Contains(lowerSearch)));
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DataSourceRegistration>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken ct = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
            return [];

        return await _dbSet
            .Where(d => idList.Contains(d.Id))
            .ToListAsync(ct);
    }
}
