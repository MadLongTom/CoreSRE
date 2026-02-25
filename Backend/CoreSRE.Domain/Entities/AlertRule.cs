using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;

namespace CoreSRE.Domain.Entities;

/// <summary>
/// 告警路由规则。定义如何将 Alertmanager 告警匹配到处置链路。
/// </summary>
public class AlertRule : BaseEntity
{
    /// <summary>规则名称，如 "HighErrorRate-OrderService"</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>规则描述</summary>
    public string? Description { get; private set; }

    /// <summary>规则状态</summary>
    public AlertRuleStatus Status { get; private set; } = AlertRuleStatus.Active;

    /// <summary>标签匹配条件列表（与 Alertmanager route 对齐）</summary>
    public List<AlertMatcherVO> Matchers { get; private set; } = [];

    /// <summary>事故严重等级</summary>
    public IncidentSeverity Severity { get; private set; } = IncidentSeverity.P3;

    /// <summary>关联的 SOP（SkillRegistration ID, null = 走根因分析链路）</summary>
    public Guid? SopId { get; private set; }

    /// <summary>SOP 链路：执行 SOP 的 ChatClient Agent ID</summary>
    public Guid? ResponderAgentId { get; private set; }

    /// <summary>根因链路：负责根因分析的 Team Agent ID</summary>
    public Guid? TeamAgentId { get; private set; }

    /// <summary>根因链路：负责生成 SOP 的总结 Agent ID</summary>
    public Guid? SummarizerAgentId { get; private set; }

    /// <summary>通知渠道标识列表（预留：Slack/Teams/Email）</summary>
    public List<string> NotificationChannels { get; private set; } = [];

    /// <summary>冷却时间（分钟），同指纹告警不重复触发</summary>
    public int CooldownMinutes { get; private set; } = 15;

    /// <summary>自定义标签</summary>
    public Dictionary<string, string>? Tags { get; private set; }

    private AlertRule() { } // EF Core

    /// <summary>创建告警路由规则</summary>
    public static AlertRule Create(
        string name,
        List<AlertMatcherVO> matchers,
        IncidentSeverity severity,
        string? description = null,
        Guid? sopId = null,
        Guid? responderAgentId = null,
        Guid? teamAgentId = null,
        Guid? summarizerAgentId = null,
        List<string>? notificationChannels = null,
        int cooldownMinutes = 15,
        Dictionary<string, string>? tags = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("AlertRule name is required.", nameof(name));
        if (matchers is null || matchers.Count == 0)
            throw new ArgumentException("At least one matcher is required.", nameof(matchers));

        // 互斥校验：有 SOP 走链路 A，无 SOP 必须配 TeamAgent 走链路 B
        if (sopId.HasValue && teamAgentId.HasValue)
            throw new ArgumentException("SopId and TeamAgentId are mutually exclusive.");

        return new AlertRule
        {
            Name = name,
            Description = description,
            Matchers = matchers,
            Severity = severity,
            SopId = sopId,
            ResponderAgentId = responderAgentId,
            TeamAgentId = teamAgentId,
            SummarizerAgentId = summarizerAgentId,
            NotificationChannels = notificationChannels ?? [],
            CooldownMinutes = cooldownMinutes,
            Tags = tags
        };
    }

    /// <summary>更新规则</summary>
    public void Update(
        string? name = null,
        string? description = null,
        List<AlertMatcherVO>? matchers = null,
        IncidentSeverity? severity = null,
        Guid? sopId = null,
        Guid? responderAgentId = null,
        Guid? teamAgentId = null,
        Guid? summarizerAgentId = null,
        List<string>? notificationChannels = null,
        int? cooldownMinutes = null,
        Dictionary<string, string>? tags = null)
    {
        if (name is not null) Name = name;
        if (description is not null) Description = description;
        if (matchers is not null) Matchers = matchers;
        if (severity.HasValue) Severity = severity.Value;
        if (sopId.HasValue) SopId = sopId;
        if (responderAgentId.HasValue) ResponderAgentId = responderAgentId;
        if (teamAgentId.HasValue) TeamAgentId = teamAgentId;
        if (summarizerAgentId.HasValue) SummarizerAgentId = summarizerAgentId;
        if (notificationChannels is not null) NotificationChannels = notificationChannels;
        if (cooldownMinutes.HasValue) CooldownMinutes = cooldownMinutes.Value;
        if (tags is not null) Tags = tags;
    }

    /// <summary>设置 SOP 绑定（链路 C 自动生成后调用）</summary>
    public void BindSop(Guid sopId, Guid responderAgentId)
    {
        SopId = sopId;
        ResponderAgentId = responderAgentId;
    }

    /// <summary>激活/停用规则</summary>
    public void SetStatus(AlertRuleStatus status) => Status = status;

    /// <summary>清除 SOP 绑定（重新走链路 B）</summary>
    public void ClearSopBinding()
    {
        SopId = null;
        ResponderAgentId = null;
    }

    /// <summary>
    /// 判断给定告警标签是否匹配本规则的所有 Matchers（AND 逻辑）。
    /// </summary>
    public bool IsMatch(Dictionary<string, string> alertLabels)
    {
        return Matchers.All(m =>
        {
            alertLabels.TryGetValue(m.Label, out var actual);
            return m.IsMatch(actual);
        });
    }
}
