using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CoreSRE.Infrastructure.Persistence;

/// <summary>
/// ToolRegistration 专用仓储实现
/// </summary>
public class ToolRegistrationRepository : Repository<ToolRegistration>, IToolRegistrationRepository
{
    public ToolRegistrationRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc/>
    public async Task<IEnumerable<ToolRegistration>> GetByTypeAsync(
        ToolType? type,
        CancellationToken cancellationToken = default)
    {
        if (type is null)
            return await _dbSet.ToListAsync(cancellationToken);

        return await _dbSet
            .Where(t => t.ToolType == type.Value)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ToolRegistration?> GetByNameAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(t => t.Name == name, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<(IReadOnlyList<ToolRegistration> Items, int TotalCount)> GetPagedAsync(
        ToolType? type,
        ToolStatus? status,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet.AsQueryable();

        if (type is not null)
            query = query.Where(t => t.ToolType == type.Value);

        if (status is not null)
            query = query.Where(t => t.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var lowerSearch = search.ToLower();
            query = query.Where(t =>
                t.Name.ToLower().Contains(lowerSearch) ||
                (t.Description != null && t.Description.ToLower().Contains(lowerSearch)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<ToolRegistration>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
            return Enumerable.Empty<ToolRegistration>();

        return await _dbSet
            .Where(t => idList.Contains(t.Id))
            .Include(t => t.McpToolItems)
            .ToListAsync(cancellationToken);
    }
}
