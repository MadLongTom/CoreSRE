using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CoreSRE.Infrastructure.Persistence;

/// <summary>
/// WorkflowExecution 专用仓储实现
/// </summary>
public class WorkflowExecutionRepository : Repository<WorkflowExecution>, IWorkflowExecutionRepository
{
    public WorkflowExecutionRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc/>
    public async Task<IEnumerable<WorkflowExecution>> GetByWorkflowIdAsync(
        Guid workflowDefinitionId,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(e => e.WorkflowDefinitionId == workflowDefinitionId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<WorkflowExecution>> GetByStatusAsync(
        ExecutionStatus status,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(e => e.Status == status)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
