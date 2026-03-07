using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Incidents.Queries.GetIncidentChatHistory;

public class GetIncidentChatHistoryQueryHandler(
    IIncidentRepository incidentRepository)
    : IRequestHandler<GetIncidentChatHistoryQuery, Result<List<IncidentChatMessageDto>>>
{
    public async Task<Result<List<IncidentChatMessageDto>>> Handle(
        GetIncidentChatHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var incident = await incidentRepository.GetByIdAsync(request.IncidentId, cancellationToken);
        if (incident is null)
            return Result<List<IncidentChatMessageDto>>.NotFound($"Incident '{request.IncidentId}' not found.");

        if (incident.ConversationId is null)
            return Result<List<IncidentChatMessageDto>>.Ok([]);

        // Read session data from AgentSessionRecord
        // The agent ID could be the SOP agent or team agent — we need to find it from the alert rule
        // For now, just try to read by conversation ID pattern match
        var conversationId = incident.ConversationId.Value.ToString();

        // Try to find session data — iterate over potential agent IDs
        // The AgentSessionRecord uses agentId + conversationId as composite key
        // We'll extract messages from timeline events as a reliable fallback
        var messages = new List<IncidentChatMessageDto>();

        // Extract chat messages from timeline events (this is always available)
        foreach (var evt in incident.Timeline)
        {
            var eventType = evt.EventType.ToString();
            if (eventType is "AgentMessage" or "HumanIntervention")
            {
                messages.Add(new IncidentChatMessageDto
                {
                    Role = eventType == "HumanIntervention" ? "user" : "assistant",
                    Content = evt.Details ?? evt.Summary,
                    AgentName = evt.ActorAgentId?.ToString(),
                    Timestamp = evt.Timestamp
                });
            }
        }

        return Result<List<IncidentChatMessageDto>>.Ok(messages);
    }
}
