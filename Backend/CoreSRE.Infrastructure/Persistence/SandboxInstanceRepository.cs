using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CoreSRE.Infrastructure.Persistence;

/// <summary>
/// SandboxInstance 专用仓储实现
/// </summary>
public class SandboxInstanceRepository : Repository<SandboxInstance>, ISandboxInstanceRepository
{
    public SandboxInstanceRepository(AppDbContext context) : base(context) { }

    public async Task<IEnumerable<SandboxInstance>> GetByStatusAsync(
        SandboxStatus status, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(s => s.Status == status)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<SandboxInstance>> GetByAgentIdAsync(
        Guid agentId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(s => s.AgentId == agentId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<SandboxInstance> Items, int TotalCount)> GetPagedAsync(
        SandboxStatus? status, Guid? agentId, string? search,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _dbSet.AsQueryable();

        if (status is not null) query = query.Where(s => s.Status == status.Value);
        if (agentId is not null) query = query.Where(s => s.AgentId == agentId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var lower = search.ToLower();
            query = query.Where(s => s.Name.ToLower().Contains(lower));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IEnumerable<SandboxInstance>> GetRunningWithAutoStopAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(s => s.Status == SandboxStatus.Running && s.AutoStopMinutes > 0)
            .ToListAsync(cancellationToken);
    }
}
