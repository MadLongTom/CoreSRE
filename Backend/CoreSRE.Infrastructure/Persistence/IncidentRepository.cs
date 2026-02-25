using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CoreSRE.Infrastructure.Persistence;

public class IncidentRepository(AppDbContext context)
    : Repository<Incident>(context), IIncidentRepository
{
    public async Task<IEnumerable<Incident>> GetByStatusAsync(IncidentStatus status, CancellationToken ct = default)
        => await context.Incidents
            .Where(i => i.Status == status)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

    public async Task<IEnumerable<Incident>> GetByAlertRuleIdAsync(Guid alertRuleId, CancellationToken ct = default)
        => await context.Incidents
            .Where(i => i.AlertRuleId == alertRuleId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

    public async Task<Incident?> FindActiveByFingerprintAsync(
        Guid alertRuleId,
        string fingerprint,
        int cooldownMinutes,
        CancellationToken ct = default)
    {
        var query = context.Incidents
            .Where(i => i.AlertRuleId == alertRuleId
                        && i.AlertFingerprint == fingerprint
                        && i.Status != IncidentStatus.Closed);

        // cooldownMinutes <= 0 表示不限时间窗口（查找任意未关闭 Incident）
        if (cooldownMinutes > 0)
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-cooldownMinutes);
            query = query.Where(i => i.CreatedAt >= cutoff);
        }

        return await query
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IEnumerable<Incident>> GetFilteredAsync(
        IncidentStatus? status = null,
        IncidentSeverity? severity = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default)
    {
        var query = context.Incidents.AsQueryable();

        if (status.HasValue)
            query = query.Where(i => i.Status == status.Value);
        if (severity.HasValue)
            query = query.Where(i => i.Severity == severity.Value);
        if (from.HasValue)
            query = query.Where(i => i.CreatedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(i => i.CreatedAt <= to.Value);

        return await query
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);
    }
}
