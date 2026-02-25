using CoreSRE.Domain.Enums;

namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 事故时间线条目值对象。存储为 JSONB 数组元素。
/// </summary>
public sealed record IncidentTimelineVO
{
    /// <summary>事件发生时间</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>事件类型</summary>
    public TimelineEventType EventType { get; init; }

    /// <summary>事件摘要</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>详细信息（JSON 或纯文本，可选）</summary>
    public string? Details { get; init; }

    /// <summary>操作 Agent ID（可选）</summary>
    public Guid? ActorAgentId { get; init; }

    /// <summary>附加元数据（可选）</summary>
    public Dictionary<string, string>? Metadata { get; init; }

    /// <summary>创建一条时间线条目</summary>
    public static IncidentTimelineVO Create(
        TimelineEventType eventType,
        string summary,
        string? details = null,
        Guid? actorAgentId = null,
        Dictionary<string, string>? metadata = null) => new()
    {
        Timestamp = DateTime.UtcNow,
        EventType = eventType,
        Summary = summary,
        Details = details,
        ActorAgentId = actorAgentId,
        Metadata = metadata
    };
}
