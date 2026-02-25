using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;

namespace CoreSRE.Domain.Interfaces;

public interface IAlertRuleRepository : IRepository<AlertRule>
{
    Task<IEnumerable<AlertRule>> GetActiveRulesAsync(CancellationToken ct = default);
    Task<IEnumerable<AlertRule>> GetByStatusAsync(AlertRuleStatus status, CancellationToken ct = default);
    Task<bool> HasIncidentsAsync(Guid alertRuleId, CancellationToken ct = default);
}
