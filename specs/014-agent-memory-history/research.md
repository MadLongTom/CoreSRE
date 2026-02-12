# Research: Agent Memory & History Management

**Feature**: 014-agent-memory-history  
**Date**: 2026-02-11

---

## 1. ChatHistoryProvider Integration Pattern

### Decision: Use `InMemoryChatHistoryProvider` with framework session pipeline

**Rationale**: The `InMemoryChatHistoryProvider` is the SDK's default in-memory history implementation. It implements `IList<ChatMessage>`, supports serialization/deserialization (for session store persistence), and accepts an optional `IChatReducer`. The existing `PostgresAgentSessionStore` already handles serialize/deserialize via `agent.SerializeSession()` / `agent.DeserializeSessionAsync()`, so `InMemoryChatHistoryProvider` + `PostgresAgentSessionStore` gives a complete persistence pipeline with zero custom serialization code.

**Alternatives considered**:
- `CosmosChatHistoryProvider` — rejected because the project uses PostgreSQL, not Cosmos DB.
- Custom `ChatHistoryProvider` with EF Core — rejected because `InMemoryChatHistoryProvider` already provides all needed functionality; custom implementation would duplicate framework code.
- `WorkflowChatHistoryProvider` — internal to workflow engine, not suitable for direct agent chat.

### Wiring Pattern

Configure `ChatHistoryProviderFactory` on `ChatClientAgentOptions` in `AgentResolverService.ResolveChatClientAgent()`:

```csharp
options.ChatHistoryProviderFactory = (ctx, ct) =>
{
    IChatReducer? reducer = maxHistoryMessages.HasValue
        ? new MessageCountingChatReducer(maxHistoryMessages.Value)
        : null;

    ChatHistoryProvider provider = ctx.SerializedState.ValueKind == JsonValueKind.Object
        ? new InMemoryChatHistoryProvider(reducer, ctx.SerializedState, ctx.JsonSerializerOptions)
        : new InMemoryChatHistoryProvider(reducer);

    return ValueTask.FromResult(provider);
};
```

The factory receives `ctx.SerializedState` from the session store when restoring a prior session, or `default(JsonElement)` when creating a new session. The `ValueKind` check distinguishes the two cases.

---

## 2. Session Store Integration

### Decision: Wire existing `PostgresAgentSessionStore` via `AIHostAgent` wrapper

**Rationale**: `PostgresAgentSessionStore` already extends `AgentSessionStore` and implements proper UPSERT logic. The `AIHostAgent` wrapper provides `GetOrCreateSessionAsync()` and `SaveSessionAsync()` which encapsulate the session lifecycle. The chat endpoint should:
1. Wrap the resolved `AIAgent` in `AIHostAgent(agent, sessionStore)`
2. Call `hostAgent.GetOrCreateSessionAsync(conversationId)` to load or create session
3. Call `hostAgent.RunStreamingAsync(messages, session)` to run with history
4. Call `hostAgent.SaveSessionAsync(conversationId, session)` after streaming completes

This eliminates the raw SQL `PersistChatHistoryAsync` method entirely.

**Alternatives considered**:
- Manual serialization/deserialization without `AIHostAgent` — rejected because `AIHostAgent` encapsulates the pattern cleanly and handles edge cases.
- Builder-based registration (`builder.WithSessionStore(...)`) — rejected because the project doesn't use `AddHostedAgent` DI pattern; agents are resolved dynamically by ID at runtime.

---

## 3. Token Window Management via IChatReducer

### Decision: Use `MessageCountingChatReducer` with configurable message count

**Rationale**: The simplest and most predictable strategy. `MessageCountingChatReducer(int maxMessages)` keeps the last N non-system messages, preserving system prompts. This is the SDK's built-in implementation. Token-counting reducers require model-specific tokenizer dependencies and add complexity for marginal benefit at this stage.

