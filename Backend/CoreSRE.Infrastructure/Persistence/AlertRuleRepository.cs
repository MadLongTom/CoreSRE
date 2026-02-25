using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CoreSRE.Infrastructure.Persistence;

public class AlertRuleRepository(AppDbContext context)
    : Repository<AlertRule>(context), IAlertRuleRepository
{
    public async Task<IEnumerable<AlertRule>> GetActiveRulesAsync(CancellationToken ct = default)
        => await context.AlertRules
            .Where(r => r.Status == AlertRuleStatus.Active)
            .ToListAsync(ct);

    public async Task<IEnumerable<AlertRule>> GetByStatusAsync(AlertRuleStatus status, CancellationToken ct = default)
        => await context.AlertRules
            .Where(r => r.Status == status)
            .ToListAsync(ct);

    public async Task<bool> HasIncidentsAsync(Guid alertRuleId, CancellationToken ct = default)
        => await context.Incidents
            .AnyAsync(i => i.AlertRuleId == alertRuleId, ct);
}
