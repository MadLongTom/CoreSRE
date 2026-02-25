namespace CoreSRE.Application.Alerts.Interfaces;

/// <summary>
/// Incident 实时通知器接口（Application 层定义，Infrastructure 层通过 SignalR 实现）。
/// </summary>
public interface IIncidentNotifier
{
    Task IncidentCreatedAsync(
        Guid incidentId, string title, string status, string severity,
        string route, string alertName, Guid alertRuleId, DateTime createdAt,
        CancellationToken ct = default);

    Task IncidentStatusChangedAsync(
        Guid incidentId, string oldStatus, string newStatus, DateTime timestamp,
        CancellationToken ct = default);

    Task TimelineEventAddedAsync(
        Guid incidentId, string eventType, string summary, DateTime timestamp,
        string? actorAgentId = null, Dictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    Task ChatMessageReceivedAsync(
        Guid incidentId, string role, string content,
        string? agentName = null, DateTime? timestamp = null,
        CancellationToken ct = default);

    Task IncidentResolvedAsync(
        Guid incidentId, string? resolution, DateTime resolvedAt,
        CancellationToken ct = default);

    Task IncidentEscalatedAsync(
        Guid incidentId, string reason, DateTime timestamp,
        CancellationToken ct = default);

    Task RcaCompletedAsync(
        Guid incidentId, string rootCause, DateTime timestamp,
        CancellationToken ct = default);

    Task SopGeneratedAsync(
        Guid incidentId, Guid skillId, string sopName, DateTime timestamp,
        CancellationToken ct = default);

    Task IncidentTimeoutAsync(
        Guid incidentId, string reason, DateTime timestamp,
        CancellationToken ct = default);
}
