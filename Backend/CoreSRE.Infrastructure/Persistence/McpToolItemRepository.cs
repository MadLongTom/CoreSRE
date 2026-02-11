using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CoreSRE.Infrastructure.Persistence;

/// <summary>
/// McpToolItem 专用仓储实现
/// </summary>
public class McpToolItemRepository : Repository<McpToolItem>, IMcpToolItemRepository
{
    public McpToolItemRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<McpToolItem>> GetByToolRegistrationIdAsync(
        Guid toolRegistrationId,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(m => m.ToolRegistrationId == toolRegistrationId)
            .OrderBy(m => m.ToolName)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteByToolRegistrationIdAsync(
        Guid toolRegistrationId,
        CancellationToken cancellationToken = default)
    {
        var items = await _dbSet
            .Where(m => m.ToolRegistrationId == toolRegistrationId)
            .ToListAsync(cancellationToken);

        if (items.Count > 0)
        {
            _dbSet.RemoveRange(items);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
