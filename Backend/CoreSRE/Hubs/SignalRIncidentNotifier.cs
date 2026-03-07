using CoreSRE.Application.Alerts.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace CoreSRE.Hubs;

/// <summary>
/// Incident 实时推送实现 — 通过 SignalR IHubContext 推送事件。
/// </summary>
public class SignalRIncidentNotifier(
    IHubContext<IncidentHub, IIncidentClient> hubContext,
    ILogger<SignalRIncidentNotifier> logger) : IIncidentNotifier
{
    private const string ListGroup = "incident:list";

    public async Task IncidentCreatedAsync(
        Guid incidentId, string title, string status, string severity,
        string route, string alertName, Guid alertRuleId, DateTime createdAt,
        CancellationToken ct = default)
    {
        await SafeSendAsync(ListGroup, () =>
            hubContext.Clients.Group(ListGroup).IncidentCreated(new IncidentCreatedEvent(
                incidentId, title, status, severity, route, alertName, alertRuleId, createdAt)));
    }

    public async Task IncidentStatusChangedAsync(
        Guid incidentId, string oldStatus, string newStatus, DateTime timestamp,
        CancellationToken ct = default)
    {
        // Push to both list and detail groups
        await SafeSendAsync(ListGroup, () =>
            hubContext.Clients.Group(ListGroup).IncidentStatusChanged(
                new IncidentStatusChangedEvent(incidentId, oldStatus, newStatus, timestamp)));

        var detailGroup = $"incident:{incidentId}";
        await SafeSendAsync(detailGroup, () =>
            hubContext.Clients.Group(detailGroup).IncidentStatusChanged(
                new IncidentStatusChangedEvent(incidentId, oldStatus, newStatus, timestamp)));
    }

    public async Task TimelineEventAddedAsync(
        Guid incidentId, string eventType, string summary, DateTime timestamp,
        string? actorAgentId = null, Dictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        var group = $"incident:{incidentId}";
        await SafeSendAsync(group, () =>
            hubContext.Clients.Group(group).TimelineEventAdded(
                new TimelineEventAddedPayload(incidentId, eventType, summary, timestamp, actorAgentId, metadata)));
    }

    public async Task ChatMessageReceivedAsync(
        Guid incidentId, string role, string content,
        string? agentName = null, DateTime? timestamp = null,
        CancellationToken ct = default)
    {
        var group = $"incident:{incidentId}";
        await SafeSendAsync(group, () =>
            hubContext.Clients.Group(group).ChatMessageReceived(
                new ChatMessagePayload(incidentId, role, content, agentName, timestamp)));
    }

    public async Task IncidentResolvedAsync(
        Guid incidentId, string? resolution, DateTime resolvedAt,
        CancellationToken ct = default)
    {
        var group = $"incident:{incidentId}";
        await SafeSendAsync(group, () =>
            hubContext.Clients.Group(group).IncidentResolved(
                new IncidentResolvedEvent(incidentId, resolution, resolvedAt)));
    }

    public async Task IncidentEscalatedAsync(
        Guid incidentId, string reason, DateTime timestamp,
        CancellationToken ct = default)
    {
        var group = $"incident:{incidentId}";
        await SafeSendAsync(group, () =>
            hubContext.Clients.Group(group).IncidentEscalated(
                new IncidentEscalatedEvent(incidentId, reason, timestamp)));
    }

    public async Task RcaCompletedAsync(
        Guid incidentId, string rootCause, DateTime timestamp,
        CancellationToken ct = default)
    {
        var group = $"incident:{incidentId}";
        await SafeSendAsync(group, () =>
            hubContext.Clients.Group(group).RcaCompleted(
                new RcaCompletedEvent(incidentId, rootCause, timestamp)));
    }

    public async Task SopGeneratedAsync(
        Guid incidentId, Guid skillId, string sopName, DateTime timestamp,
        CancellationToken ct = default)
    {
        var group = $"incident:{incidentId}";
        await SafeSendAsync(group, () =>
            hubContext.Clients.Group(group).SopGenerated(
                new SopGeneratedEvent(incidentId, skillId, sopName, timestamp)));
    }

    public async Task IncidentTimeoutAsync(
        Guid incidentId, string reason, DateTime timestamp,
        CancellationToken ct = default)
    {
        var group = $"incident:{incidentId}";
        await SafeSendAsync(group, () =>
            hubContext.Clients.Group(group).IncidentTimeout(
                new IncidentTimeoutEvent(incidentId, reason, timestamp)));
    }

    public async Task AgentProcessingChangedAsync(
        Guid incidentId, bool isProcessing, string? agentName, DateTime timestamp,
        CancellationToken ct = default)
    {
        var group = $"incident:{incidentId}";
        await SafeSendAsync(group, () =>
            hubContext.Clients.Group(group).AgentProcessingChanged(
                new AgentProcessingChangedEvent(incidentId, isProcessing, agentName, timestamp)));
    }

    public async Task HumanInterventionAcknowledgedAsync(
        Guid incidentId, DateTime timestamp,
        CancellationToken ct = default)
    {
        var group = $"incident:{incidentId}";
        await SafeSendAsync(group, () =>
            hubContext.Clients.Group(group).HumanInterventionAcknowledged(
                new HumanInterventionAcknowledgedEvent(incidentId, timestamp)));
    }

    public async Task InterventionRequestReceivedAsync(
        Guid incidentId, string requestId, string type, string prompt, DateTime createdAt,
        string? toolName = null, string? toolCallId = null,
        Dictionary<string, object?>? toolArguments = null, List<string>? choices = null,
        CancellationToken ct = default)
    {
        var group = $"incident:{incidentId}";
        await SafeSendAsync(group, () =>
            hubContext.Clients.Group(group).InterventionRequestReceived(
                new InterventionRequestPayload(incidentId, requestId, type, prompt, createdAt,
                    toolName, toolCallId, toolArguments, choices)));
    }

    public async Task InterventionRequestResolvedAsync(
        Guid incidentId, string requestId, string responseType,
        string? responseContent = null, bool? approved = null,
        string? operatorName = null, DateTime? timestamp = null,
        CancellationToken ct = default)
    {
        var group = $"incident:{incidentId}";
        await SafeSendAsync(group, () =>
            hubContext.Clients.Group(group).InterventionRequestResolved(
                new InterventionRequestResolvedPayload(incidentId, requestId, responseType,
                    responseContent, approved, operatorName, timestamp)));
    }

    private async Task SafeSendAsync(string group, Func<Task> sendAction)
    {
        try
        {
            await sendAction();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SignalR push failed for group {Group}.", group);
        }
    }
}
