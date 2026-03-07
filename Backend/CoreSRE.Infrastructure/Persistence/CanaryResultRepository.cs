using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CoreSRE.Infrastructure.Persistence;

public class CanaryResultRepository(AppDbContext context)
    : Repository<CanaryResult>(context), ICanaryResultRepository
{
    public async Task<IEnumerable<CanaryResult>> GetByAlertRuleIdAsync(
        Guid alertRuleId, CancellationToken ct = default)
        => await context.CanaryResults
            .Where(c => c.AlertRuleId == alertRuleId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

    public async Task<IEnumerable<CanaryResult>> GetFilteredAsync(
        DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var query = context.CanaryResults.AsQueryable();
        if (from.HasValue) query = query.Where(c => c.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(c => c.CreatedAt <= to.Value);
        return await query.OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
    }
}
