using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using MediatR;

namespace CoreSRE.Application.Alerts.Queries.GetAlertRuleHealth;

public class GetAlertRuleHealthQueryHandler(
    IAlertRuleRepository alertRuleRepo,
    IIncidentRepository incidentRepo,
    ISkillRegistrationRepository skillRepo)
    : IRequestHandler<GetAlertRuleHealthQuery, Result<AlertRuleHealthVO>>
{
    public async Task<Result<AlertRuleHealthVO>> Handle(
        GetAlertRuleHealthQuery request, CancellationToken cancellationToken)
    {
        var alertRule = await alertRuleRepo.GetByIdAsync(request.AlertRuleId, cancellationToken);
        if (alertRule is null)
            return Result<AlertRuleHealthVO>.NotFound($"AlertRule '{request.AlertRuleId}' not found.");

        var since = DateTime.UtcNow.AddDays(-30);
        var incidents = await incidentRepo.GetFilteredAsync(
            from: since, ct: cancellationToken);

        var ruleIncidents = incidents.Where(i => i.AlertRuleId == alertRule.Id).ToList();

        var factors = new List<HealthFactor>();
        var recommendations = new List<string>();
        var totalScore = 0;

        // Factor 1: SOP 覆盖（+20 分）
        var hasSop = alertRule.SopId.HasValue;
        var sopEarned = hasSop ? 20 : 0;
        factors.Add(new HealthFactor("SOP覆盖", 20, sopEarned,
            hasSop ? "已绑定 SOP" : "未绑定 SOP"));
        if (!hasSop)
            recommendations.Add("该规则无 SOP 覆盖，建议生成 SOP 以提升自动化处置率。");
        totalScore += sopEarned;

        // Factor 2: SOP 成功率（+30 分）
        var sopSuccessEarned = 0;
        if (alertRule.SopId.HasValue)
        {
            var sop = await skillRepo.GetByIdAsync(alertRule.SopId.Value, cancellationToken);
            var rate = sop?.ExecutionStats?.RollingSuccessRate ?? 0.0;
            sopSuccessEarned = rate >= 0.8 ? 30 : (int)(rate * 30);
            factors.Add(new HealthFactor("SOP成功率", 30, sopSuccessEarned,
                $"滚动成功率: {rate:P0}"));
            if (rate < 0.6)
                recommendations.Add($"SOP 成功率仅 {rate:P0}，建议审查或重新生成 SOP。");
        }
        else
        {
            factors.Add(new HealthFactor("SOP成功率", 30, 0, "无 SOP，不适用"));
        }
        totalScore += sopSuccessEarned;

        // Factor 3: 平均 MTTR（+20 分）
        var resolvedIncidents = ruleIncidents
            .Where(i => i.TimeToResolveMs.HasValue)
            .ToList();
        var avgMttr = resolvedIncidents.Count > 0
            ? resolvedIncidents.Average(i => i.TimeToResolveMs!.Value)
            : 0;
        // 基线: 30 分钟 = 1800000ms，低于基线得满分
        var mttrEarned = avgMttr <= 0 ? 10
            : avgMttr <= 1_800_000 ? 20
            : avgMttr <= 3_600_000 ? 10 : 0;
        factors.Add(new HealthFactor("平均MTTR", 20, mttrEarned,
            resolvedIncidents.Count > 0 ? $"近30天均值: {avgMttr / 60000:F1} 分钟" : "无解决记录"));
        if (avgMttr > 3_600_000)
            recommendations.Add("MTTR 超过 60 分钟，建议优化处置流程或分配更强的 Agent。");
        totalScore += mttrEarned;

        // Factor 4: 人工介入率（+30 分，< 10% 得满分）
        var humanInterventionCount = ruleIncidents
            .Count(i => i.Timeline.Any(t => t.EventType == TimelineEventType.HumanIntervention));
        var interventionRate = ruleIncidents.Count > 0
            ? (double)humanInterventionCount / ruleIncidents.Count
            : 0;
        var interventionEarned = interventionRate <= 0.10 ? 30
            : interventionRate <= 0.30 ? 20
            : interventionRate <= 0.50 ? 10 : 0;
        factors.Add(new HealthFactor("人工介入率", 30, interventionEarned,
            ruleIncidents.Count > 0 ? $"{interventionRate:P0} ({humanInterventionCount}/{ruleIncidents.Count})" : "无 Incident 记录"));
        if (interventionRate > 0.30)
            recommendations.Add($"人工介入率 {interventionRate:P0} 偏高，建议审查 SOP 或 Agent 配置。");
        totalScore += interventionEarned;

        // 触发频率合理性检查（附加建议，不影响评分）
        if (ruleIncidents.Count > 200)
            recommendations.Add($"近30天触发 {ruleIncidents.Count} 次，频率过高，建议调整 Matcher 条件或告警阈值以减少误报。");

        var health = AlertRuleHealthVO.Create(totalScore, factors, recommendations);

        // 持久化评分
        alertRule.SetHealthScore(health);
        await alertRuleRepo.UpdateAsync(alertRule, cancellationToken);

        return Result<AlertRuleHealthVO>.Ok(health);
    }
}
