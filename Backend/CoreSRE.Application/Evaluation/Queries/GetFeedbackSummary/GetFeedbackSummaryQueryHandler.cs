using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Evaluation.Queries.GetFeedbackSummary;

public class GetFeedbackSummaryQueryHandler(
    IIncidentRepository incidentRepo,
    ICanaryResultRepository canaryResultRepo,
    IPromptSuggestionRepository promptSuggestionRepo,
    ISkillRegistrationRepository skillRepo)
    : IRequestHandler<GetFeedbackSummaryQuery, Result<FeedbackSummaryDto>>
{
    public async Task<Result<FeedbackSummaryDto>> Handle(
        GetFeedbackSummaryQuery request, CancellationToken cancellationToken)
    {
        var from = request.From ?? DateTime.UtcNow.AddDays(-30);
        var to = request.To ?? DateTime.UtcNow;

        var incidents = (await incidentRepo.GetFilteredAsync(
            from: from, to: to, ct: cancellationToken)).ToList();

        var totalIncidents = incidents.Count;
        var fallbackCount = incidents.Count(i => i.Route == IncidentRoute.FallbackRca);
        var fallbackRate = totalIncidents > 0
            ? (double)fallbackCount / totalIncidents : 0;

        // 金丝雀结果统计
        var canaryResults = (await canaryResultRepo.GetFilteredAsync(
            from: from, to: to, ct: cancellationToken)).ToList();
        var canaryConsistencyRate = canaryResults.Count > 0
            ? (double)canaryResults.Count(c => c.IsConsistent) / canaryResults.Count : 0;

        // Prompt 优化建议统计
        var suggestions = (await promptSuggestionRepo.GetFilteredAsync(
            from: from, to: to, ct: cancellationToken)).ToList();
        var suggestionsApplied = suggestions.Count(s => s.Status == SuggestionStatus.Applied);
        var adoptionRate = suggestions.Count > 0
            ? (double)suggestionsApplied / suggestions.Count : 0;

        // SOP 解绑 & 降级统计
        var sopsAutoDisabled = incidents
            .Count(i => i.Timeline.Any(t => t.EventType == TimelineEventType.SopAutoDisabled));
        var allSkills = await skillRepo.GetAllAsync(cancellationToken);
        var sopsDegraded = allSkills.Count(s => s.Status == SkillStatus.Degraded);

        return Result<FeedbackSummaryDto>.Ok(new FeedbackSummaryDto(
            TotalIncidents: totalIncidents,
            FallbackCount: fallbackCount,
            FallbackRate: fallbackRate,
            CanaryResultCount: canaryResults.Count,
            CanaryConsistencyRate: canaryConsistencyRate,
            PromptSuggestionsTotal: suggestions.Count,
            PromptSuggestionsApplied: suggestionsApplied,
            PromptAdoptionRate: adoptionRate,
            SopsAutoDisabledCount: sopsAutoDisabled,
            SopsDegradedCount: sopsDegraded));
    }
}
