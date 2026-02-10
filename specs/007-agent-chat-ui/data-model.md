# Data Model: 007 Agent Chat UI

**Created**: 2026-02-10  
**Feature**: [spec.md](spec.md)

## Design Decision: Reuse AgentSessionRecord

Instead of creating a custom `ChatMessage` entity, `Conversation` embeds a reference to the existing `AgentSessionRecord` entity. The `AgentSessionRecord.SessionData` (JSONB) already stores the complete chat history as a serialized `ChatClientAgentSession`, which contains:

```json
{
  "ConversationId": null,
  "ChatHistoryProviderState": {
    "Messages": [
      { "Role": "user", "Contents": [{ "$type": "text", "Text": "Hello" }] },
      { "Role": "assistant", "Contents": [{ "$type": "text", "Text": "Hi there!" }] }
    ]
  },
  "AIContextProviderState": null
}
```

This avoids duplicating message storage and keeps data consistent with the Agent Framework's internal session model.

## Entities

### Conversation (Aggregate Root)

Represents a chat session between the user and a specific Agent. Once created with an Agent binding, the Agent cannot be changed. Chat history is stored in the associated `AgentSessionRecord`.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | UUID (PK) | Required, auto-generated | Unique identifier |
| AgentId | UUID (FK) | Required, immutable after creation | Reference to AgentRegistration.Id |
| Title | string | Max 200 chars, nullable | Conversation title (auto-generated from first message, nullable until first message) |
| CreatedAt | DateTime | Required, auto-set | When the conversation was created |
| UpdatedAt | DateTime? | Auto-set on modification | Last activity timestamp |

**Relationships**:
- Belongs to one `AgentRegistration` (many-to-one, via AgentId)
- References one `AgentSessionRecord` (via composite key: AgentRegistration.Name + Conversation.Id.ToString())

