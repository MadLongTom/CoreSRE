using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CoreSRE.Infrastructure.Persistence;

/// <summary>
/// Conversation 专用仓储实现
/// </summary>
public class ConversationRepository : Repository<Conversation>, IConversationRepository
{
    public ConversationRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc/>
    public async Task<IEnumerable<Conversation>> GetAllOrderedByUpdatedAtAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
