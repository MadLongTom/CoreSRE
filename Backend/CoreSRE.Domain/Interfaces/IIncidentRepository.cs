using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;

namespace CoreSRE.Domain.Interfaces;

public interface IIncidentRepository : IRepository<Incident>
{
    Task<IEnumerable<Incident>> GetByStatusAsync(IncidentStatus status, CancellationToken ct = default);
    Task<IEnumerable<Incident>> GetByAlertRuleIdAsync(Guid alertRuleId, CancellationToken ct = default);
    Task<Incident?> FindActiveByFingerprintAsync(Guid alertRuleId, string fingerprint, int cooldownMinutes, CancellationToken ct = default);
    Task<IEnumerable<Incident>> GetFilteredAsync(IncidentStatus? status = null, IncidentSeverity? severity = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
}