**AgentSessionRecord link**: The `Conversation` does NOT have a direct FK to `AgentSessionRecord`. Instead, the session is located by the composite key:
- `AgentSessionRecord.AgentId` = `AgentRegistration.Name` (string, the agent's stable name)
- `AgentSessionRecord.ConversationId` = `Conversation.Id.ToString()` (string representation of the UUID)

This is a **logical reference**, not a database FK constraint, because `AgentSessionRecord` uses string composite keys while `Conversation` uses UUID PKs. The Application layer resolves this mapping.

**Invariants**:
- `AgentId` is set at creation and MUST NOT change after first message is sent
- `Title` is auto-generated from the first user message (first 50 chars)

**Factory method**: `Conversation.Create(Guid agentId)` — creates a new conversation with the given agent binding

**Domain methods**:
- `SetTitle(string title)` — sets conversation title (only if not already set)
- `Touch()` — updates `UpdatedAt` timestamp (called when messages are exchanged)

---

### AgentSessionRecord (Existing Entity — from SPEC-004)

Already implemented. Stores the Agent Framework's serialized session data including full chat history.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| AgentId | string (PK part 1) | Required, max 255 | Agent name (from AIAgent.Id) |
| ConversationId | string (PK part 2) | Required, max 255 | Maps to Conversation.Id.ToString() |
| SessionData | JsonElement (JSONB) | Required | Serialized session including ChatHistoryProviderState with full message history |
| SessionType | string | Required, max 100 | e.g. "ChatClientAgentSession" — for diagnostics |
| CreatedAt | DateTime | Required | Record creation time |
| UpdatedAt | DateTime | Required | Last update time |

**Key point**: `SessionData` contains `ChatHistoryProviderState.Messages[]` — the complete chat history. The Application layer deserializes this to extract messages for display in the UI.

---

### Message Extraction (Read Model)

Chat messages are NOT a separate entity. They are extracted from `AgentSessionRecord.SessionData` at read time. The Application layer provides a DTO projection:

| DTO Field | Source | Description |
|-----------|--------|-------------|
| Role | `SessionData.ChatHistoryProviderState.Messages[i].Role` | "user" or "assistant" |
| Content | `SessionData.ChatHistoryProviderState.Messages[i].Contents[0].Text` | Message text |
| Index | Array position | Message ordering (0-based) |

The `GetConversationByIdQueryHandler` is responsible for:
1. Loading the `Conversation` entity
2. Resolving the agent name from `AgentRegistration`
3. Loading the `AgentSessionRecord` by `(agentName, conversationId)`
4. Deserializing `SessionData` to extract messages for the DTO

---

## State Transitions

### Conversation Lifecycle

```
[Created] ──first AG-UI run──→ [Active] ──Delete()──→ [Deleted]
                                   │
                                   └──subsequent runs──→ [Active] (repeating)
```

- **Created**: `Conversation.Create(agentId)` called. No messages yet. `AgentSessionRecord` does not exist until first AG-UI stream completes.
- **Active**: At least one AG-UI run has completed. `AgentSessionRecord` contains chat history. `UpdatedAt` is refreshed on each run.
- **Deleted**: Conversation is removed. Associated `AgentSessionRecord` should also be cleaned up.

### Message Flow (per turn — AG-UI protocol)

```
User types message
       │
       ▼
Frontend: useAgentChat hook
  1. agent.addMessage({ id, role: "user", content })
  2. agent.runAgent() → POST /api/chat/stream (RunAgentInput)
       │
       ▼
Backend: MapAGUI receives RunAgentInput
  1. IAgentResolver resolves agentId from context → ChatClientAgent
  2. ChatClientAgent uses ChatHistoryProvider backed by AgentSessionStore
  3. ChatClientAgent.RunStreamingAsync(messages, cancellationToken)
       │
       ▼
Backend → Frontend: AG-UI SSE events
  RUN_STARTED
  TEXT_MESSAGE_START { messageId, role: "assistant" }
  TEXT_MESSAGE_CONTENT { delta: "token" } (repeated)
  TEXT_MESSAGE_END { messageId }
  RUN_FINISHED
       │
       ▼
AgentSessionStore (PostgresAgentSessionStore):
  Automatically persists updated session with new messages
  to AgentSessionRecord.SessionData (JSONB)
       │
       ▼
Frontend: subscriber callbacks fire
  onRunFinished → call REST to touch Conversation.UpdatedAt
  onMessagesChanged → update UI message list
```

**Key difference from previous design**: Messages are NOT persisted via a separate REST call. The `ChatHistoryProvider` inside `ChatClientAgent` automatically appends request + response messages to the session, and `PostgresAgentSessionStore` persists the updated `AgentSessionRecord` after each invocation. The frontend only needs to call REST to update `Conversation.UpdatedAt` and `Title`.

## Database Schema (PostgreSQL)

### Table: `conversations` (NEW)

```sql
CREATE TABLE conversations (
    id              UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    agent_id        UUID        NOT NULL REFERENCES agent_registrations(id),
    title           VARCHAR(200),
    created_at      TIMESTAMP   NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMP
);

CREATE INDEX IX_conversations_agent_id ON conversations(agent_id);
CREATE INDEX IX_conversations_updated_at ON conversations(updated_at DESC);
```

### Table: `agent_sessions` (EXISTING — from SPEC-004)

```sql
-- Already exists, no changes needed
CREATE TABLE agent_sessions (
    agent_id        VARCHAR(255)  NOT NULL,
    conversation_id VARCHAR(255)  NOT NULL,
    session_data    JSONB         NOT NULL,
    session_type    VARCHAR(100)  NOT NULL,
    created_at      TIMESTAMP     NOT NULL,
    updated_at      TIMESTAMP     NOT NULL,
    PRIMARY KEY (agent_id, conversation_id)
);
```

**Logical join**: `agent_sessions.conversation_id = conversations.id::text AND agent_sessions.agent_id = agent_registrations.name`

## Validation Rules

| Entity | Field | Rule |
|--------|-------|------|
| Conversation | AgentId | Must be a valid, existing AgentRegistration ID |
| Conversation | Title | Max 200 characters |
