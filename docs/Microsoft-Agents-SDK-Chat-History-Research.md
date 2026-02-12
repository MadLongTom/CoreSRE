# Microsoft Agent Framework — Chat History, Reducers & Memory Providers

> **Source repo**: [`microsoft/agent-framework`](https://github.com/microsoft/agent-framework)
> **NuGet**: `Microsoft.Agents.AI.Hosting` v1.0.0-preview.260209.1
> **Target frameworks**: .NET 8.0, .NET Standard 2.0, .NET Framework 4.7.2

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [ChatHistoryProvider (abstract)](#2-chathistoryprovider)
3. [InMemoryChatHistoryProvider](#3-inmemorychathistoryprovider)
4. [IChatReducer & Built-in Implementations](#4-ichatreducer)
5. [ChatHistoryMemoryProvider (vector-backed)](#5-chathistorychathistorymemoryprovider)
6. [ChatHistoryMemoryProviderScope](#6-chathistorychathistorymemoryprovidersscope)
7. [ChatHistoryMemoryProviderOptions](#7-chathistorychathistorymemoryprovidersoptions)
8. [ChatClientAgentOptions](#8-chatclientagentoptions)
9. [ChatClientAgent](#9-chatclientagent)
10. [AgentSession Hierarchy](#10-agentsession-hierarchy)
11. [AgentSessionStore](#11-agentsessionstore)
12. [AIAgent Base Class & RunStreamingAsync](#12-aiagent)
13. [VectorStore Abstraction](#13-vectorstore-abstraction)
14. [Wiring Samples](#14-wiring-samples)

---

## 1. Architecture Overview

There are **two parallel provider systems** that plug into `ChatClientAgent`:

| Concern | Base Class | Wired via | Purpose |
|---|---|---|---|
| **Message history** | `ChatHistoryProvider` | `ChatClientAgentOptions.ChatHistoryProviderFactory` | Store/retrieve conversation messages |
| **Contextual augmentation** | `AIContextProvider` | `ChatClientAgentOptions.AIContextProviderFactory` | Inject extra tools, instructions, or retrieved memory into invocations |

`ChatHistoryMemoryProvider` extends **`AIContextProvider`**, NOT `ChatHistoryProvider`.
It stores all messages in a vector store and retrieves semantically similar past messages.

```
ChatHistoryProvider (abstract)        AIContextProvider (abstract)
├── InMemoryChatHistoryProvider       ├── ChatHistoryMemoryProvider
├── CosmosChatHistoryProvider         ├── Mem0Provider
└── WorkflowChatHistoryProvider       └── (custom)
```

---

## 2. ChatHistoryProvider

**File**: `dotnet/src/Microsoft.Agents.AI.Abstractions/ChatHistoryProvider.cs`
**Namespace**: `Microsoft.Agents.AI`
**Assembly**: `Microsoft.Agents.AI.Abstractions`

```csharp
public abstract class ChatHistoryProvider
{
    // ── Lifecycle hooks (called by the agent pipeline) ──────────

    // Public entry points (call the Core methods)
    public ValueTask<IEnumerable<ChatMessage>> InvokingAsync(InvokingContext context, CancellationToken ct);
    public ValueTask InvokedAsync(InvokedContext context, CancellationToken ct);

    // Override these in subclasses
    protected abstract ValueTask<IEnumerable<ChatMessage>> InvokingCoreAsync(
        InvokingContext context, CancellationToken ct);

    protected abstract ValueTask InvokedCoreAsync(
        InvokedContext context, CancellationToken ct);

    // ── Serialization ───────────────────────────────────────────
    public abstract JsonElement Serialize(JsonSerializerOptions? options = null);

    // ── Service provider pattern ────────────────────────────────
    public virtual object? GetService(Type serviceType, object? serviceKey = null);

    // ── Nested context classes ──────────────────────────────────

    public class InvokingContext
    {
        public AIAgent Agent { get; }
        public AgentSession Session { get; }
        public IEnumerable<ChatMessage> RequestMessages { get; }
    }

    public class InvokedContext
    {
        public AIAgent Agent { get; }
        public AgentSession Session { get; }
        public IEnumerable<ChatMessage> RequestMessages { get; }
        public IEnumerable<ChatMessage>? ResponseMessages { get; }
        public Exception? InvokeException { get; }
    }
}
```

### Known Implementations

| Class | Assembly | Storage |
|---|---|---|
| `InMemoryChatHistoryProvider` | Abstractions | In-memory `List<ChatMessage>` |
| `CosmosChatHistoryProvider` | `Microsoft.Agents.AI.CosmosNoSql` | Cosmos DB (hierarchical partition keys) |
| `WorkflowChatHistoryProvider` | `Microsoft.Agents.AI.Workflows` | Bookmark-based (internal) |

---

## 3. InMemoryChatHistoryProvider

**File**: `dotnet/src/Microsoft.Agents.AI.Abstractions/InMemoryChatHistoryProvider.cs`
**Namespace**: `Microsoft.Agents.AI`

```csharp
[DebuggerDisplay("Count = {Count}")]
public sealed class InMemoryChatHistoryProvider
    : ChatHistoryProvider, IList<ChatMessage>, IReadOnlyList<ChatMessage>
{
    private List<ChatMessage> _messages;
```

### Constructors

```csharp
// 1. Empty — no reducer
public InMemoryChatHistoryProvider();

// 2. Restore from serialized state — no reducer
public InMemoryChatHistoryProvider(
    JsonElement serializedState,
    JsonSerializerOptions? jsonSerializerOptions = null);

// 3. With reducer
public InMemoryChatHistoryProvider(
    IChatReducer chatReducer,
    ChatReducerTriggerEvent reducerTriggerEvent = ChatReducerTriggerEvent.BeforeMessagesRetrieval);

// 4. Full constructor (all others delegate here)
public InMemoryChatHistoryProvider(
    IChatReducer? chatReducer,
    JsonElement serializedState,
    JsonSerializerOptions? jsonSerializerOptions = null,
    ChatReducerTriggerEvent reducerTriggerEvent = ChatReducerTriggerEvent.BeforeMessagesRetrieval);
```

### Properties

```csharp
public IChatReducer? ChatReducer { get; }
public ChatReducerTriggerEvent ReducerTriggerEvent { get; }
public int Count { get; }
public bool IsReadOnly { get; }
public ChatMessage this[int index] { get; set; }
```

### Lifecycle Behaviour

**`InvokingCoreAsync`** (called before LLM invocation):
```csharp
protected override async ValueTask<IEnumerable<ChatMessage>> InvokingCoreAsync(
    InvokingContext context, CancellationToken ct)
{
    if (ReducerTriggerEvent is ChatReducerTriggerEvent.BeforeMessagesRetrieval
        && ChatReducer is not null)
    {
        _messages = (await ChatReducer.ReduceAsync(_messages, ct)).ToList();
    }
    return _messages;
}
```

**`InvokedCoreAsync`** (called after LLM responds):
```csharp
protected override async ValueTask InvokedCoreAsync(
    InvokedContext context, CancellationToken ct)
{
    if (context.InvokeException is not null) return;

    var allNew = context.RequestMessages.Concat(context.ResponseMessages ?? []);
    _messages.AddRange(allNew);

    if (ReducerTriggerEvent is ChatReducerTriggerEvent.AfterMessageAdded
        && ChatReducer is not null)
    {
        _messages = (await ChatReducer.ReduceAsync(_messages, ct)).ToList();
    }
}
```

### ChatReducerTriggerEvent (nested enum)

```csharp
public enum ChatReducerTriggerEvent
{
    AfterMessageAdded,          // Reducer runs after InvokedCoreAsync adds messages
    BeforeMessagesRetrieval     // Reducer runs before InvokingCoreAsync returns messages (default)
}
```

### Serialization

```csharp
public override JsonElement Serialize(JsonSerializerOptions? options = null);
// Serializes internal State { Messages = _messages }
```

### Collection Operations

Full `IList<ChatMessage>`: `Add`, `Remove`, `Insert`, `Clear`, `IndexOf`, `Contains`, `CopyTo`, `GetEnumerator`, indexer.

---

## 4. IChatReducer

**Namespace**: `Microsoft.Extensions.AI` (from the `Microsoft.Extensions.AI` package)

```csharp
public interface IChatReducer
{
    Task<IEnumerable<ChatMessage>> ReduceAsync(
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken = default);
}
```

> The sample comment confirms: *"Any implementation of `Microsoft.Extensions.AI.IChatReducer` can be used."*

### Built-in: `MessageCountingChatReducer`

Used in the official `Agent_Step16_ChatReduction` sample:

```csharp
// Keeps only the last N non-system messages
var reducer = new MessageCountingChatReducer(2);

// Constructor signature (inferred from usage):
public MessageCountingChatReducer(int maxMessages);
```

---

## 5. ChatHistoryMemoryProvider

**File**: `dotnet/src/Microsoft.Agents.AI/Memory/ChatHistoryMemoryProvider.cs`
**Namespace**: `Microsoft.Agents.AI`
**Extends**: `AIContextProvider, IDisposable` — ⚠️ NOT ChatHistoryProvider

Stores all chat messages in a vector store and retrieves semantically related history via similarity search.

### Constants

```csharp
DefaultContextPrompt = "## Memories\nConsider the following memories...";
DefaultMaxResults = 3;
DefaultFunctionToolName = "Search";
```

### Constructors

```csharp
// 1. Primary public constructor
public ChatHistoryMemoryProvider(
    VectorStore vectorStore,
    string collectionName,
    int vectorDimensions,
    ChatHistoryMemoryProviderScope storageScope,
    ChatHistoryMemoryProviderScope? searchScope = null,
    ChatHistoryMemoryProviderOptions? options = null,
    ILoggerFactory? loggerFactory = null);

// 2. From serialized state
public ChatHistoryMemoryProvider(
    VectorStore vectorStore,
    string collectionName,
    int vectorDimensions,
    JsonElement serializedState,
    JsonSerializerOptions? jsonSerializerOptions = null,
    ChatHistoryMemoryProviderOptions? options = null,
    ILoggerFactory? loggerFactory = null);
```

### Vector Store Collection Schema

The collection is created with this definition:

| Property | Type | Attributes |
|---|---|---|
| `Key` | `Guid` | Key |
| `Role` | `string` | Indexed |
| `MessageId` | `string` | Indexed |
| `AuthorName` | `string` | — |
| `ApplicationId` | `string` | Indexed |
| `AgentId` | `string` | Indexed |
| `UserId` | `string` | Indexed |
| `SessionId` | `string` | Indexed |
| `Content` | `string` | FullTextIndexed |
| `CreatedAt` | `string` | Indexed |
| `ContentEmbedding` | `string` | Vector (vectorDimensions) |

### Behaviour

- **`InvokingCoreAsync`**: If `SearchBehavior.BeforeAIInvoke`, extracts text from request messages → `SearchTextAsync` → returns results as a user message with context prompt. If `SearchBehavior.OnDemandFunctionCalling`, returns tools for the model to call.
- **`InvokedCoreAsync`**: Stores request + response messages into the vector store via upsert.
- **`SearchChatHistoryAsync`**: Builds filter expressions from scope properties → `collection.SearchAsync()`.
- Creates an on-demand search tool via `AIFunctionFactory.Create(SearchTextAsync, ...)`.

---

## 6. ChatHistoryMemoryProviderScope

**File**: `dotnet/src/Microsoft.Agents.AI/Memory/ChatHistoryMemoryProviderScope.cs`

```csharp
public sealed class ChatHistoryMemoryProviderScope
{
    public ChatHistoryMemoryProviderScope();
    public ChatHistoryMemoryProviderScope(ChatHistoryMemoryProviderScope sourceScope); // clone

    public string? ApplicationId { get; set; }
    public string? AgentId { get; set; }
    public string? SessionId { get; set; }
    public string? UserId { get; set; }
}
```

Used for both **storage** (which scope fields to write) and **search** (which scope fields to filter by). Enables cross-session and cross-agent memory retrieval.

---

## 7. ChatHistoryMemoryProviderOptions

**File**: `dotnet/src/Microsoft.Agents.AI/Memory/ChatHistoryMemoryProviderOptions.cs`

```csharp
public sealed class ChatHistoryMemoryProviderOptions
{
    public SearchBehavior SearchTime { get; set; }       // default: BeforeAIInvoke
    public string? FunctionToolName { get; set; }        // default: "Search"
    public string? FunctionToolDescription { get; set; } // default: "Allows searching..."
    public string? ContextPrompt { get; set; }
    public int? MaxResults { get; set; }                 // default: 3
    public bool EnableSensitiveTelemetryData { get; set; } // default: false

    public enum SearchBehavior
    {
        BeforeAIInvoke,           // Search before every LLM call, inject as context
        OnDemandFunctionCalling   // Expose as a tool the LLM can call
    }
}
```

---

## 8. ChatClientAgentOptions

**File**: `dotnet/src/Microsoft.Agents.AI/ChatClient/ChatClientAgentOptions.cs`

```csharp
public sealed class ChatClientAgentOptions
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public ChatOptions? ChatOptions { get; set; }
    public bool UseProvidedChatClientAsIs { get; set; }

    // ── Factory delegates for session-scoped providers ──────────

    public Func<ChatHistoryProviderFactoryContext, CancellationToken,
        ValueTask<ChatHistoryProvider>>? ChatHistoryProviderFactory { get; set; }

    public Func<AIContextProviderFactoryContext, CancellationToken,
        ValueTask<AIContextProvider>>? AIContextProviderFactory { get; set; }

    // ── Factory context classes ─────────────────────────────────

    public class ChatHistoryProviderFactoryContext
    {
        public JsonElement SerializedState { get; }
        public JsonSerializerOptions? JsonSerializerOptions { get; }
    }

    public class AIContextProviderFactoryContext
    {
        public JsonElement SerializedState { get; }
        public JsonSerializerOptions? JsonSerializerOptions { get; }
    }

    public ChatClientAgentOptions Clone(); // deep copy including ChatOptions
}
```

---

## 9. ChatClientAgent

**File**: `dotnet/src/Microsoft.Agents.AI/ChatClient/ChatClientAgent.cs`
**Extends**: `AIAgent`

```csharp
public sealed partial class ChatClientAgent : AIAgent
{
    // ── Constructors ────────────────────────────────────────────

    // Simple — builds ChatClientAgentOptions internally
    public ChatClientAgent(
        IChatClient chatClient,
        string? instructions,
        string? name = null,
        string? description = null,
        IList<AITool>? tools = null,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? serviceProvider = null);

    // Full options
    public ChatClientAgent(
        IChatClient chatClient,
        ChatClientAgentOptions? options = null,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? serviceProvider = null);

    // ── Session creation ────────────────────────────────────────

    // Uses factories from options
    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken ct);

    // Server-side conversation storage
    public ValueTask<AgentSession> CreateSessionAsync(
        string conversationId, CancellationToken ct = default);

    // Custom chat history provider
    public ValueTask<AgentSession> CreateSessionAsync(
        ChatHistoryProvider chatHistoryProvider, CancellationToken ct = default);
}
```

### Session Type: `ChatClientAgentSession`

```csharp
public sealed class ChatClientAgentSession : AgentSession
{
    public string? ConversationId { get; }                    // mutually exclusive with ChatHistoryProvider
    public ChatHistoryProvider? ChatHistoryProvider { get; }
    public AIContextProvider? AIContextProvider { get; }
}
```

Default behaviour: if no `ChatHistoryProviderFactory` is configured, `ChatClientAgentSession.DeserializeAsync` defaults to `new InMemoryChatHistoryProvider()`.

---

## 10. AgentSession Hierarchy

```
AgentSession (abstract)
├── InMemoryAgentSession (abstract)
│   └── ChatClientAgentSession (sealed)
├── ServiceIdAgentSession (abstract)
│   └── FoundryAgentSession, etc.
└── A2AAgentSession (sealed)
```

### AgentSession

```csharp
// File: dotnet/src/Microsoft.Agents.AI.Abstractions/AgentSession.cs
public abstract class AgentSession
{
    protected AgentSession();
    public virtual object? GetService(Type serviceType, object? serviceKey = null);
}
```

### InMemoryAgentSession

```csharp
// File: dotnet/src/Microsoft.Agents.AI.Abstractions/InMemoryAgentSession.cs
public abstract class InMemoryAgentSession : AgentSession
{
    protected InMemoryAgentSession(InMemoryChatHistoryProvider? chatHistoryProvider = null);
    protected InMemoryAgentSession(IEnumerable<ChatMessage> messages);
    protected InMemoryAgentSession(JsonElement serializedState, ...);

    public InMemoryChatHistoryProvider ChatHistoryProvider { get; }

    protected internal virtual JsonElement Serialize(JsonSerializerOptions? options = null);

    public override object? GetService(Type serviceType, object? serviceKey = null)
        => base.GetService(serviceType, serviceKey)
           ?? ChatHistoryProvider?.GetService(serviceType, serviceKey);
}
```

### ServiceIdAgentSession

```csharp
// File: dotnet/src/Microsoft.Agents.AI.Abstractions/ServiceIdAgentSession.cs
public abstract class ServiceIdAgentSession : AgentSession
{
    protected ServiceIdAgentSession();
    protected ServiceIdAgentSession(string serviceSessionId);
    protected ServiceIdAgentSession(JsonElement serializedState, JsonSerializerOptions? options = null);

    protected string? ServiceSessionId { get; set; }

    protected internal virtual JsonElement Serialize(JsonSerializerOptions? options = null);
}
```

---

## 11. AgentSessionStore

**File**: `dotnet/src/Microsoft.Agents.AI.Hosting/AgentSessionStore.cs`
**Namespace**: `Microsoft.Agents.AI.Hosting`

```csharp
public abstract class AgentSessionStore
{
    public abstract ValueTask SaveSessionAsync(
        AIAgent agent,
        string conversationId,
        AgentSession session,
        CancellationToken cancellationToken = default);

    public abstract ValueTask<AgentSession> GetSessionAsync(
        AIAgent agent,
        string conversationId,
        CancellationToken cancellationToken = default);
}
```

### Implementations

| Class | Assembly | Behaviour |
|---|---|---|
| `InMemoryAgentSessionStore` | `Microsoft.Agents.AI.Hosting` | `ConcurrentDictionary`-backed; serializes via `agent.SerializeSession()` on save |
| `NoopAgentSessionStore` | `Microsoft.Agents.AI.Hosting` | Never persists; `GetSessionAsync` always returns a new session |

### `InMemoryAgentSessionStore`

```csharp
// File: dotnet/src/Microsoft.Agents.AI.Hosting/Local/InMemoryAgentSessionStore.cs
public sealed class InMemoryAgentSessionStore : AgentSessionStore
{
    public override ValueTask SaveSessionAsync(AIAgent agent, string conversationId,
        AgentSession session, CancellationToken ct)
    {
        var key = GetKey(conversationId, agent.Id);
        _threads[key] = agent.SerializeSession(session);
        return default;
    }

    public override async ValueTask<AgentSession> GetSessionAsync(AIAgent agent,
        string conversationId, CancellationToken ct)
    {
        var key = GetKey(conversationId, agent.Id);
        JsonElement? content = _threads.TryGetValue(key, out var existing) ? existing : null;
        return content switch
        {
            null => await agent.CreateSessionAsync(ct),
            _    => await agent.DeserializeSessionAsync(content.Value, ct),
        };
    }
}
```

### AIHostAgent (hosting wrapper)

```csharp
// File: dotnet/src/Microsoft.Agents.AI.Hosting/AIHostAgent.cs
public class AIHostAgent : DelegatingAIAgent
{
    public AIHostAgent(AIAgent innerAgent, AgentSessionStore sessionStore);

    public ValueTask<AgentSession> GetOrCreateSessionAsync(
        string conversationId, CancellationToken ct);

    public ValueTask SaveSessionAsync(
        string conversationId, AgentSession session, CancellationToken ct);
}
```

### DI Registration

```csharp
// Extension methods in HostedAgentBuilderExtensions
builder.WithInMemorySessionStore();
builder.WithSessionStore(myStore);
builder.WithSessionStore((IServiceProvider sp, string agentName) => new MyStore(...));
```

---

## 12. AIAgent Base Class

**File**: `dotnet/src/Microsoft.Agents.AI.Abstractions/AIAgent.cs`
**Namespace**: `Microsoft.Agents.AI`

```csharp
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public abstract class AIAgent
{
    // ── Identity ────────────────────────────────────────────────
    public string? Id { get; }
    public string? Name { get; }
    public string? Description { get; }
    protected abstract string? IdCore { get; }

    // ── Session lifecycle ───────────────────────────────────────
    public ValueTask<AgentSession> CreateSessionAsync(CancellationToken ct);
    protected abstract ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken ct);

    public JsonElement SerializeSession(AgentSession session, JsonSerializerOptions? options = null);
    protected abstract JsonElement SerializeSessionCore(AgentSession session, JsonSerializerOptions? options = null);

    public ValueTask<AgentSession> DeserializeSessionAsync(JsonElement state, JsonSerializerOptions? options = null, CancellationToken ct = default);
    protected abstract ValueTask<AgentSession> DeserializeSessionCoreAsync(JsonElement state, JsonSerializerOptions? options = null, CancellationToken ct = default);

    // ── Run (non-streaming) ─────────────────────────────────────

    public Task<AgentResponse> RunAsync(AgentSession? session = null, AgentRunOptions? options = null, CancellationToken ct = default);
    public Task<AgentResponse> RunAsync(string message, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken ct = default);
    public Task<AgentResponse> RunAsync(ChatMessage message, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken ct = default);
    public Task<AgentResponse> RunAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken ct = default);

    protected abstract Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken ct = default);

    // ── RunStreaming ─────────────────────────────────────────────

    public IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken ct = default);

    public IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(
        string message,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken ct = default);

    public IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(
        ChatMessage message,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken ct = default);

    public async IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        AgentRunContext context = new(this, session, messages as IReadOnlyCollection<ChatMessage> ?? messages.ToList(), options);
        CurrentRunContext = context;
        await foreach (var update in RunCoreStreamingAsync(messages, session, options, ct))
        {
            yield return update;
            CurrentRunContext = context; // restore after caller resumes
        }
    }

    protected abstract IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken ct = default);

    // ── Context ─────────────────────────────────────────────────
    public static AgentRunContext? CurrentRunContext { get; }
    public virtual object? GetService(Type serviceType, object? serviceKey = null);
}
```

### Structured Output (`ChatClientAgent` only — partial class)

```csharp
public Task<ChatClientAgentResponse<T>> RunAsync<T>(
    string message, AgentSession? session = null,
    JsonSerializerOptions? serializerOptions = null,
    AgentRunOptions? options = null,
    bool? useJsonSchemaResponseFormat = null,
    CancellationToken ct = default);

// Also overloads for ChatMessage and IEnumerable<ChatMessage>
```

---

## 13. VectorStore Abstraction

Uses **`Microsoft.Extensions.VectorData`** (not a custom abstraction):

```csharp
// Key types from Microsoft.Extensions.VectorData
VectorStore                     // abstract base for vector store providers
VectorStoreCollection           // typed collection<TKey, TRecord>
VectorStoreCollectionDefinition // schema definition builder
VectorStoreKeyProperty          // marks a key field
VectorStoreDataProperty         // marks a data field (IsIndexed, IsFullTextIndexed)
VectorStoreVectorProperty       // marks a vector embedding field
```

### Available Implementations

The `AgentWithMemory_Step01` sample uses:

```csharp
using Microsoft.Extensions.VectorData;

var vectorStore = new InMemoryVectorStore(new InMemoryVectorStoreOptions
{
    EmbeddingGenerator = embeddingGenerator
});
```

---

## 14. Wiring Samples

### A. ChatHistoryProvider + IChatReducer

From `Agent_Step16_ChatReduction/Program.cs`:

```csharp
AIAgent agent = new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential())
    .GetChatClient(deploymentName)
    .AsAIAgent(new ChatClientAgentOptions
    {
        ChatOptions = new() { Instructions = "You are good at telling jokes." },
        Name = "Joker",
        ChatHistoryProviderFactory = (ctx, ct) =>
            new ValueTask<ChatHistoryProvider>(
                new InMemoryChatHistoryProvider(
                    new MessageCountingChatReducer(2),  // keep last 2 non-system messages
                    ctx.SerializedState,
                    ctx.JsonSerializerOptions))
    });

AgentSession session = await agent.CreateSessionAsync();
Console.WriteLine(await agent.RunAsync("Tell me a joke about a pirate.", session));

// Access chat history via session
IList<ChatMessage>? chatHistory = session.GetService<IList<ChatMessage>>();
Console.WriteLine($"Chat history has {chatHistory?.Count} messages.");
```

### B. ChatHistoryMemoryProvider (vector-backed semantic memory)

From `AgentWithMemory_Step01_ChatHistoryMemory/Program.cs`:

```csharp
var vectorStore = new InMemoryVectorStore(new InMemoryVectorStoreOptions
{
    EmbeddingGenerator = embeddingGenerator
});

AIAgent agent = chatClient.AsAIAgent(new ChatClientAgentOptions
{
    ChatOptions = new() { Instructions = "You are a helpful assistant." },
    AIContextProviderFactory = (ctx, ct) =>
        new ValueTask<AIContextProvider>(
            new ChatHistoryMemoryProvider(
                vectorStore,
                collectionName: "chathistory",
                vectorDimensions: 3072,
                storageScope: new ChatHistoryMemoryProviderScope
                {
                    ApplicationId = "myapp",
                    UserId = "user1"
                },
                searchScope: new ChatHistoryMemoryProviderScope
                {
                    ApplicationId = "myapp"  // search across all users
                }))
});
```

### C. Custom ChatHistoryProvider with VectorStore

From `Agent_Step07_3rdPartyChatHistoryStorage/Program.cs`:

```csharp
public class VectorChatHistoryProvider : ChatHistoryProvider
{
    private readonly VectorStore _vectorStore;
    // ... stores/retrieves messages as vector records
    // InvokingCoreAsync → vector similarity search for relevant history
    // InvokedCoreAsync → upsert new messages into vector store
}
```

### D. Session Persistence with AgentSessionStore

```csharp
// Via DI
builder.AddHostedAgent("myAgent", agentFactory)
    .WithInMemorySessionStore();

// Manual usage
var hostAgent = new AIHostAgent(innerAgent, new InMemoryAgentSessionStore());

// Get or create session
var session = await hostAgent.GetOrCreateSessionAsync("conv-123");

// Run
var response = await hostAgent.RunAsync("Hello", session);

// Save
await hostAgent.SaveSessionAsync("conv-123", session);
```

### E. Manual Session Serialization/Deserialization

```csharp
// Serialize
JsonElement serialized = agent.SerializeSession(session);

// Persist serialized to your store...

// Restore
AgentSession restored = await agent.DeserializeSessionAsync(serialized);

// Continue conversation
var response = await agent.RunAsync("Follow-up question", restored);
```

---

## CosmosChatHistoryProvider (Supplementary)

**Assembly**: `Microsoft.Agents.AI.CosmosNoSql`

```csharp
public sealed class CosmosChatHistoryProvider : ChatHistoryProvider, IDisposable
{
    // Constructors use connection string + hierarchical partition keys
    // (tenantId, userId, sessionId)

    public int? MaxMessagesToRetrieve { get; set; }
    // Default TTL: 86400 seconds (24 hours)

    public Task<int> GetMessageCountAsync(CancellationToken ct);
    public Task<int> ClearMessagesAsync(CancellationToken ct);
}
```

## Mem0Provider (Alternative AIContextProvider)

**Assembly**: `Microsoft.Agents.AI.Mem0`

```csharp
public sealed class Mem0Provider : AIContextProvider  // NOT ChatHistoryProvider
{
    // Stores messages as Mem0 memories, retrieves via semantic search
}

public sealed class Mem0ProviderScope
{
    public string? ApplicationId { get; set; }
    public string? AgentId { get; set; }
    public string? SessionId { get; set; }  // maps to "thread"
    public string? UserId { get; set; }
}
```
