using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CoreSRE.Infrastructure.Persistence;

/// <summary>
/// LlmProvider 仓储实现
/// </summary>
public class LlmProviderRepository : Repository<LlmProvider>, ILlmProviderRepository
{
    public LlmProviderRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc/>
    public async Task<LlmProvider?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(p => p.Name == name, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsWithNameAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AnyAsync(p => p.Name == name && (excludeId == null || p.Id != excludeId), cancellationToken);
    }
}
