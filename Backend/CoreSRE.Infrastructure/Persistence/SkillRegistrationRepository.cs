using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CoreSRE.Infrastructure.Persistence;

/// <summary>
/// SkillRegistration 专用仓储实现
/// </summary>
public class SkillRegistrationRepository : Repository<SkillRegistration>, ISkillRegistrationRepository
{
    public SkillRegistrationRepository(AppDbContext context) : base(context) { }

    public async Task<SkillRegistration?> GetByNameAsync(
        string name, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(s => s.Name == name, cancellationToken);
    }

    public async Task<(IReadOnlyList<SkillRegistration> Items, int TotalCount)> GetPagedAsync(
        SkillScope? scope, SkillStatus? status, string? category, string? search,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _dbSet.AsQueryable();

        if (scope is not null) query = query.Where(s => s.Scope == scope.Value);
        if (status is not null) query = query.Where(s => s.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(s => s.Category == category);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var lower = search.ToLower();
            query = query.Where(s =>
                s.Name.ToLower().Contains(lower) ||
                s.Description.ToLower().Contains(lower));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IEnumerable<SkillRegistration>> GetByIdsAsync(
        IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return [];
        return await _dbSet
            .Where(s => idList.Contains(s.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<SkillRegistration>> GetActiveByIdsAsync(
        IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return [];
        return await _dbSet
            .Where(s => idList.Contains(s.Id) && s.Status == SkillStatus.Active)
            .ToListAsync(cancellationToken);
    }
}
