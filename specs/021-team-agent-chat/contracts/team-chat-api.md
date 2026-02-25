# API Contract: Team Chat

**Spec**: [../spec.md](../spec.md) | **Date**: 2026-02-17

## Overview

No new HTTP endpoints are added. The existing `POST /api/chat/stream` endpoint is extended to handle Team-type agents transparently. Agent type routing is determined server-side after resolving the agent.

## Modified Endpoint

### POST /api/chat/stream

**Change**: Adds a third handler path for Team agents alongside existing ChatClient and A2A paths.

#### Request (unchanged)

```jsonc
// Content-Type: application/json
{
  "threadId": "conversation-uuid",          // optional, auto-generated if absent
  "runId": "run-uuid",                      // optional, auto-generated if absent
  "messages": [
    {
      "id": "msg-uuid",
      "role": "user",
      "content": "Analyze the production incident"
    }
  ],
  "context": [
    {
      "description": "agentId",
      "value": "team-agent-uuid"            // Can now be a Team agent ID
    }
  ],
  "state": null,
  "forwardedProps": null
}
```

#### Response: SSE Event Stream

**Content-Type**: `text/event-stream`

For Team agents, the response includes all standard AG-UI events plus team-specific extensions:

| Event Type | When Emitted | New Fields |
|---|---|---|
| `RUN_STARTED` | Start of stream | — |
| `TEAM_PROGRESS` | Team: agent transition | `currentAgentId`, `currentAgentName`, `step`, `totalSteps`, `mode` |
| `TEXT_MESSAGE_START` | Agent begins output | `participantAgentId?`, `participantAgentName?` |
| `TEXT_MESSAGE_CONTENT` | Streaming text chunk | `participantAgentId?` |
| `TEXT_MESSAGE_END` | Agent output complete | — |
| `TOOL_CALL_START` | Tool invocation | `participantAgentId?`, `participantAgentName?` |
| `TOOL_CALL_ARGS` | Tool arguments | — |
| `TOOL_CALL_END` | Tool result | — |
| `TEAM_HANDOFF` | Handoffs: agent transfer | `fromAgentId`, `fromAgentName`, `toAgentId`, `toAgentName` |
| `TEAM_LEDGER_UPDATE` | MagneticOne: ledger change | `ledgerType`, `agentName?`, `content` |
| `RUN_FINISHED` | Stream complete | — |
| `RUN_ERROR` | Error occurred | `participantAgentId?`, `participantAgentName?` |

See [sse-events.md](sse-events.md) for detailed event schemas and sequence diagrams.

#### Error Responses

| Status | Condition | Body |
|---|---|---|
| 400 | Missing/invalid agentId | `{ "message": "Missing or invalid agentId in context." }` |
| 400 | Team agent with no active participants | `{ "message": "Team agent 'X' has no resolvable participants." }` |
| 400 | Participant agent not found | `{ "message": "Participant agent 'Y' (id: ...) not found or inactive." }` |
| SSE `RUN_ERROR` | Runtime error during orchestration | `{ "type": "RUN_ERROR", "message": "...", "code": "ParticipantAgentError", "participantAgentName": "..." }` |

## Internal Routing Logic (Backend)

```
POST /api/chat/stream
  ↓
  agentResolver.ResolveAsync(agentId, threadId, ct)
  ↓
  ┌─── AgentType.ChatClient ──→ HandleChatClient{WithHistory|Stateless}Async()
  │
  ├─── AgentType.A2A ──────────→ HandleA2AStreamAsync()
  │
  └─── AgentType.Team ─────────→ HandleTeamStreamAsync()   ← NEW
```

### HandleTeamStreamAsync Flow

```
1. ResolvedAgent contains a Workflow-backed AIAgent (from ITeamOrchestrator)
2. Send RUN_STARTED
3. Iterate over aiAgent.RunStreamingAsync(messages, session, ct):
   a. Check AgentResponseUpdate.AgentId/AgentName for participant attribution
   b. On agent change → emit TEAM_PROGRESS
   c. On text content → emit TEXT_MESSAGE_START/CONTENT/END with participant fields
   d. On tool call → emit TOOL_CALL_* with participant fields
   e. On handoff (detected via tool name pattern) → emit TEAM_HANDOFF
   f. On ledger update (MagneticOne callback) → emit TEAM_LEDGER_UPDATE
4. Send RUN_FINISHED
5. Persist session via sessionStore.SaveSessionAsync()
```

## Existing Endpoints (Unchanged)

| Method | Path | Purpose |
|---|---|---|
| GET | /api/chat/conversations | List conversations (already includes Team agent conversations) |
| POST | /api/chat/conversations | Create conversation (already accepts any agentId including Team) |
| GET | /api/chat/conversations/{id} | Get conversation with messages |
| POST | /api/chat/conversations/{id}/touch | Update conversation metadata |
| DELETE | /api/chat/conversations/{id} | Delete conversation |

These endpoints require no changes because:
- `Conversation` entity is agent-type-agnostic (just stores `AgentId`)
- Messages are projected from `AgentSessionRecord.SessionData` JSONB, which gets the new participant fields automatically via the framework's serialization

## DTOs

### TeamChatEventDto Hierarchy (new)

```csharp
// Backend/CoreSRE.Application/Chat/DTOs/TeamChatEventDto.cs

public abstract record TeamChatEventDto(string EventType);

public sealed record TeamHandoffEventDto(
    string FromAgentId, string FromAgentName,
    string ToAgentId, string ToAgentName
) : TeamChatEventDto("TEAM_HANDOFF");

public sealed record TeamLedgerUpdateEventDto(
    string LedgerType, string? AgentName, string Content
) : TeamChatEventDto("TEAM_LEDGER_UPDATE");

public sealed record TeamProgressEventDto(
    string CurrentAgentId, string CurrentAgentName,
    int? Step, int? TotalSteps, string Mode
) : TeamChatEventDto("TEAM_PROGRESS");
```

### ChatMessageDto (modified)

```csharp
// Existing DTO, adding two optional fields:
public sealed record ChatMessageDto(
    int Index, string Role, string Content,
    ToolCallDto[]? ToolCalls, string? MemoryContext,
    string? ParticipantAgentId,    // NEW
    string? ParticipantAgentName   // NEW
);
```
