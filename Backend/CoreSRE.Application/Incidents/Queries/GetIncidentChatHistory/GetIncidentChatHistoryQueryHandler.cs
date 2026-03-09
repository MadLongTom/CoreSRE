using System.Text.Json;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Incidents.Queries.GetIncidentChatHistory;

public class GetIncidentChatHistoryQueryHandler(
    IIncidentRepository incidentRepository,
    IChatHistoryReader chatHistoryReader)
    : IRequestHandler<GetIncidentChatHistoryQuery, Result<List<IncidentChatMessageDto>>>
{
    public async Task<Result<List<IncidentChatMessageDto>>> Handle(
        GetIncidentChatHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var incident = await incidentRepository.GetByIdAsync(request.IncidentId, cancellationToken);
        if (incident is null)
            return Result<List<IncidentChatMessageDto>>.NotFound($"Incident '{request.IncidentId}' not found.");

        if (incident.ConversationId is null || incident.AgentId is null)
            return Result<List<IncidentChatMessageDto>>.Ok([]);

        var agentId = incident.AgentId.Value.ToString();
        var conversationId = incident.ConversationId.Value.ToString();

        // Read session data from AgentSessionRecord using agentId + conversationId composite key
        var sessionData = await chatHistoryReader.GetSessionDataAsync(
            agentId, conversationId, cancellationToken);

        if (sessionData.HasValue)
        {
            var messages = ExtractMessagesFromSession(sessionData.Value);
            if (messages.Count > 0)
                return Result<List<IncidentChatMessageDto>>.Ok(messages);
        }

        // Fallback: extract from timeline events (AgentMessage / HumanIntervention)
        var fallback = new List<IncidentChatMessageDto>();
        foreach (var evt in incident.Timeline)
        {
            var eventType = evt.EventType.ToString();
            if (eventType is "AgentMessage" or "HumanIntervention")
            {
                fallback.Add(new IncidentChatMessageDto
                {
                    Role = eventType == "HumanIntervention" ? "user" : "assistant",
                    Content = evt.Details ?? evt.Summary,
                    AgentName = evt.ActorAgentId?.ToString(),
                    Timestamp = evt.Timestamp
                });
            }
        }

        return Result<List<IncidentChatMessageDto>>.Ok(fallback);
    }

    /// <summary>
    /// Extract chat messages from AgentSessionRecord.SessionData.
    /// Handles both top-level and stateBag-wrapped ChatClientAgentSession formats.
    /// </summary>
    private static List<IncidentChatMessageDto> ExtractMessagesFromSession(JsonElement sessionData)
    {
        // Try top-level chatHistoryProviderState
        if (TryFindMessages(sessionData, out var msgs))
            return ParseMessages(msgs);

        // Try nested under stateBag
        if (sessionData.TryGetProperty("stateBag", out var stateBag)
            && TryFindMessages(stateBag, out var bagMsgs))
            return ParseMessages(bagMsgs);

        return [];
    }

    private static bool TryFindMessages(JsonElement parent, out JsonElement messages)
    {
        // Framework default key
        if (parent.TryGetProperty("chatHistoryProviderState", out var h1)
            && h1.TryGetProperty("messages", out messages))
            return true;

        // Custom provider key
        if (parent.TryGetProperty("PostgresChatHistoryProvider", out var h2)
            && h2.TryGetProperty("messages", out messages))
            return true;

        messages = default;
        return false;
    }

    private static List<IncidentChatMessageDto> ParseMessages(JsonElement messagesArray)
    {
        var result = new List<IncidentChatMessageDto>();

        foreach (var msg in messagesArray.EnumerateArray())
        {
            var role = msg.TryGetProperty("role", out var rp) ? rp.GetString() ?? "user" : "user";

            // Skip system and tool messages
            if (role is "system" or "tool")
                continue;

            var text = ExtractTextContent(msg);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var authorName = msg.TryGetProperty("authorName", out var anp) ? anp.GetString() : null;

            result.Add(new IncidentChatMessageDto
            {
                Role = role,
                Content = text,
                AgentName = authorName,
                Timestamp = DateTime.UtcNow // session data doesn't store per-message timestamps
            });
        }

        return result;
    }

    private static string? ExtractTextContent(JsonElement msg)
    {
        if (!msg.TryGetProperty("contents", out var contents))
            return null;

        var sb = new System.Text.StringBuilder();
        foreach (var c in contents.EnumerateArray())
        {
            // Check "kind" or "$type" to identify text content
            var kind = c.TryGetProperty("kind", out var kp) ? kp.GetString() : null;
            kind ??= c.TryGetProperty("$type", out var tp) ? tp.GetString() : null;

            if (kind is "text" or "TextContent" or null)
            {
                if (c.TryGetProperty("text", out var textProp))
                    sb.Append(textProp.GetString());
            }
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }
}
