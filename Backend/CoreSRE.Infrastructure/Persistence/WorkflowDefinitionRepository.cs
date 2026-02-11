using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CoreSRE.Infrastructure.Persistence;

/// <summary>
/// WorkflowDefinition 专用仓储实现
/// </summary>
public class WorkflowDefinitionRepository : Repository<WorkflowDefinition>, IWorkflowDefinitionRepository
{
    public WorkflowDefinitionRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc/>
    public async Task<WorkflowDefinition?> GetByNameAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(w => w.Name == name, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<WorkflowDefinition>> GetByStatusAsync(
        WorkflowStatus status,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(w => w.Status == status)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsWithNameAsync(
        string name,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet.Where(w => w.Name == name);

        if (excludeId.HasValue)
            query = query.Where(w => w.Id != excludeId.Value);

        return await query.AnyAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> IsReferencedByAgentAsync(
        Guid workflowId,
        CancellationToken cancellationToken = default)
    {
        return await _context.AgentRegistrations
            .AnyAsync(a => a.WorkflowRef == workflowId, cancellationToken);
    }
}
