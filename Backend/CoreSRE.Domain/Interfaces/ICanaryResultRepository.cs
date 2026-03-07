using CoreSRE.Domain.Entities;

namespace CoreSRE.Domain.Interfaces;

public interface ICanaryResultRepository : IRepository<CanaryResult>
{
    Task<IEnumerable<CanaryResult>> GetByAlertRuleIdAsync(Guid alertRuleId, CancellationToken ct = default);
    Task<IEnumerable<CanaryResult>> GetFilteredAsync(DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
}
