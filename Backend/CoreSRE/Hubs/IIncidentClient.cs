namespace CoreSRE.Hubs;

/// <summary>
/// IncidentHub 的 Server→Client 事件接口。
/// </summary>
public interface IIncidentClient
{
    // ── 列表级事件（JoinIncidentList 组） ──

    /// <summary>新 Incident 被创建</summary>
    Task IncidentCreated(IncidentCreatedEvent evt);

    /// <summary>Incident 状态发生变更</summary>
    Task IncidentStatusChanged(IncidentStatusChangedEvent evt);

    // ── 详情级事件（JoinIncident 组） ──

    /// <summary>时间线新增条目</summary>
    Task TimelineEventAdded(TimelineEventAddedPayload evt);

    /// <summary>Agent 对话产生新消息（逐段 streaming）</summary>
    Task ChatMessageReceived(ChatMessagePayload evt);

    /// <summary>Incident 已解决</summary>
    Task IncidentResolved(IncidentResolvedEvent evt);

    /// <summary>Incident 上报/升级</summary>
    Task IncidentEscalated(IncidentEscalatedEvent evt);

    /// <summary>RCA 根因分析完成</summary>
    Task RcaCompleted(RcaCompletedEvent evt);

    /// <summary>SOP 自动生成完成</summary>
    Task SopGenerated(SopGeneratedEvent evt);

    /// <summary>处置超时，需人工介入</summary>
    Task IncidentTimeout(IncidentTimeoutEvent evt);

    /// <summary>Agent 正在处理中状态变更（开始/结束）</summary>
    Task AgentProcessingChanged(AgentProcessingChangedEvent evt);

    /// <summary>人工消息已被 Agent 接收并继续执行</summary>
    Task HumanInterventionAcknowledged(HumanInterventionAcknowledgedEvent evt);

    /// <summary>Agent 发起结构化干预请求（工具审批 / 文本输入 / 选择）</summary>
    Task InterventionRequestReceived(InterventionRequestPayload evt);

    /// <summary>干预请求被人工回复（通知前端清除待处理请求）</summary>
    Task InterventionRequestResolved(InterventionRequestResolvedPayload evt);
}

// ── Event DTOs ──

public record IncidentCreatedEvent(
    Guid IncidentId,
    string Title,
    string Status,
    string Severity,
    string Route,
    string AlertName,
    Guid AlertRuleId,
    DateTime CreatedAt);

public record IncidentStatusChangedEvent(
    Guid IncidentId,
    string OldStatus,
    string NewStatus,
    DateTime Timestamp);

public record TimelineEventAddedPayload(
    Guid IncidentId,
    string EventType,
    string Summary,
    DateTime Timestamp,
    string? ActorAgentId = null,
    Dictionary<string, string>? Metadata = null);

public record ChatMessagePayload(
    Guid IncidentId,
    string Role,
    string Content,
    string? AgentName = null,
    DateTime? Timestamp = null);

public record IncidentResolvedEvent(
    Guid IncidentId,
    string? Resolution,
    DateTime ResolvedAt);

public record IncidentEscalatedEvent(
    Guid IncidentId,
    string Reason,
    DateTime Timestamp);

public record RcaCompletedEvent(
    Guid IncidentId,
    string RootCause,
    DateTime Timestamp);

public record SopGeneratedEvent(
    Guid IncidentId,
    Guid SkillId,
    string SopName,
    DateTime Timestamp);

public record IncidentTimeoutEvent(
    Guid IncidentId,
    string Reason,
    DateTime Timestamp);

public record AgentProcessingChangedEvent(
    Guid IncidentId,
    bool IsProcessing,
    string? AgentName,
    DateTime Timestamp);

public record HumanInterventionAcknowledgedEvent(
    Guid IncidentId,
    DateTime Timestamp);

public record InterventionRequestPayload(
    Guid IncidentId,
    string RequestId,
    string Type,
    string Prompt,
    DateTime CreatedAt,
    string? ToolName = null,
    string? ToolCallId = null,
    Dictionary<string, object?>? ToolArguments = null,
    List<string>? Choices = null);

public record InterventionRequestResolvedPayload(
    Guid IncidentId,
    string RequestId,
    string ResponseType,
    string? ResponseContent = null,
    bool? Approved = null,
    string? OperatorName = null,
    DateTime? Timestamp = null);
