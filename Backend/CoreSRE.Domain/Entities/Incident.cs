using System.Text.Json;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;

namespace CoreSRE.Domain.Entities;

/// <summary>
/// 故障事件（Incident）。由告警触发，记录完整处置生命周期。
/// </summary>
public class Incident : BaseEntity
{
    /// <summary>事故标题（自动生成或从告警 annotations.summary 提取）</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>事故严重等级</summary>
    public IncidentSeverity Severity { get; private set; } = IncidentSeverity.P3;

    /// <summary>事故状态</summary>
    public IncidentStatus Status { get; private set; } = IncidentStatus.Open;

    /// <summary>触发的路由规则 ID</summary>
    public Guid? AlertRuleId { get; private set; }

    /// <summary>Alertmanager fingerprint（去重键）</summary>
    public string AlertFingerprint { get; private set; } = string.Empty;

    /// <summary>原始告警 JSON（审计用）</summary>
    public JsonDocument? AlertPayload { get; private set; }

    /// <summary>告警标签快照</summary>
    public Dictionary<string, string> AlertLabels { get; private set; } = new();

    /// <summary>处置链路</summary>
    public IncidentRoute Route { get; private set; }

    /// <summary>Agent 对话 ID（链接到 Conversation 实体）</summary>
    public Guid? ConversationId { get; private set; }

    /// <summary>使用的 SOP（SkillRegistration ID）</summary>
    public Guid? SopId { get; private set; }

    /// <summary>根因分析结论</summary>
    public string? RootCause { get; private set; }

    /// <summary>处置结论</summary>
    public string? Resolution { get; private set; }

    /// <summary>根因链路生成的新 SOP（如果有）</summary>
    public Guid? GeneratedSopId { get; private set; }

    /// <summary>开始处理时间</summary>
    public DateTime StartedAt { get; private set; } = DateTime.UtcNow;

    /// <summary>解决时间</summary>
    public DateTime? ResolvedAt { get; private set; }

    /// <summary>MTTD — 告警触发到开始处理（毫秒）</summary>
    public long? TimeToDetectMs { get; private set; }

    /// <summary>MTTR — 开始处理到解决（毫秒）</summary>
    public long? TimeToResolveMs { get; private set; }

    /// <summary>事件时间线</summary>
    public List<IncidentTimelineVO> Timeline { get; private set; } = [];

    private Incident() { } // EF Core

    /// <summary>创建 SOP 执行链路的 Incident</summary>
    public static Incident CreateForSopExecution(
        string title,
        IncidentSeverity severity,
        Guid alertRuleId,
        string alertFingerprint,
        JsonDocument? alertPayload,
        Dictionary<string, string> alertLabels,
        Guid sopId)
    {
        var incident = new Incident
        {
            Title = title,
            Severity = severity,
            Status = IncidentStatus.Open,
            AlertRuleId = alertRuleId,
            AlertFingerprint = alertFingerprint,
            AlertPayload = alertPayload,
            AlertLabels = alertLabels,
            Route = IncidentRoute.SopExecution,
            SopId = sopId,
            StartedAt = DateTime.UtcNow
        };
        incident.AddTimelineEvent(TimelineEventType.AlertReceived, $"告警已接收: {title}");
        return incident;
    }

    /// <summary>创建根因分析链路的 Incident</summary>
    public static Incident CreateForRootCauseAnalysis(
        string title,
        IncidentSeverity severity,
        Guid alertRuleId,
        string alertFingerprint,
        JsonDocument? alertPayload,
        Dictionary<string, string> alertLabels)
    {
        var incident = new Incident
        {
            Title = title,
            Severity = severity,
            Status = IncidentStatus.Open,
            AlertRuleId = alertRuleId,
            AlertFingerprint = alertFingerprint,
            AlertPayload = alertPayload,
            AlertLabels = alertLabels,
            Route = IncidentRoute.RootCauseAnalysis,
            StartedAt = DateTime.UtcNow
        };
        incident.AddTimelineEvent(TimelineEventType.AlertReceived, $"告警已接收: {title}");
        return incident;
    }

    /// <summary>追加时间线事件</summary>
    public void AddTimelineEvent(TimelineEventType eventType, string summary, string? details = null)
    {
        Timeline.Add(IncidentTimelineVO.Create(eventType, summary, details));
    }

    /// <summary>追加时间线事件（完整 VO）</summary>
    public void AddTimelineEvent(IncidentTimelineVO timelineEvent)
    {
        Timeline.Add(timelineEvent);
    }

    /// <summary>关联对话</summary>
    public void SetConversation(Guid conversationId)
    {
        ConversationId = conversationId;
    }

    /// <summary>更新状态（含生命周期校验）</summary>
    public void TransitionTo(IncidentStatus newStatus)
    {
        if (!IsValidTransition(Status, newStatus))
            throw new InvalidOperationException(
                $"Invalid incident status transition: {Status} → {newStatus}");

        var oldStatus = Status;
        Status = newStatus;

        if (newStatus == IncidentStatus.Resolved || newStatus == IncidentStatus.Closed)
        {
            ResolvedAt = DateTime.UtcNow;
            TimeToResolveMs = (long)(ResolvedAt.Value - StartedAt).TotalMilliseconds;
        }

        AddTimelineEvent(TimelineEventType.StatusChanged,
            $"状态变更: {oldStatus} → {newStatus}");
    }

    /// <summary>记录根因分析结论</summary>
    public void SetRootCause(string rootCause)
    {
        RootCause = rootCause;
        AddTimelineEvent(TimelineEventType.RootCauseFound, "根因分析完成", rootCause);
    }

    /// <summary>记录处置结论并标记已解决</summary>
    public void Resolve(string resolution)
    {
        Resolution = resolution;
        TransitionTo(IncidentStatus.Resolved);
        AddTimelineEvent(TimelineEventType.Resolved, "事故已解决", resolution);
    }

    /// <summary>记录生成的 SOP</summary>
    public void SetGeneratedSop(Guid sopId)
    {
        GeneratedSopId = sopId;
        AddTimelineEvent(TimelineEventType.SopGenerated, $"已自动生成 SOP: {sopId}");
    }

    /// <summary>设置 MTTD</summary>
    public void SetTimeToDetect(DateTime alertFiredAt)
    {
        TimeToDetectMs = (long)(StartedAt - alertFiredAt).TotalMilliseconds;
    }

    /// <summary>上报（人工介入）</summary>
    public void Escalate(string reason)
    {
        AddTimelineEvent(TimelineEventType.Escalated, $"已上报: {reason}");
    }

    /// <summary>状态流转合法性校验</summary>
    private static bool IsValidTransition(IncidentStatus from, IncidentStatus to)
    {
        return (from, to) switch
        {
            (IncidentStatus.Open, IncidentStatus.Investigating) => true,
            (IncidentStatus.Open, IncidentStatus.Resolved) => true,       // 快速解决
            (IncidentStatus.Open, IncidentStatus.Closed) => true,         // 误报关闭
            (IncidentStatus.Investigating, IncidentStatus.Mitigated) => true,
            (IncidentStatus.Investigating, IncidentStatus.Resolved) => true,
            (IncidentStatus.Investigating, IncidentStatus.Closed) => true, // 取消
            (IncidentStatus.Mitigated, IncidentStatus.Resolved) => true,
            (IncidentStatus.Mitigated, IncidentStatus.Investigating) => true, // 回退
            (IncidentStatus.Resolved, IncidentStatus.Closed) => true,
            (IncidentStatus.Resolved, IncidentStatus.Investigating) => true,  // 重开
            _ => false
        };
    }
}
