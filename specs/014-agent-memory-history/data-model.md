# Data Model: Agent Memory & History Management

**Feature**: 014-agent-memory-history  
**Date**: 2026-02-11

---

## Entity Diagram

```
┌─────────────────────────┐         ┌──────────────────────────┐
│   AgentRegistration     │         │     Conversation         │
│   (existing)            │         │     (existing)           │
├─────────────────────────┤         ├──────────────────────────┤
│ Id: Guid (PK)           │    1:N  │ Id: Guid (PK)            │
│ Name: string            │◄────────│ AgentId: Guid (FK)       │
│ AgentType: enum         │         │ Title: string            │
│ LlmConfig: LlmConfigVO  │         │ CreatedAt, UpdatedAt     │
│ ...                     │         └──────────┬───────────────┘
└─────────────────────────┘                    │
         │                                     │
         │ LlmConfig (JSONB)                   │ 1:1
         ▼                                     ▼
┌─────────────────────────┐         ┌──────────────────────────┐
│   LlmConfigVO           │         │   AgentSessionRecord     │
│   (modified)            │         │   (existing, no changes) │
├─────────────────────────┤         ├──────────────────────────┤
│ ProviderId: Guid?       │         │ AgentId: string (PK)     │
│ ModelId: string         │         │ ConversationId: string   │
│ Instructions: string?   │         │   (PK)                   │
│ ToolRefs: List<Guid>    │         │ SessionData: JsonElement  │
│ Temperature: float?     │         │   (JSONB)                │
│ MaxOutputTokens: int?   │         │ SessionType: string      │
│ ... (existing fields)   │         │ CreatedAt, UpdatedAt     │
│─────────────────────────│         └──────────────────────────┘
│ EnableChatHistory: bool?│  ← NEW
│ MaxHistoryMessages: int?│  ← NEW
│ EnableSemanticMemory:   │  ← NEW
│   bool?                 │
│ MemorySearchMode:       │  ← NEW
│   string?               │
│ MemoryMaxResults: int?  │  ← NEW
└─────────────────────────┘

         Phase 3 (Semantic Memory) — vector collection managed by SDK
         ┌──────────────────────────────────────┐
         │   agent_memories (VectorStore)       │
         │   (auto-created by SDK)              │
         ├──────────────────────────────────────┤
         │ Key: Guid (PK)                       │
         │ Role: string (indexed)               │
         │ MessageId: string (indexed)          │
         │ AuthorName: string                   │
         │ ApplicationId: string (indexed)      │
         │ AgentId: string (indexed)            │
         │ UserId: string (indexed)             │
         │ SessionId: string (indexed)          │
         │ Content: string (full-text indexed)  │
         │ CreatedAt: string (indexed)          │
         │ ContentEmbedding: vector(N)          │
         └──────────────────────────────────────┘
```

---

## Entities

### LlmConfigVO (Modified)

**Location**: `CoreSRE.Domain/ValueObjects/LlmConfigVO.cs`  
**Storage**: JSONB column on `agent_registrations` table  
**Type**: Sealed record (value object, immutable)

#### New Fields

| Field | Type | Default | Validation | Description |
|-------|------|---------|------------|-------------|
| `EnableChatHistory` | `bool?` | `true` | — | Enables server-side chat history management via framework |
| `MaxHistoryMessages` | `int?` | `null` | Must be `null` or `> 0` | Max messages to keep in context window. `null` = platform default (50) |
| `EnableSemanticMemory` | `bool?` | `false` | — | Enables cross-session vector memory retrieval |
| `MemorySearchMode` | `string?` | `null` | Must be `null`, `"BeforeAIInvoke"`, or `"OnDemandFunctionCalling"` | How semantic memory is injected |
| `MemoryMaxResults` | `int?` | `null` | Must be `null` or `> 0` | Max memory snippets returned per query. `null` = SDK default (3) |

#### Backward Compatibility

- All new fields are nullable
- Existing JSONB records without these fields will deserialize with `null` values
- `null` for `EnableChatHistory` is treated as `true` (framework managed) for ChatClient agents
- `null` for `EnableSemanticMemory` is treated as `false` (disabled)
- No database migration required for JSONB shape changes

