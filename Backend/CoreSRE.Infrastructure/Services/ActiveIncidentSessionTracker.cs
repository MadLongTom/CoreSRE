using System.Collections.Concurrent;
using System.Threading.Channels;
using CoreSRE.Application.Alerts.Interfaces;
using CoreSRE.Application.Incidents.Models;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// Tracks active incident Agent sessions. Supports:
/// - Structured intervention requests (Agent → Human) with pause/resume via TaskCompletionSource
/// - Proactive human messages (Human → Agent spontaneously) via Channel
/// Singleton — shared across scoped DI lifetimes.
/// </summary>
public sealed class ActiveIncidentSessionTracker : IActiveIncidentTracker
{
    /// <summary>Tracks which incidents are currently being processed by agents.</summary>
    private readonly ConcurrentDictionary<Guid, ActiveIncidentInfo> _activeIncidents = new();

    /// <summary>Pending structured intervention requests waiting for human response (Feature C — pause/resume).</summary>
    private readonly ConcurrentDictionary<string, PendingInterventionRequest> _pendingRequests = new();

    /// <summary>All pending request IDs for a given incident.</summary>
    private readonly ConcurrentDictionary<Guid, ConcurrentBag<string>> _incidentRequests = new();

    /// <summary>Channel per incident for proactive human messages (spontaneous, not triggered by agent request).</summary>
    private readonly ConcurrentDictionary<Guid, Channel<ProactiveHumanMessage>> _proactiveChannels = new();

    // ── Registration ──

    public Channel<ProactiveHumanMessage> RegisterActive(Guid incidentId, Guid agentId, string conversationId)
    {
        var channel = Channel.CreateUnbounded<ProactiveHumanMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _activeIncidents[incidentId] = new ActiveIncidentInfo(agentId, conversationId, DateTime.UtcNow);
        _proactiveChannels[incidentId] = channel;
        _incidentRequests[incidentId] = new ConcurrentBag<string>();

        return channel;
    }

    public void UnregisterActive(Guid incidentId)
    {
        _activeIncidents.TryRemove(incidentId, out _);

        if (_proactiveChannels.TryRemove(incidentId, out var channel))
            channel.Writer.TryComplete();

        // Complete all pending requests for this incident
        if (_incidentRequests.TryRemove(incidentId, out var requestIds))
        {
            foreach (var rid in requestIds)
            {
                if (_pendingRequests.TryRemove(rid, out var pending))
                    pending.Completion.TrySetCanceled();
            }
        }
    }

    public bool IsActive(Guid incidentId) => _activeIncidents.ContainsKey(incidentId);

    public ActiveIncidentInfo? GetActiveInfo(Guid incidentId) =>
        _activeIncidents.TryGetValue(incidentId, out var info) ? info : null;

    // ── Feature B: Structured Intervention Requests ──

    /// <summary>
    /// Agent-side: post an intervention request and wait for human response.
    /// This blocks the calling task until a response arrives or cancellation (Feature C — true pause).
    /// </summary>
    public Task<InterventionResponse> RequestInterventionAsync(
        InterventionRequest request,
        CancellationToken cancellationToken = default)
    {
        var pending = new PendingInterventionRequest(request, new TaskCompletionSource<InterventionResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously));

        _pendingRequests[request.RequestId] = pending;

        if (_incidentRequests.TryGetValue(request.IncidentId, out var bag))
            bag.Add(request.RequestId);

        // Register cancellation to unblock if timed out
        cancellationToken.Register(() =>
        {
            if (_pendingRequests.TryRemove(request.RequestId, out var p))
                p.Completion.TrySetCanceled(cancellationToken);
        });

        return pending.Completion.Task;
    }

    /// <summary>
    /// Human-side: respond to a pending intervention request.
    /// Unblocks the agent task that called RequestInterventionAsync (Feature C — resume).
    /// </summary>
    public bool TryRespondToRequest(string requestId, InterventionResponse response)
    {
        if (!_pendingRequests.TryRemove(requestId, out var pending))
            return false;

        return pending.Completion.TrySetResult(response);
    }

    /// <summary>Get a pending request by ID (for display/validation).</summary>
    public InterventionRequest? GetPendingRequest(string requestId) =>
        _pendingRequests.TryGetValue(requestId, out var p) ? p.Request : null;

    /// <summary>Get all pending intervention requests for an incident.</summary>
    public IReadOnlyList<InterventionRequest> GetPendingRequestsForIncident(Guid incidentId)
    {
        if (!_incidentRequests.TryGetValue(incidentId, out var requestIds))
            return [];

        var result = new List<InterventionRequest>();
        foreach (var rid in requestIds)
        {
            if (_pendingRequests.TryGetValue(rid, out var pending))
                result.Add(pending.Request);
        }
        return result;
    }

    // ── Legacy/Proactive: spontaneous human messages (not in response to a request) ──

    public bool TryInjectMessage(Guid incidentId, string content, string? operatorName = null)
    {
        if (!_proactiveChannels.TryGetValue(incidentId, out var channel)) return false;
        return channel.Writer.TryWrite(new ProactiveHumanMessage(content, operatorName, DateTime.UtcNow));
    }

    public ChannelReader<ProactiveHumanMessage>? GetProactiveReader(Guid incidentId) =>
        _proactiveChannels.TryGetValue(incidentId, out var channel) ? channel.Reader : null;

    // ── Internal types ──

    private sealed record PendingInterventionRequest(
        InterventionRequest Request,
        TaskCompletionSource<InterventionResponse> Completion);
}
