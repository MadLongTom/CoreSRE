using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Evaluation.Queries.GetDashboard;

public class GetEvaluationDashboardQueryHandler(
    IIncidentRepository incidentRepository,
    IAlertRuleRepository alertRuleRepository)
    : IRequestHandler<GetEvaluationDashboardQuery, Result<EvaluationDashboardDto>>
{
    public async Task<Result<EvaluationDashboardDto>> Handle(
        GetEvaluationDashboardQuery request, CancellationToken cancellationToken)
    {
        var from = request.From ?? DateTime.UtcNow.AddDays(-30);
        var to = request.To ?? DateTime.UtcNow;

        var incidents = await incidentRepository.GetFilteredAsync(
            status: null, severity: null, from: from, to: to, ct: cancellationToken);
        var incidentList = incidents.ToList();
        var total = incidentList.Count;

        if (total == 0)
        {
            return Result<EvaluationDashboardDto>.Ok(new EvaluationDashboardDto());
        }

        // 自动修复率：Chain A (SopExecution) + Resolved
        var sopResolved = incidentList.Count(i =>
            i.Route == IncidentRoute.SopExecution &&
            i.Status is IncidentStatus.Resolved or IncidentStatus.Closed);
        var autoResolveRate = (double)sopResolved / total;

        // 平均 MTTR
        var resolvedIncidents = incidentList.Where(i => i.TimeToResolveMs.HasValue).ToList();
        var avgMttr = resolvedIncidents.Count > 0
            ? resolvedIncidents.Average(i => i.TimeToResolveMs!.Value)
            : 0;

        // 按 Severity 分组 MTTR
        var mttrBySeverity = resolvedIncidents
            .GroupBy(i => i.Severity.ToString())
            .ToDictionary(
                g => g.Key,
                g => g.Average(i => i.TimeToResolveMs!.Value));

        // SOP 覆盖率
        var allRules = await alertRuleRepository.GetActiveRulesAsync(cancellationToken);
        var ruleList = allRules.ToList();
        var sopCoverage = ruleList.Count > 0
            ? (double)ruleList.Count(r => r.SopId.HasValue) / ruleList.Count
            : 0;

        // 人工介入率
        var humanInterventionCount = incidentList.Count(i =>
            i.Timeline.Any(t => t.EventType == TimelineEventType.HumanIntervention));
        var humanInterventionRate = (double)humanInterventionCount / total;

        // 超时率
        var timeoutCount = incidentList.Count(i =>
            i.Timeline.Any(t => t.EventType == TimelineEventType.Timeout));
        var timeoutRate = (double)timeoutCount / total;

        // RCA 准确率
        var annotated = incidentList.Where(i => i.PostMortem is not null).ToList();
        var scorable = annotated.Where(i => i.PostMortem!.RcaAccuracy != RcaAccuracyRating.NotApplicable).ToList();
        double? rcaAccuracy = scorable.Count > 0
            ? scorable.Average(i => i.PostMortem!.RcaAccuracy switch
            {
                RcaAccuracyRating.Accurate => 1.0,
                RcaAccuracyRating.PartiallyAccurate => 0.5,
                _ => 0.0,
            })
            : null;

        return Result<EvaluationDashboardDto>.Ok(new EvaluationDashboardDto
        {
            TotalIncidents = total,
            AutoResolveRate = autoResolveRate,
            AverageMttrMs = avgMttr,
            MttrBySeverity = mttrBySeverity,
            SopCoverageRate = sopCoverage,
            HumanInterventionRate = humanInterventionRate,
            TimeoutRate = timeoutRate,
            RcaAccuracyRate = rcaAccuracy,
            AnnotatedIncidentCount = annotated.Count,
        });
    }
}
