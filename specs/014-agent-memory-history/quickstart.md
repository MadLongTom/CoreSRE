# Quickstart: Agent Memory & History Management

**Feature**: 014-agent-memory-history  
**Date**: 2026-02-11

---

## What This Feature Does

Replaces the stateless chat pattern (frontend sends full message history every time) with framework-managed server-side history. Adds token window management to prevent context overflow in long conversations. Enables cross-session semantic memory so agents can "recall" past conversations.

---

## Implementation Phases

### Phase 1: Framework-Managed Chat History (P1)

**Goal**: Wire `ChatHistoryProvider` + `PostgresAgentSessionStore` into the agent pipeline, eliminate manual SQL.

#### Step 1 — Extend `LlmConfigVO` (Domain Layer)

Add 5 new nullable fields to the sealed record:

```csharp
// CoreSRE.Domain/ValueObjects/LlmConfigVO.cs
public bool? EnableChatHistory { get; init; }        // default: true
public int? MaxHistoryMessages { get; init; }        // null = platform default (50)
public bool? EnableSemanticMemory { get; init; }     // default: false
public string? MemorySearchMode { get; init; }       // "BeforeAIInvoke" | "OnDemandFunctionCalling"
public int? MemoryMaxResults { get; init; }          // null = SDK default (3)
```

No migration needed — JSONB handles new nullable fields transparently.

#### Step 2 — Configure `ChatHistoryProviderFactory` (Infrastructure Layer)

In `AgentResolverService.ResolveChatClientAgent()`, add to `ChatClientAgentOptions`:

```csharp
var enableHistory = reg.LlmConfig?.EnableChatHistory ?? true;
var maxMessages = reg.LlmConfig?.MaxHistoryMessages;

if (enableHistory)
{
    options.ChatHistoryProviderFactory = (ctx, ct) =>
    {
        IChatReducer? reducer = maxMessages.HasValue
            ? new MessageCountingChatReducer(maxMessages.Value)
            : new MessageCountingChatReducer(50); // platform default

        ChatHistoryProvider provider = ctx.SerializedState.ValueKind == JsonValueKind.Object
            ? new InMemoryChatHistoryProvider(reducer, ctx.SerializedState, ctx.JsonSerializerOptions)
            : new InMemoryChatHistoryProvider(reducer);

        return ValueTask.FromResult(provider);
    };
}
```

#### Step 3 — Refactor Chat Endpoint (API Layer)

Replace `HandleChatClientStreamAsync` flow:

```csharp
// Before: chatClient.GetStreamingResponseAsync(messages) + manual SQL persist
// After:
var sessionStore = sp.GetRequiredService<PostgresAgentSessionStore>();
var hostAgent = new AIHostAgent(aiAgent, sessionStore);
var session = await hostAgent.GetOrCreateSessionAsync(conversationId);

// Extract only the new user message from frontend payload
var newMessage = new ChatMessage(ChatRole.User, input.Messages.Last().Content);

await foreach (var update in hostAgent.RunStreamingAsync(newMessage, session))
{
    // Emit SSE events from AgentResponseUpdate
}

await hostAgent.SaveSessionAsync(conversationId, session);
```

Delete `PersistChatHistoryAsync` method entirely.

#### Step 4 — Wire Session Store in DI

```csharp
// CoreSRE.Infrastructure/DependencyInjection.cs
services.AddSingleton<AgentSessionStore>(sp =>
    CreatePostgresSessionStore(sp)); // existing factory method — just wire it
```

---

### Phase 2: Token Window Management (P2)

**Goal**: Add configurable message-count truncation.

Already handled in Phase 1 Step 2 — `MessageCountingChatReducer` is configured based on `MaxHistoryMessages`. The reducer runs `BeforeMessagesRetrieval`, so:
- Full history is stored in the session
- Only the last N messages are sent to the LLM
- The frontend always sees all messages

#### Frontend UI

Add "History & Memory" collapsible section to `LlmConfigSection.tsx`:

```tsx
<Collapsible>
  <CollapsibleTrigger>History & Memory</CollapsibleTrigger>
  <CollapsibleContent>
    <Switch label="Enable Chat History" checked={config.enableChatHistory ?? true} />
    <NumberInput label="Max History Messages" value={config.maxHistoryMessages} />
    <Switch label="Enable Semantic Memory" checked={config.enableSemanticMemory ?? false} />
    {/* MemorySearchMode and MemoryMaxResults shown when memory enabled */}
  </CollapsibleContent>
</Collapsible>
```

---

### Phase 3: Semantic Memory (P3)

**Goal**: Cross-session vector recall.

#### Step 1 — Add pgvector Extension

New migration:
```sql
CREATE EXTENSION IF NOT EXISTS vector;
```

#### Step 2 — Configure VectorStore + Embedding Generator

```csharp
// DI registration
services.AddSingleton<VectorStore>(sp => /* pgvector VectorStore adapter */);
services.AddSingleton<IEmbeddingGenerator>(sp => /* from LLM provider */);
```

#### Step 3 — Wire `AIContextProviderFactory`

In `AgentResolverService`, when `EnableSemanticMemory == true`:

```csharp
options.AIContextProviderFactory = async (ctx, ct) =>
{
    var vectorStore = serviceProvider.GetRequiredService<VectorStore>();
    return new ChatHistoryMemoryProvider(
        vectorStore,
        collectionName: "agent_memories",
        vectorDimensions: 1536,
        storageScope: new ChatHistoryMemoryProviderScope
        {
            ApplicationId = "CoreSRE",
            AgentId = agentId.ToString(),
        },
        options: new ChatHistoryMemoryProviderOptions
        {
            SearchTime = searchMode,
            MaxResults = maxResults,
        });
};
```

---

## Testing Strategy (Constitution-Compliant)

Per Constitution Principle II (TDD — NON-NEGOTIABLE), follow Red-Green-Refactor:

1. **Write tests first** for `AgentResolverService` — verify `ChatHistoryProviderFactory` is set when `EnableChatHistory = true`, not set when `false`
2. **Write tests first** for token window — verify `MessageCountingChatReducer` is configured with correct maxMessages
3. **Write tests first** for endpoint — verify session store is called (mock), no raw SQL executed
4. Run tests (Red — they fail)
5. Implement changes
6. Run tests (Green — they pass)

### Test Coverage Targets (per Constitution)
- Domain: 95% (LlmConfigVO field additions — trivial)
- Infrastructure: 80% (AgentResolverService configuration tests)
- API: 80% (endpoint refactoring tests)

---

## Verification Checklist

- [ ] Conversation history persists across browser refreshes
- [ ] `PostgresAgentSessionStore` is used — no raw SQL in endpoint
- [ ] Long conversations (100+ messages) don't trigger token errors
- [ ] `MaxHistoryMessages` setting is respected
- [ ] `EnableChatHistory = false` preserves stateless behavior
- [ ] Frontend shows History & Memory config controls
- [ ] All existing tests pass (no regressions)
- [ ] New tests follow `{Method}_{Scenario}_{ExpectedResult}` naming
