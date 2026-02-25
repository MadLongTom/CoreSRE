using CoreSRE.Application.Alerts.DTOs;
using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Alerts.Queries.MatchAlertRules;

/// <summary>
/// 对给定告警列表，遍历所有 Active AlertRule 进行标签匹配。
/// 返回 (AlertVO, 匹配到的 AlertRuleId) 的列表。
/// </summary>
public record MatchAlertRulesQuery(List<AlertVO> Alerts)
    : IRequest<Result<List<AlertRuleMatch>>>;

/// <summary>
/// 告警与匹配规则的关联结果。
/// </summary>
public class AlertRuleMatch
{
    public AlertVO Alert { get; set; } = null!;
    public Guid AlertRuleId { get; set; }
    public Guid? SopId { get; set; }
    public Guid? ResponderAgentId { get; set; }
    public Guid? TeamAgentId { get; set; }
    public Guid? SummarizerAgentId { get; set; }
    public Domain.Enums.IncidentSeverity Severity { get; set; }
    public int CooldownMinutes { get; set; }
    public List<string> NotificationChannels { get; set; } = [];
}
