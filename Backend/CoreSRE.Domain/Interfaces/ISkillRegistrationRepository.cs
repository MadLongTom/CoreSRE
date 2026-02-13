using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;

namespace CoreSRE.Domain.Interfaces;

/// <summary>
/// SkillRegistration 专用仓储接口
/// </summary>
public interface ISkillRegistrationRepository : IRepository<SkillRegistration>
{
    Task<SkillRegistration?> GetByNameAsync(
        string name, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<SkillRegistration> Items, int TotalCount)> GetPagedAsync(
        SkillScope? scope, SkillStatus? status, string? category, string? search,
        int page, int pageSize, CancellationToken cancellationToken = default);

    Task<IEnumerable<SkillRegistration>> GetByIdsAsync(
        IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    Task<IEnumerable<SkillRegistration>> GetActiveByIdsAsync(
        IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
}