---

### AgentSessionRecord (Unchanged)

**Location**: `CoreSRE.Domain/Entities/AgentSessionRecord.cs`  
**Table**: `agent_sessions`

No structural changes. The `SessionData` JSONB column will contain the framework's serialized session format:

```json
{
  "chatHistoryProviderState": {
    "messages": [
      {
        "role": "system",
        "contents": [{"text": "You are..."}]
      },
      {
        "role": "user",
        "contents": [{"text": "Hello"}]
      },
      {
        "role": "assistant",
        "contents": [{"text": "Hi there!"}]
      }
    ]
  }
}
```

This is the same format currently written by the manual SQL in `PersistChatHistoryAsync`, so existing session data is compatible with the framework's deserialization.

---

### Conversation (Unchanged)

**Location**: `CoreSRE.Domain/Entities/Conversation.cs`  
**Table**: `conversations`

No changes. Conversation metadata continues to reference `AgentId` and store `Title`. Chat history lives in `AgentSessionRecord.SessionData`.

---

### Agent Memory Collection (New — Phase 3)

**Not a domain entity** — managed by the `VectorStore` SDK automatically.  
**Table**: `agent_memories` (name configurable via `collectionName` parameter)  
**Engine**: pgvector extension

The `ChatHistoryMemoryProvider` creates and manages this collection through the `Microsoft.Extensions.VectorData.VectorStore` abstraction. The schema is defined by the SDK's inner `ChatHistoryMemoryRecord` class — no domain entity or EF configuration needed.

**Scoping**:
- `ApplicationId`: `"CoreSRE"` (constant for all agents)
- `AgentId`: `AgentRegistration.Id.ToString()`
- `UserId`: Derived from conversation context (currently no auth — may use a placeholder or session-derived identifier)
- `SessionId`: `Conversation.Id.ToString()`

**Isolation**: Search queries filter by `ApplicationId + AgentId + UserId`, ensuring per-user memory isolation.

---

## State Transitions

### Chat Session Lifecycle

```
                    ┌─────────────────┐
                    │   No Session    │
                    │   (first msg)   │
                    └────────┬────────┘
                             │ GetOrCreateSessionAsync
                             ▼
                    ┌─────────────────┐
                    │  Active Session │◄──────────────────┐
                    │  (in memory)    │                    │
                    └────────┬────────┘                    │
                             │                            │
                   ┌─────────┼──────────┐                 │
                   ▼         ▼          ▼                 │
          ┌──────────┐ ┌──────────┐ ┌──────────┐         │
          │ Reducer  │ │ Run LLM  │ │ Memory   │         │
          │ truncates│ │ via agent│ │ retrieval │         │
          │ messages │ │ pipeline │ │ (Phase 3) │         │
          └──────────┘ └──────────┘ └──────────┘         │
                   │         │          │                 │
                   └─────────┼──────────┘                 │
                             ▼                            │
                    ┌─────────────────┐                   │
                    │ Save Session    │───────────────────┘
                    │ (PostgresStore) │   next message
                    └─────────────────┘
```

### EnableChatHistory Modes

```
EnableChatHistory = true (default):
  Frontend messages[] → extract last user message only
                       → session store provides full history
                       → reducer trims if needed
                       → LLM receives: stored history + new message

EnableChatHistory = false (backward compatible):
  Frontend messages[] → use all messages as-is
                       → no session store interaction
                       → no reducer
                       → LLM receives: frontend-provided messages
```

---

## Relationships Summary

| Relationship | Type | Description |
|---|---|---|
| AgentRegistration → LlmConfigVO | Embedded (JSONB) | Configuration stored as part of agent |
| AgentRegistration → Conversation | 1:N | Agent has many conversations |
| Conversation → AgentSessionRecord | 1:1 | Each conversation has one session record |
| AgentRegistration → agent_memories | 1:N (via scope) | Memory embeddings scoped to agent |
| User → agent_memories | 1:N (via scope) | Memory embeddings scoped to user |