**Alternatives considered**:
- Token-counting reducer — deferred to future iteration. Would require integrating a tokenizer (e.g., tiktoken) and knowing each model's exact context window size.
- Summarization reducer — deferred. Would require an additional LLM call to summarize truncated messages, adding latency and cost.
- No reducer (rely on model's built-in truncation) — rejected because model truncation is unpredictable and can lose important context.

### Configuration

`LlmConfigVO.MaxHistoryMessages` maps to `MessageCountingChatReducer` constructor parameter. Platform default: 50 messages (configurable via `appsettings.json`).

`ChatReducerTriggerEvent.BeforeMessagesRetrieval` (the default) is preferred — reducer runs just before messages are sent to the LLM, so the full history is stored but only a window is sent.

---

## 4. AG-UI Protocol Compatibility

### Decision: Dual-mode operation based on `EnableChatHistory` flag

**Rationale**: The current AG-UI protocol is stateless — the frontend sends the full message list with every request. Changing this globally would break the existing contract. Instead:

- **`EnableChatHistory = true`** (new default for ChatClient agents): Backend loads history from session store. Frontend-provided messages are used only for the *new user message* (the last message in the list). Prior messages are ignored in favor of the stored history.
- **`EnableChatHistory = false`**: Backward-compatible stateless mode. Backend uses all frontend-provided messages as-is (current behavior). No session store involved.

The chat endpoint detects the mode from the resolved agent's `LlmConfig.EnableChatHistory` value.

**Alternatives considered**:
- Always use server-side history (breaking change) — rejected for backward compatibility.
- New dedicated endpoint for stateful chat — rejected because it would fragment the API surface unnecessarily.

---

## 5. Semantic Memory via ChatHistoryMemoryProvider

### Decision: Use `ChatHistoryMemoryProvider` (AIContextProvider) with pgvector VectorStore

**Rationale**: `ChatHistoryMemoryProvider` is purpose-built for this use case — it extends `AIContextProvider`, stores conversation messages as vector embeddings, and retrieves semantically similar past messages. It plugs into `ChatClientAgentOptions.AIContextProviderFactory`, independent of `ChatHistoryProvider`.

Key design points:
- Uses `Microsoft.Extensions.VectorData.VectorStore` abstraction
- Scoped via `ChatHistoryMemoryProviderScope` (ApplicationId, AgentId, UserId, SessionId)
- `SearchBehavior.BeforeAIInvoke` injects memories automatically; `OnDemandFunctionCalling` lets the LLM decide when to search
- The SDK creates the vector collection schema automatically (Key, Role, Content, ContentEmbedding, etc.)

**Alternatives considered**:
- `Mem0Provider` — viable alternative but adds an external dependency (Mem0 service). `ChatHistoryMemoryProvider` is first-party and uses standard `VectorStore` abstraction.
- Custom vector search implementation — rejected because the SDK already provides the full pipeline.
- Semantic Kernel memory — rejected because the project uses Microsoft.Agents, not Semantic Kernel.

### Vector Store Provider

pgvector is the natural choice because:
1. Project already uses PostgreSQL
2. `Npgsql.EntityFrameworkCore.PostgreSQL` is already referenced
3. No additional database infrastructure needed

Need to add:
- `Microsoft.Extensions.VectorData` NuGet package (or the pgvector adapter package)
- Enable `pgvector` PostgreSQL extension via migration
- Configure an `IEmbeddingGenerator` from the LLM provider for vector generation

### Embedding Generator

The `ChatHistoryMemoryProvider` requires a `VectorStore` with an `EmbeddingGenerator`. The project's LLM providers (OpenAI-compatible) can serve embedding models. The embedding model and dimensions will be configured at the platform level (e.g., `text-embedding-3-small` with 1536 dimensions).

---

## 6. LlmConfigVO Extension

### Decision: Add 5 new nullable fields to existing value object

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `EnableChatHistory` | `bool?` | `true` | Enable framework history management |
| `MaxHistoryMessages` | `int?` | `null` (platform default: 50) | Message-count truncation threshold |
| `EnableSemanticMemory` | `bool?` | `false` | Enable cross-session vector memory |
| `MemorySearchMode` | `string?` | `"BeforeAIInvoke"` | `BeforeAIInvoke` or `OnDemandFunctionCalling` |
| `MemoryMaxResults` | `int?` | `null` (SDK default: 3) | Max semantic memory results |

**Rationale**: All fields are nullable to maintain backward compatibility with existing JSONB data. Missing fields in existing records default to safe values (history on, memory off). No database migration needed for JSONB changes — PostgreSQL handles nullable fields transparently.

---

## 7. Chat Endpoint Refactoring Strategy

### Decision: Refactor `HandleChatClientStreamAsync` to use Agent Framework pipeline

Current flow:
```
Frontend → messages[] → chatClient.GetStreamingResponseAsync(messages) → manual PersistChatHistoryAsync (raw SQL)
```

New flow:
```
Frontend → messages[] →
  1. Resolve agent with ChatHistoryProvider configured
  2. Wrap in AIHostAgent(agent, sessionStore)
  3. GetOrCreateSessionAsync(conversationId) — loads stored history
  4. agent.RunStreamingAsync(newUserMessage, session) — framework merges history
  5. Stream SSE events to frontend
  6. SaveSessionAsync(conversationId, session) — framework serializes history
```

Key changes:
- `PersistChatHistoryAsync` method is **deleted** entirely
- SSE event generation moves from manual `chatClient` streaming to `agent.RunStreamingAsync` output
- The frontend's `messages[]` payload is still accepted but only the last user message is extracted when `EnableChatHistory = true`

---

## 8. Frontend Configuration UI

### Decision: Add collapsible "History & Memory" section to `LlmConfigSection.tsx`

Pattern follows existing "Advanced" section implementation:
- Collapsible `<Collapsible>` component with "History & Memory" header
- Toggle switch for `EnableChatHistory`
- Number input for `MaxHistoryMessages` (shown when history enabled)
- Toggle switch for `EnableSemanticMemory`
- Select dropdown for `MemorySearchMode` (shown when memory enabled)
- Number input for `MemoryMaxResults` (shown when memory enabled)

All fields use the same `onChange` pattern as existing advanced fields.

---

## 9. Migration Strategy

### Phase 1 & 2 (Chat History + Reducer)
- **No schema migration needed** — `LlmConfigVO` is stored as JSONB in `agent_registrations.llm_config`. New nullable fields in the JSONB are transparent to PostgreSQL.
- **No data migration needed** — existing `agent_sessions` table schema is compatible. The framework's `SerializeSession/DeserializeSession` writes the same `chatHistoryProviderState.messages[]` structure already used by manual SQL.

### Phase 3 (Semantic Memory)
- **New migration needed**:
  1. Enable pgvector extension: `CREATE EXTENSION IF NOT EXISTS vector`
  2. The vector collection table is created automatically by the `VectorStore` SDK — no manual table definition needed
- **New NuGet packages**: vector data adapter for PostgreSQL

---

## 10. Error Handling & Graceful Degradation

### Decision: Best-effort persistence, never block chat

| Failure | Behavior |
|---------|----------|
| Session load fails | Log warning, create fresh session, continue chat |
| Session save fails | Log error, return response to user anyway |
| Corrupted session data | Log warning, create fresh session |
| Vector store unavailable | Log info, skip memory features, chat works normally |
| Embedding model unavailable | Log warning, skip memory storage/retrieval |
| `MaxHistoryMessages ≤ 0` | Treat as null (platform default applies) |

All error handling wraps persistence/memory operations in try-catch blocks. The chat response is always delivered to the user.
