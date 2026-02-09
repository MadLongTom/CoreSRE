# Microsoft Agent Framework — Comprehensive Source Code Analysis

> **Source**: `e:\CoreSRE\.reference\codes\agent-framework` (cloned repository)
> **Date**: 2026-02-09

---

## 1. Framework Overview

**Microsoft Agent Framework** is a comprehensive, multi-language framework (Python & .NET) for building, orchestrating, and deploying AI agents. It provides:

- **Single-agent creation**: Wrap any LLM provider (OpenAI, Azure OpenAI, Anthropic, etc.) into a unified `AIAgent` abstraction
- **Multi-agent orchestration**: Graph-based workflow engine with sequential, concurrent, handoff, and group-chat patterns
- **Protocol interoperability**: Native support for A2A (Agent-to-Agent) and AG-UI protocols
- **Hosting infrastructure**: ASP.NET Core integration, session persistence, Azure Functions support
- **Observability**: Built-in OpenTelemetry tracing and structured logging via decorator agents
- **Checkpointing & time-travel**: Durable workflow state with checkpoint/replay capabilities
- **Declarative agents**: YAML/JSON-driven agent definitions without code

The NuGet entry point is `Microsoft.Agents.AI`. The framework builds on `Microsoft.Extensions.AI` (`IChatClient`, `ChatMessage`, `AITool`, etc.) as its messaging substrate.

---

## 2. Core Abstractions

### 2.1 The Type Hierarchy at a Glance

```
AIAgent (abstract)                    ← root abstraction
├── ChatClientAgent                   ← wraps IChatClient (main concrete agent)
├── A2AAgent                          ← remote agent via A2A protocol
├── DelegatingAIAgent (abstract)      ← decorator pattern base
│   ├── LoggingAgent                  ← structured logging
│   ├── OpenTelemetryAgent            ← OTel semantic conventions
│   ├── FunctionInvocationDelegatingAgent ← function-call middleware
│   ├── AIHostAgent                   ← adds session persistence
│   └── AnonymousDelegatingAIAgent    ← inline lambda-based
└── WorkflowHostAgent (internal)      ← wraps a Workflow as an AIAgent

AgentSession (abstract)               ← conversation state container
├── InMemoryAgentSession
├── ChatClientAgentSession
├── A2AAgentSession
├── WorkflowSession
└── ServiceIdAgentSession

AgentResponse                         ← non-streaming response
AgentResponseUpdate                   ← streaming response chunk
AgentRunOptions                       ← run configuration
AgentRunContext                       ← in-flight run context (AsyncLocal)

ChatHistoryProvider (abstract)        ← chat history I/O
└── InMemoryChatHistoryProvider       ← in-memory IList<ChatMessage>

AIContextProvider (abstract)          ← dynamic context injection
AIContext                             ← transient context (instructions, tools, messages)
AIAgentMetadata                       ← provider name for telemetry

Executor (abstract)                   ← workflow node
├── FunctionExecutor<TInput>          ← lambda-based node
├── ChatForwardingExecutor            ← message splitter/forwarder
├── ChatProtocolExecutor              ← agent-hosting node
└── AggregatingExecutor               ← fan-in aggregation

Workflow                              ← executable graph definition
WorkflowBuilder                       ← fluent graph construction
AgentWorkflowBuilder                  ← high-level agent-composition patterns
```

---

## 3. dotnet/ Directory Structure

### 3.1 Source Projects (`dotnet/src/`)

