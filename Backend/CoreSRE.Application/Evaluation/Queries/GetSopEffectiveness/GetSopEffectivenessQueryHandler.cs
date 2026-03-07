using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Evaluation.Queries.GetSopEffectiveness;

public class GetSopEffectivenessQueryHandler(
    IIncidentRepository incidentRepository,
    ISkillRegistrationRepository skillRepository)
    : IRequestHandler<GetSopEffectivenessQuery, Result<List<SopEffectivenessDto>>>
{
    public async Task<Result<List<SopEffectivenessDto>>> Handle(
        GetSopEffectivenessQuery request, CancellationToken cancellationToken)
    {
        var from = request.From ?? DateTime.UtcNow.AddDays(-30);
        var to = request.To ?? DateTime.UtcNow;

        var incidents = await incidentRepository.GetFilteredAsync(
            status: null, severity: null, from: from, to: to, ct: cancellationToken);

        var sopIncidents = incidents
            .Where(i => i.Route == IncidentRoute.SopExecution && i.SopId.HasValue)
            .GroupBy(i => i.SopId!.Value)
            .ToList();

        var sopIds = sopIncidents.Select(g => g.Key).ToList();
        var skills = await skillRepository.GetByIdsAsync(sopIds, cancellationToken);
        var skillMap = skills.ToDictionary(s => s.Id, s => s.Name);

        var result = sopIncidents.Select(g =>
        {
            var list = g.ToList();
            var resolved = list.Count(i => i.Status is IncidentStatus.Resolved or IncidentStatus.Closed);
            var avgMs = list
                .Where(i => i.TimeToResolveMs.HasValue)
                .Select(i => (double)i.TimeToResolveMs!.Value)
                .DefaultIfEmpty(0)
                .Average();
            var interventions = list.Count(i =>
                i.Timeline.Any(t => t.EventType == TimelineEventType.HumanIntervention));

            return new SopEffectivenessDto
            {
                SopId = g.Key,
                SopName = skillMap.GetValueOrDefault(g.Key, "Unknown"),
                UsageCount = list.Count,
                SuccessRate = list.Count > 0 ? (double)resolved / list.Count : 0,
                AverageExecutionMs = avgMs,
                HumanInterventionCount = interventions,
            };
        })
        .OrderByDescending(s => s.UsageCount)
        .ToList();

        return Result<List<SopEffectivenessDto>>.Ok(result);
    }
}
