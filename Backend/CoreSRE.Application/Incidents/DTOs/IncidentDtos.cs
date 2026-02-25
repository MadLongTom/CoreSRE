using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Incidents.DTOs;

/// <summary>
/// Incident 列表摘要 DTO。
/// </summary>
public class IncidentSummaryDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string AlertName { get; set; } = string.Empty;
    public string? AlertFingerprint { get; set; }
    public Guid AlertRuleId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Incident 详情 DTO（含 Timeline）。
/// </summary>
public class IncidentDetailDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string AlertName { get; set; } = string.Empty;
    public string? AlertFingerprint { get; set; }
    public Guid AlertRuleId { get; set; }
    public Guid? ConversationId { get; set; }
    public string? RootCause { get; set; }
    public string? Resolution { get; set; }
    public string? GeneratedSopId { get; set; }
    public Dictionary<string, string>? AlertLabels { get; set; }
    public TimeSpan? TimeToDetect { get; set; }
    public TimeSpan? TimeToResolve { get; set; }
    public List<IncidentTimelineItemDto> Timeline { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Incident Timeline 条目 DTO。
/// </summary>
public class IncidentTimelineItemDto
{
    public string EventType { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? ActorAgentId { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}