| Project | Purpose | Key Dependencies |
|---------|---------|-----------------|
| **Microsoft.Agents.AI.Abstractions** | Core abstract types: `AIAgent`, `AgentSession`, `AgentResponse`, `ChatHistoryProvider`, `AIContextProvider`, `AIContext` | `Microsoft.Extensions.AI.Abstractions` |
| **Microsoft.Agents.AI** | Concrete agent types: `ChatClientAgent`, `AIAgentBuilder`, pipeline decorators (Logging, OTel, FunctionInvocation), `TextSearchProvider` | `.Abstractions` |
| **Microsoft.Agents.AI.OpenAI** | Extension methods to create agents from OpenAI SDK clients (`ResponsesClient.AsAIAgent()`, `ChatClient.AsAIAgent()`, `AssistantClient` support) | `.AI`, `OpenAI` SDK |
| **Microsoft.Agents.AI.AzureAI** | Azure AI Foundry–backed agents | `.AI`, Azure SDK |
| **Microsoft.Agents.AI.AzureAI.Persistent** | Azure AI agent with persistent server-side threads | `.AzureAI` |
| **Microsoft.Agents.AI.Anthropic** | Anthropic Claude provider integration | `.AI` |
| **Microsoft.Agents.AI.A2A** | `A2AAgent` — calls remote agents via A2A protocol | `.Abstractions`, `A2A` client lib |
| **Microsoft.Agents.AI.AGUI** | AG-UI protocol support (`AGUIChatClient`, `AGUIHttpService`) | `.AI` |
| **Microsoft.Agents.AI.Workflows** | Graph-based workflow engine: `Workflow`, `WorkflowBuilder`, `Executor`, edges, checkpointing, group chat, handoffs | `.Abstractions` |
| **Microsoft.Agents.AI.Workflows.Declarative** | Declarative (YAML/JSON) workflow definitions | `.Workflows` |
| **Microsoft.Agents.AI.Workflows.Declarative.AzureAI** | Azure AI extensions for declarative workflows | `.Workflows.Declarative` |
| **Microsoft.Agents.AI.Workflows.Generators** | Source generators for workflow code gen | `.Workflows` |
| **Microsoft.Agents.AI.Declarative** | Declarative agent factory (`PromptAgentFactory`, YAML bot element parsing) | `.AI` |
| **Microsoft.Agents.AI.Hosting** | ASP.NET hosting: `AIHostAgent`, `AgentSessionStore`, DI registration (`AddAIAgent`), `WorkflowCatalog` | `.Abstractions`, `Microsoft.Extensions.Hosting` |
| **Microsoft.Agents.AI.Hosting.A2A** | A2A hosting bridge (converts `AIAgent` → A2A `ITaskManager`) | `.Hosting`, `.A2A` |
| **Microsoft.Agents.AI.Hosting.A2A.AspNetCore** | `MapA2A()` endpoint routing for ASP.NET Core | `.Hosting.A2A` |
| **Microsoft.Agents.AI.Hosting.AGUI.AspNetCore** | AG-UI endpoint routing for ASP.NET Core | `.AGUI` |
| **Microsoft.Agents.AI.Hosting.AzureFunctions** | Azure Functions trigger integration | `.Hosting` |
| **Microsoft.Agents.AI.Hosting.OpenAI** | Host agents as OpenAI-compatible endpoints | `.Hosting` |
| **Microsoft.Agents.AI.DevUI** | Interactive developer UI for testing/debugging | `.AI` |
| **Microsoft.Agents.AI.CopilotStudio** | Copilot Studio integration | `.AI` |
| **Microsoft.Agents.AI.GitHub.Copilot** | GitHub Copilot integration | `.AI` |
| **Microsoft.Agents.AI.Mem0** | Mem0 memory provider | `.AI` |
| **Microsoft.Agents.AI.Purview** | Microsoft Purview data governance | `.AI` |
| **Microsoft.Agents.AI.CosmosNoSql** | Cosmos DB chat history / session storage | `.AI` |
| **Microsoft.Agents.AI.DurableTask** | Durable Task Framework integration for workflows | `.Workflows` |
| **LegacySupport/** | Backward compatibility with prior Bot Framework | — |

### 3.2 Dependency Flow

```
Abstractions
    ↑
    AI  ←── OpenAI / AzureAI / Anthropic / Declarative
    ↑
  Workflows  ←── Workflows.Declarative
    ↑
  Hosting  ←── Hosting.A2A ←── Hosting.A2A.AspNetCore
             ←── Hosting.AGUI.AspNetCore
             ←── Hosting.AzureFunctions
```

---

## 4. Key .NET Types — Detailed API Surface

### 4.1 `AIAgent` (abstract base class)

```csharp
namespace Microsoft.Agents.AI;

public abstract class AIAgent
{
    // Identity
    string Id { get; }                       // GUID by default, overridable via IdCore
    virtual string? Name { get; }
    virtual string? Description { get; }

    // Static context (AsyncLocal)
    static AgentRunContext? CurrentRunContext { get; protected set; }

    // Service locator pattern
    virtual object? GetService(Type serviceType, object? serviceKey = null);
    TService? GetService<TService>(object? serviceKey = null);

    // Session lifecycle
    ValueTask<AgentSession> CreateSessionAsync(CancellationToken ct);
    JsonElement SerializeSession(AgentSession session, JsonSerializerOptions? options);
    ValueTask<AgentSession> DeserializeSessionAsync(JsonElement state, ...);

    // Non-streaming execution (multiple overloads)
    Task<AgentResponse> RunAsync(AgentSession? session, AgentRunOptions? options, CancellationToken ct);
    Task<AgentResponse> RunAsync(string message, ...);
    Task<AgentResponse> RunAsync(ChatMessage message, ...);
    Task<AgentResponse> RunAsync(IEnumerable<ChatMessage> messages, ...);

    // Streaming execution (multiple overloads)
    IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(string message, ...);
    IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(IEnumerable<ChatMessage> messages, ...);

    // Override points for implementors
    protected abstract ValueTask<AgentSession> CreateSessionCoreAsync(...);
    protected abstract Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, ...);
    protected abstract IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(...);
    protected abstract JsonElement SerializeSessionCore(...);
    protected abstract ValueTask<AgentSession> DeserializeSessionCoreAsync(...);
}
```

### 4.2 `AgentResponse`

```csharp
public class AgentResponse
{
    IList<ChatMessage> Messages { get; set; }    // Response messages
    string? Text { get; }                         // Concatenated text from all messages
    string? AgentId { get; set; }
    string? AgentName { get; set; }
    string? ResponseId { get; set; }
    DateTimeOffset? CreatedAt { get; set; }
    UsageDetails? Usage { get; set; }
    object? RawRepresentation { get; set; }
    ResponseContinuationToken? ContinuationToken { get; set; }
    AdditionalPropertiesDictionary? AdditionalProperties { get; set; }
}
```

### 4.3 `AgentSession` (abstract)

```csharp
public abstract class AgentSession
{
    object? GetService(Type serviceType, object? serviceKey = null);
    TService? GetService<TService>(object? serviceKey = null);
}
```

Concrete sessions carry state for their domain (e.g. `ChatClientAgentSession` holds a `ChatHistoryProvider` + `AIContextProvider`; `A2AAgentSession` holds a `ContextId` and `TaskId`; `WorkflowSession` holds the run state).

### 4.4 `ChatClientAgent` (the primary concrete agent)

```csharp
public sealed class ChatClientAgent : AIAgent
{
    ChatClientAgent(IChatClient chatClient, string? instructions, string? name, ...);
    ChatClientAgent(IChatClient chatClient, ChatClientAgentOptions? options, ...);

    IChatClient ChatClient { get; }   // The underlying (possibly decorated) chat client
}
```

Options include `ChatHistoryProviderFactory`, `AIContextProviderFactory`, `ChatOptions` (instructions, tools), `UseProvidedChatClientAsIs`.

### 4.5 `DelegatingAIAgent` (decorator pattern)

```csharp
public abstract class DelegatingAIAgent : AIAgent
{
    protected DelegatingAIAgent(AIAgent innerAgent);
    protected AIAgent InnerAgent { get; }
    // Transparent pass-through of all operations to InnerAgent
}
```

### 4.6 `AIAgentBuilder` (pipeline construction)

```csharp
public sealed class AIAgentBuilder
{
    AIAgentBuilder(AIAgent innerAgent);
    AIAgentBuilder Use(Func<AIAgent, AIAgent> factory);       // Add decorating layer
    AIAgentBuilder Use(Func<AIAgent, IServiceProvider, AIAgent> factory);
    AIAgent Build(IServiceProvider? services = null);
}
```

### 4.7 `ChatHistoryProvider` / `AIContextProvider`

```csharp
public abstract class ChatHistoryProvider
{
    abstract ValueTask<IEnumerable<ChatMessage>> InvokingAsync(InvokingContext ctx, CancellationToken ct);
    abstract ValueTask InvokedAsync(InvokedContext ctx, CancellationToken ct);
    abstract JsonElement Serialize(JsonSerializerOptions? options);
}

public abstract class AIContextProvider
{
    abstract ValueTask<AIContext> InvokingAsync(InvokingContext ctx, CancellationToken ct);
    virtual ValueTask InvokedAsync(InvokedContext ctx, CancellationToken ct);
    virtual JsonElement Serialize(JsonSerializerOptions? options);
}
```

### 4.8 `AIContext`

```csharp
public sealed class AIContext
{
    string? Instructions { get; set; }       // Transient additional instructions
    IList<ChatMessage>? Messages { get; set; }  // Permanent additions to history
    IList<AITool>? Tools { get; set; }       // Transient additional tools
}
```

---

## 5. Agent Lifecycle

### 5.1 Creation

```csharp
// Option A: Direct from OpenAI client (extension method)
var agent = openAIClient
    .GetOpenAIResponseClient("gpt-4o-mini")
    .AsAIAgent(name: "Bot", instructions: "You are helpful.");

// Option B: Manual ChatClientAgent construction
var agent = new ChatClientAgent(chatClient, options);

// Option C: Builder pipeline
var agent = new AIAgentBuilder(baseAgent)
    .Use(inner => new LoggingAgent(inner, logger))
    .Use(inner => new OpenTelemetryAgent(inner))
    .Build();

// Option D: Hosted via DI (ASP.NET)
builder.AddAIAgent("MyAgent", "You are helpful.");
```

### 5.2 Session Management

```csharp
// Create a session (conversation)
var session = await agent.CreateSessionAsync();

// Run the agent in the session
var response = await agent.RunAsync("Hello!", session);

// Persist session across requests
var json = agent.SerializeSession(session);
// ... store json ...
var restored = await agent.DeserializeSessionAsync(json);
```

### 5.3 Hosting Lifecycle

In hosted scenarios, `AIHostAgent` wraps an agent with `AgentSessionStore` for automatic session persistence:

```csharp
// Server creates or restores session per conversation ID
var session = await hostAgent.GetOrCreateSessionAsync(conversationId);
var response = await hostAgent.RunAsync(userMessage, session);
await hostAgent.SaveSessionAsync(conversationId, session);
```

### 5.4 Agent is Stateless; Session Carries State

A critical design point: `AIAgent` instances are **stateless and reentrant**. All conversation state lives in `AgentSession`. A single agent instance can serve multiple concurrent conversations via different sessions.

---

## 6. Message Passing

### 6.1 Message Types

The framework uses `Microsoft.Extensions.AI.ChatMessage` as the universal message type:

```csharp
public class ChatMessage
{
    ChatRole Role { get; set; }            // User, Assistant, System, Tool
    IList<AIContent> Contents { get; set; } // Text, Image, FunctionCall, FunctionResult, etc.
    string? AuthorName { get; set; }
}
```

### 6.2 Agent Communication Patterns

| Pattern | Mechanism |
|---------|-----------|
| **Direct invocation** | `agent.RunAsync(messages, session)` |
| **Agent-as-function-tool** | `agent.AsAIFunction()` → another agent calls it via tool calling |
| **Sequential workflow** | Workflow edges route output of one agent as input to next |
| **Concurrent workflow** | Fan-out edges send same input to multiple agents in parallel |
| **Group chat** | `GroupChatManager` selects next agent; all share a conversation history |
| **Handoffs** | Handoff functions injected as tools; agent calls `handoff_to_<id>()` |
| **A2A remote** | `A2AAgent` sends messages to remote agents via HTTP A2A protocol |

### 6.3 Workflow Message Passing (Internal)

Within a workflow, executors communicate via **typed messages routed along edges**:

```csharp
// Inside an Executor:
await context.SendMessageAsync(myMessage, targetId: null);  // broadcast to connected executors
await context.YieldOutputAsync(result);                      // emit workflow output
```

Edge types: `Direct`, `FanOut`, `FanIn`. Conditional edges filter by message type or predicate.

---

## 7. Workflow / Orchestration

### 7.1 Core Concepts

- **`Workflow`** — An immutable graph of `Executor` nodes connected by `Edge` objects
- **`Executor`** — A processing node that receives and sends typed messages
- **`WorkflowBuilder`** — Fluent API to define the graph
- **`SuperStep`** — One round of message propagation; all pending messages are delivered, executors run, then new messages are collected for the next super-step
- **`Run` / `StreamingRun`** — An executing workflow instance, yielding `WorkflowEvent` objects

### 7.2 High-Level Builder Patterns

```csharp
// Sequential pipeline
var workflow = AgentWorkflowBuilder.BuildSequential(agent1, agent2, agent3);

// Concurrent fan-out
var workflow = AgentWorkflowBuilder.BuildConcurrent([agent1, agent2], aggregator);

// Handoffs (agent decides which agent to transfer to)
var workflow = AgentWorkflowBuilder.BuildHandoffs(triageAgent)
    .WithHandoff(triageAgent, salesAgent)
    .WithHandoff(triageAgent, supportAgent)
    .WithHandoff(salesAgent, triageAgent)
    .Build();

// Group chat (round-robin, LLM-selected, or custom manager)
var workflow = AgentWorkflowBuilder
    .BuildGroupChat(agents => new RoundRobinGroupChatManager(agents) { MaximumIterationCount = 10 })
    .AddParticipants(agent1, agent2, agent3)
    .Build();
```

### 7.3 Low-Level Builder (Custom Graphs)

```csharp
var nodeA = new MyCustomExecutor("A");
var nodeB = new FunctionExecutor<string>("B", async (input, ctx, ct) => {
    await ctx.SendMessageAsync(input.ToUpper());
});

var workflow = new WorkflowBuilder(nodeA)
    .AddEdge(nodeA, nodeB)
    .AddEdge(nodeB, nodeA, condition: msg => msg is not string s || s.Length < 100)
    .WithOutputFrom(nodeB)
    .Build();
```

### 7.4 Workflow → Agent Bridge

Any `Workflow` can be used as an `AIAgent`:

```csharp
AIAgent workflowAgent = workflow.AsAgent(name: "MyWorkflow");
var response = await workflowAgent.RunAsync("Hello!");
```

### 7.5 Checkpointing & Time-Travel

```csharp
var checkpointStore = new FileSystemJsonCheckpointStore("./checkpoints");
var checkpointManager = new CheckpointManager(checkpointStore);

AIAgent agent = workflow.AsAgent(checkpointManager: checkpointManager);
```

Checkpoints capture the full workflow state (executor states, in-flight messages) allowing:
- Resume from failure
- Time-travel debugging (replay from any checkpoint)
- Human-in-the-loop (pause workflow, wait for external input, resume)

### 7.6 Execution Environments

```csharp
InProcessExecution.OffThread   // Default — runs on thread-pool
InProcessExecution.Concurrent  // Allows concurrent runs on same workflow
InProcessExecution.Lockstep    // Deterministic single-threaded execution
```

The `IWorkflowExecutionEnvironment` interface allows custom execution environments (e.g., distributed via Durable Tasks).

---

## 8. A2A Integration

### 8.1 Client Side — `A2AAgent`

`A2AAgent` extends `AIAgent` and wraps an `A2AClient` to call remote agents:

```csharp
var a2aClient = new A2AClient(httpClient, agentCardUri);
var remoteAgent = new A2AAgent(a2aClient, name: "RemoteBot");

// Use like any other agent
var response = await remoteAgent.RunAsync("What is the weather?");
```

Supports:
- **Message-based responses** — synchronous request/response
- **Task-based responses** — long-running tasks with continuation tokens for polling
- **Streaming** — SSE-based streaming via `RunStreamingAsync`
- **Context persistence** — `A2AAgentSession` tracks `ContextId` and `TaskId`

### 8.2 Server Side — Hosting an Agent via A2A

```csharp
// In ASP.NET Core Program.cs:
var agentBuilder = builder.AddAIAgent("MyAgent", "You are a helpful assistant.");

var app = builder.Build();
app.MapA2A(agentBuilder, "/a2a");   // Exposes agent as A2A-compliant endpoint
```

`MapA2A()` sets up the full A2A protocol endpoint group (agent card, message send, task polling, SSE streaming).

### 8.3 A2A Agent as Function Tool

Remote agents can be composed as function tools for orchestrating agents:

```csharp
var localAgent = new ChatClientAgent(chatClient, tools: [
    remoteA2AAgent.AsAIFunction()
]);
```

---

## 9. MCP Integration

The framework supports **Model Context Protocol (MCP)** through its tool system. The samples directory contains:

| Sample | Description |
|--------|-------------|
| `Agent_MCP_Server/` | Exposing an agent as an MCP tool server |
| `Agent_MCP_Server_Auth/` | MCP tool server with authentication |
| `ResponseAgent_Hosted_MCP/` | Hosted agent with MCP tools |
| `FoundryAgent_Hosted_MCP/` | Foundry agent with MCP tools |

The integration works through `Microsoft.Extensions.AI`'s `AITool` / `AIFunction` system. MCP tools are registered as `AITool` instances in the agent's `ChatOptions.Tools` collection. The `Agent_Step10_AsMcpTool` sample shows exposing an agent itself as an MCP-callable tool.

---

## 10. Extensibility Points

### 10.1 Custom Agent (extend `AIAgent`)

Override the abstract methods to create a completely custom agent backed by any service:
- `RunCoreAsync(...)` — non-streaming execution
- `RunCoreStreamingAsync(...)` — streaming execution
- `CreateSessionCoreAsync(...)` — session factory
- `SerializeSessionCore(...)` / `DeserializeSessionCoreAsync(...)` — session persistence

### 10.2 Decorator Pipeline (extend `DelegatingAIAgent`)

Wrap any agent with cross-cutting concerns:
```csharp
public class MyMiddleware : DelegatingAIAgent
{
    protected override async Task<AgentResponse> RunCoreAsync(...)
    {
        // Pre-processing
        var response = await InnerAgent.RunAsync(messages, session, options, ct);
        // Post-processing
        return response;
    }
}
```

Built-in decorators: `LoggingAgent`, `OpenTelemetryAgent`, `FunctionInvocationDelegatingAgent`, `AIHostAgent`.

### 10.3 Builder Pipeline (`AIAgentBuilder`)

```csharp
agent.AsBuilder()
    .Use(inner => new LoggingAgent(inner, logger))
    .Use(inner => new OpenTelemetryAgent(inner))
    .Use((inner, sp) => new MyCustomDecorator(inner, sp.GetService<IFoo>()))
    .Build();
```

### 10.4 Custom `ChatHistoryProvider`

Replace in-memory storage with database-backed, summarizing, or windowed implementations:
```csharp
public class DatabaseChatHistory : ChatHistoryProvider
{
    public override ValueTask<IEnumerable<ChatMessage>> InvokingAsync(...) { /* load from DB */ }
    public override ValueTask InvokedAsync(...) { /* save to DB */ }
}
```

### 10.5 Custom `AIContextProvider`

Inject dynamic context (RAG results, user preferences, etc.) into each agent invocation:
```csharp
public class RagContextProvider : AIContextProvider
{
    public override async ValueTask<AIContext> InvokingAsync(InvokingContext ctx, ...)
    {
        var docs = await searchIndex.QueryAsync(ctx.RequestMessages.Last().Text);
        return new AIContext { Instructions = $"Relevant docs:\n{docs}" };
    }
}
```

### 10.6 Custom Workflow `Executor`

Create domain-specific workflow nodes:
```csharp
public class MyExecutor : Executor
{
    protected override RouteBuilder ConfigureRoutes(RouteBuilder rb)
        => rb.AddHandler<MyInput>(HandleAsync);

