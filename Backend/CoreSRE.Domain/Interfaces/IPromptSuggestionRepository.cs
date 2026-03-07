using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;

namespace CoreSRE.Domain.Interfaces;

public interface IPromptSuggestionRepository : IRepository<PromptOptimizationSuggestion>
{
    Task<IEnumerable<PromptOptimizationSuggestion>> GetByAgentIdAsync(Guid agentId, CancellationToken ct = default);
    Task<IEnumerable<PromptOptimizationSuggestion>> GetByStatusAsync(SuggestionStatus status, CancellationToken ct = default);
    Task<IEnumerable<PromptOptimizationSuggestion>> GetFilteredAsync(DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
}
