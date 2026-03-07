using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CoreSRE.Infrastructure.Persistence;

public class PromptSuggestionRepository(AppDbContext context)
    : Repository<PromptOptimizationSuggestion>(context), IPromptSuggestionRepository
{
    public async Task<IEnumerable<PromptOptimizationSuggestion>> GetByAgentIdAsync(
        Guid agentId, CancellationToken ct = default)
        => await context.PromptOptimizationSuggestions
            .Where(s => s.AgentId == agentId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

    public async Task<IEnumerable<PromptOptimizationSuggestion>> GetByStatusAsync(
        SuggestionStatus status, CancellationToken ct = default)
        => await context.PromptOptimizationSuggestions
            .Where(s => s.Status == status)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

    public async Task<IEnumerable<PromptOptimizationSuggestion>> GetFilteredAsync(
        DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var query = context.PromptOptimizationSuggestions.AsQueryable();
        if (from.HasValue) query = query.Where(s => s.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(s => s.CreatedAt <= to.Value);
        return await query.OrderByDescending(s => s.CreatedAt).ToListAsync(ct);
    }
}