    private async ValueTask HandleAsync(MyInput msg, IWorkflowContext ctx, CancellationToken ct)
    {
        var result = Process(msg);
        await ctx.SendMessageAsync(result, ct);
    }
}
```

### 10.7 Custom `GroupChatManager`

Control agent selection in group chats (LLM-based selection, priority queues, etc.):
```csharp
public class LlmGroupChatManager : GroupChatManager
{
    protected internal override async ValueTask<AIAgent> SelectNextAgentAsync(...)
    {
        // Use LLM to decide which agent should respond next
    }
}
```

### 10.8 Custom `AgentSessionStore`

Implement persistent session storage (Redis, SQL, Cosmos DB, etc.):
```csharp
public class RedisSessionStore : AgentSessionStore
{
    public override ValueTask SaveSessionAsync(AIAgent agent, string conversationId, AgentSession session, ...) { ... }
    public override ValueTask<AgentSession> GetSessionAsync(AIAgent agent, string conversationId, ...) { ... }
}
```

### 10.9 Custom `ICheckpointStore<T>`

Persist workflow checkpoints to durable storage:
```csharp
public class SqlCheckpointStore : ICheckpointStore<byte[]> { ... }
```

### 10.10 Custom `IWorkflowExecutionEnvironment`

Run workflows in distributed environments (Azure Durable Tasks, Orleans, etc.).

---

## 11. Sample Patterns

### 11.1 Agent Samples (`dotnet/samples/GettingStarted/Agents/`)

| Step | Pattern |
|------|---------|
| Step01 | Basic agent running |
| Step02 | Multi-turn conversation with sessions |
| Step03 | Function tools (AI tools) |
| Step04 | Function tools with human approval |
| Step05 | Structured output (typed responses) |
| Step06 | Persisted conversations (session serialization) |
| Step07 | 3rd-party chat history storage |
| Step08 | Observability (OpenTelemetry) |
| Step09 | Dependency injection |
| Step10 | Agent as MCP tool |
| Step11 | Using images in conversations |
| Step12 | Agent as function tool for other agents |
| Step13 | Background responses with tools + persistence |
| Step14 | Middleware (custom `DelegatingAIAgent`) |
| Step15 | Plugins |
| Step16 | Chat reduction (summarization/truncation) |
| Step17 | Background responses (async polling) |
| Step18 | Deep research (complex multi-step agents) |
| Step19 | Declarative agents (YAML-defined) |
| Step20 | Additional AI context (dynamic context injection) |

### 11.2 Workflow Samples (`dotnet/samples/GettingStarted/Workflows/`)

| Directory | Pattern |
|-----------|---------|
| `Agents/` | Agent-composed workflows |
| `_Foundational/` | Raw `Executor` + `WorkflowBuilder` patterns |
| `ConditionalEdges/` | Conditional routing between executors |
| `Loop/` | Looping/iterative workflows |
| `SharedStates/` | Shared state across executors |
| `HumanInTheLoop/` | External request ports for human approval |
| `Concurrent/` | Fan-out/fan-in parallel execution |
| `Checkpoint/` | Checkpointing and replay |
| `Visualization/` | Workflow graph visualization |
| `Observability/` | Workflow-level telemetry |
| `Declarative/` | YAML-defined workflows |

### 11.3 Protocol Samples

| Directory | Pattern |
|-----------|---------|
| `A2A/A2AAgent_AsFunctionTools/` | Remote A2A agents used as function tools |
| `A2A/A2AAgent_PollingForTaskCompletion/` | Long-running A2A task polling |
| `AGUI/` | AG-UI protocol client/server |
| `ModelContextProtocol/` | MCP tool server exposure |

### 11.4 Provider Samples (`AgentProviders/`)

Shows using different LLM backends: Azure OpenAI, plain OpenAI, Anthropic, Azure AI Foundry agents.

### 11.5 Hosted Samples (`HostedAgents/`, `A2AClientServer/`)

Full ASP.NET Core server hosting agents with session persistence and protocol endpoints.

---

## Summary Table: Key Namespace → Type Mapping

| Namespace | Key Types |
|-----------|-----------|
| `Microsoft.Agents.AI` | `AIAgent`, `AgentSession`, `AgentResponse`, `AgentResponseUpdate`, `AgentRunOptions`, `AgentRunContext`, `AIContext`, `AIContextProvider`, `ChatHistoryProvider`, `InMemoryChatHistoryProvider`, `AIAgentMetadata`, `DelegatingAIAgent`, `ChatClientAgent`, `AIAgentBuilder`, `LoggingAgent`, `OpenTelemetryAgent` |
| `Microsoft.Agents.AI.A2A` | `A2AAgent`, `A2AAgentSession` |
| `Microsoft.Agents.AI.Workflows` | `Workflow`, `WorkflowBuilder`, `AgentWorkflowBuilder`, `Executor`, `FunctionExecutor<T>`, `ChatForwardingExecutor`, `Edge`, `EdgeKind`, `Run`, `StreamingRun`, `IWorkflowContext`, `GroupChatManager`, `RoundRobinGroupChatManager`, `HandoffsWorkflowBuilder`, `GroupChatWorkflowBuilder`, `WorkflowHostAgent`, `CheckpointManager`, `ExecutorBinding` |
| `Microsoft.Agents.AI.Hosting` | `AIHostAgent`, `AgentSessionStore`, `IHostedAgentBuilder`, `IHostedWorkflowBuilder`, `WorkflowCatalog` |
| `Microsoft.Agents.AI.Hosting.A2A` | A2A-to-AIAgent bridge converters |
| `Microsoft.AspNetCore.Builder` | `MapA2A()` extension methods |

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────┐
│                    Consumer Code                         │
│  agent.RunAsync("Hello")  /  workflow.AsAgent().RunAsync │
└───────────────┬──────────────────────┬──────────────────┘
                │                      │
    ┌───────────▼───────────┐  ┌───────▼─────────────┐
    │   AIAgent Pipeline    │  │   Workflow Engine    │
    │ ┌───────────────────┐ │  │ ┌─────────────────┐ │
    │ │ OpenTelemetryAgent│ │  │ │  WorkflowBuilder │ │
    │ │ LoggingAgent      │ │  │ │  Executor graph  │ │
    │ │ FuncInvocation    │ │  │ │  SuperStep loop  │ │
    │ │ ChatClientAgent   │ │  │ │  Checkpointing   │ │
    │ └───────────────────┘ │  │ └─────────────────┘ │
    └───────────┬───────────┘  └──────────┬──────────┘
                │                         │
    ┌───────────▼─────────────────────────▼──────────┐
    │              Microsoft.Extensions.AI            │
    │  IChatClient  │  ChatMessage  │  AITool/AIFunc  │
    └───────────────────────┬────────────────────────┘
                            │
    ┌───────────────────────▼────────────────────────┐
    │           LLM Provider SDKs                     │
    │  OpenAI  │  Azure OpenAI  │  Anthropic  │  ... │
    └────────────────────────────────────────────────┘
```

---

## Key Design Principles

1. **`IChatClient` is the universal LLM abstraction** — Agents are thin wrappers around `IChatClient` from `Microsoft.Extensions.AI`
2. **Decorator pattern for cross-cutting concerns** — `DelegatingAIAgent` enables composable middleware
3. **Session-based state isolation** — Agents are stateless; sessions carry all conversation state
4. **Graph-based orchestration** — Workflows are DAGs of typed `Executor` nodes with edge-based message routing
5. **Protocol-first interop** — Native A2A and AG-UI support for agent-to-agent communication across runtimes
6. **Everything is an `AIAgent`** — Workflows, remote agents, and decorators all present the same `AIAgent` interface
