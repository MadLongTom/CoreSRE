# Research: ChatClient 工具绑定与对话调用

**Date**: 2026-02-11 | **Branch**: `010-chatclient-tool-binding`

## R1 — AIFunction Creation Strategy

### Decision: Subclass `AIFunction` with custom delegate

### Rationale
`AIFunctionFactory.Create()` is reflection-based — it auto-generates JSON schema from .NET delegate parameter types. There is **no factory method** that accepts a raw JSON schema + custom invoke delegate. `AIFunctionFactory.CreateDeclaration()` accepts raw JSON schema but produces a **declaration-only** `AIFunction` that is **not invocable** — it silently terminates the function-calling loop.

The correct approach is to create a custom `ToolRegistrationAIFunction` subclass that overrides:
- `Name` → tool name
- `Description` → tool description  
- `JsonSchema` → the tool's input schema (from `ToolSchemaVO.InputSchema` or `McpToolItem.InputSchema`)
- `InvokeCoreAsync()` → delegates to `IToolInvoker.InvokeAsync()`

### Alternatives Considered
1. **`AIFunctionFactory.Create(Delegate)`**: Rejected — requires compile-time delegate with typed parameters; our tool schemas are dynamic JSON.
2. **`AIFunctionFactory.CreateDeclaration()`**: Rejected — creates non-invocable declarations that break the function-calling loop.
3. **MCP SDK `McpClientTool` as `AIFunction`**: The `ModelContextProtocol` SDK provides `McpClientTool` which IS an `AIFunction`, but requires maintaining an active `McpClient` session. Our architecture creates per-invocation connections. Uniform subclass approach is simpler.

### Schema Handling
- **REST API tools**: `ToolSchemaVO.InputSchema` is `string?` (serialized JSON string) → parse to `JsonElement`
- **MCP sub-tools**: `McpToolItem.InputSchema` is `JsonElement?` (already parsed) → use directly
- If neither has schema → provide empty object schema `{}` to allow LLM to call without parameters

---

## R2 — FunctionInvokingChatClient Pipeline

### Decision: Use `ChatClientBuilder.UseFunctionInvocation()` to wrap `IChatClient`

### Rationale
The correct class name is **`FunctionInvokingChatClient`** (not `FunctionInvocationChatClient` as stated in the spec). It's a delegating handler that wraps an inner `IChatClient`.

**Wiring pattern**:
```csharp
innerChatClient
    .AsBuilder()
    .UseFunctionInvocation(options => {
        options.MaximumIterationsPerRequest = 10;
        options.AllowConcurrentInvocation = false;
    })
    .Build()
```

Tools are passed via `ChatOptions.Tools` at call time:
```csharp
var chatOptions = new ChatOptions { Tools = aiFunctions.Cast<AITool>().ToList() };
```

### Configuration
| Option | Default | Our Setting | Reason |
|--------|---------|-------------|--------|
| `MaximumIterationsPerRequest` | 40 | 10 | Per spec assumption; prevents runaway loops |
| `AllowConcurrentInvocation` | `false` | `false` | Sequential for simpler AG-UI event ordering |
| `IncludeDetailedErrors` | `false` | `true` | Better debugging in dev |
| `MaximumConsecutiveErrorsPerRequest` | 3 | 3 | Default is fine |

### Alternatives Considered
1. **Manual function-calling loop**: Rejected — `FunctionInvokingChatClient` handles loop automatically.
2. **`ChatClientAgent` with tools**: `ChatClientAgent` doesn't directly expose tool configuration.

---

## R3 — Streaming with Function Calls

### Decision: Detect `FunctionCallContent` and `FunctionResultContent` in the streaming output to emit AG-UI events

### Rationale
When `FunctionInvokingChatClient` processes a streaming response:
1. **Text tokens yield immediately** — become `TEXT_MESSAGE_CONTENT` SSE events (existing)
2. **`FunctionCallContent` yields immediately** — LLM's decision to call a function. Emit `TOOL_CALL_START` + `TOOL_CALL_ARGS`.
3. **Function execution happens between iterations** — after inner stream completes.
4. **`FunctionResultContent` yields after execution** — results added to conversation. Emit `TOOL_CALL_END`.

**Key Insight**: `FunctionInvokingChatClient` surfaces function call/result content to the outer streaming consumer. We enhance the existing streaming loop in `AgentChatEndpoints` to check `update.Contents` for `FunctionCallContent` and `FunctionResultContent` in addition to `TextContent`.

### Alternatives Considered
1. **Custom IChatClient middleware**: Rejected — adds unnecessary complexity.
2. **Event hooks on `FunctionInvokingChatClient`**: No such API exists.

---

## R4 — AG-UI Tool Call Events

### Decision: Emit `TOOL_CALL_START`, `TOOL_CALL_ARGS`, `TOOL_CALL_END` as SSE events per AG-UI protocol

### Event Specifications

| Event | Fields | When Emitted |
|-------|--------|--------------|
| `TOOL_CALL_START` | `toolCallId`, `toolCallName`, `parentMessageId` | When `FunctionCallContent` detected |
| `TOOL_CALL_ARGS` | `toolCallId`, `delta` (full args JSON) | Immediately after START |
| `TOOL_CALL_END` | `toolCallId` | When `FunctionResultContent` received |

### SSE Wire Format
```
data: {"type":"TOOL_CALL_START","toolCallId":"call_abc123","toolCallName":"get_weather","parentMessageId":"msg_1"}

data: {"type":"TOOL_CALL_ARGS","toolCallId":"call_abc123","delta":"{\"location\":\"Seattle\"}"}

data: {"type":"TOOL_CALL_END","toolCallId":"call_abc123"}
```

### Frontend Types (`@ag-ui/core`)
- `ToolCallStartEvent` — `{ type: "TOOL_CALL_START", toolCallId, toolCallName, parentMessageId }`
- `ToolCallArgsEvent` — `{ type: "TOOL_CALL_ARGS", toolCallId, delta }`
- `ToolCallEndEvent` — `{ type: "TOOL_CALL_END", toolCallId }`

### Ordering Constraints
- `TOOL_CALL_START` must precede `TOOL_CALL_ARGS` and `TOOL_CALL_END` for the same `toolCallId`
- Multiple tool calls can be interleaved (each tracked by unique `toolCallId`)
- Text message events and tool call events can interleave within a single run

### Alternatives Considered
1. **`TOOL_CALL_CHUNK`**: CopilotKit-specific convenience event — not standardized enough.
2. **`TOOL_CALL_RESULT` as separate event**: Unnecessary since `FunctionInvokingChatClient` feeds results back to LLM internally.

---

## R5 — Repository Batch Fetch

### Decision: Add `GetByIdsAsync()` to both `IToolRegistrationRepository` and `IMcpToolItemRepository`

### Rationale
`LlmConfig.ToolRefs` stores a `List<Guid>` mixing `ToolRegistration.Id` (REST API) and `McpToolItem.Id` (MCP sub-tools). Neither repository has batch-fetch.

**Resolution strategy**:
1. Take `ToolRefs` list
2. Query both repositories with `GetByIdsAsync(toolRefs)` in parallel
3. Each repository returns only matching IDs from its table
4. For `McpToolItem` matches: also load parent `ToolRegistration` for connection/auth config
5. Skip unmatched IDs (deleted tools — per edge case spec)

**New methods**:
- `IToolRegistrationRepository.GetByIdsAsync(IEnumerable<Guid> ids)` → `Task<IEnumerable<ToolRegistration>>`
- `IMcpToolItemRepository.GetByIdsAsync(IEnumerable<Guid> ids)` → `Task<IEnumerable<McpToolItem>>`

### Alternatives Considered
1. **Sequential `GetByIdAsync` per ID**: Rejected — O(n) DB roundtrips.
2. **Store tool type alongside ID**: Rejected — requires `LlmConfigVO` schema migration.

---

## R6 — Tool-to-AIFunction Conversion Service

### Decision: Create `IToolFunctionFactory` in Application, `ToolFunctionFactory` in Infrastructure

### Rationale
Encapsulates converting tool bindings to MEAI `AIFunction` objects. Crosses domain boundaries (reads entities, creates MEAI abstractions). Depends on `IToolInvokerFactory` (Infrastructure concern).

**Interface**:
```csharp
// Application/Interfaces/IToolFunctionFactory.cs
public interface IToolFunctionFactory
{
    Task<IReadOnlyList<AIFunction>> CreateFunctionsAsync(
        IReadOnlyList<Guid> toolRefs, 
        CancellationToken cancellationToken = default);
}
```

**Implementation responsibilities**:
1. Load tools from both repos via `GetByIdsAsync()`
2. For REST API `ToolRegistration`: create `ToolRegistrationAIFunction` wrapping `RestApiToolInvoker`
3. For `McpToolItem`: load parent `ToolRegistration`, create `AIFunction` wrapping `McpToolInvoker`
4. Skip deleted/not-found IDs (log warning)
5. Return `IReadOnlyList<AIFunction>`

### Alternatives Considered
1. **Inline in `AgentResolverService`**: Rejected — violates SRP.
2. **Domain service**: Rejected — depends on Infrastructure concerns.

---

## R7 — Schema Handling Unification

### Decision: Normalize both schema representations to `JsonElement` for `AIFunction.JsonSchema`

### Rationale
Two representations:
- `ToolSchemaVO.InputSchema`: `string?` (serialized JSON) — REST API tools
- `McpToolItem.InputSchema`: `JsonElement?` (parsed JSON) — MCP sub-tools

`AIFunction.JsonSchema` is `virtual JsonElement? JsonSchema { get; }`.

**Conversion**:
- REST API: `JsonDocument.Parse(inputSchemaString).RootElement.Clone()`
- MCP: use `McpToolItem.InputSchema` directly
- Null/missing: return `null` (MEAI handles absent schemas — LLM can still call without params)

---

## R8 — Frontend Tool Picker Pattern

### Decision: Combobox multi-select with search, following shadcn `Command` pattern

### Rationale
Existing `ProviderModelSelect.tsx` uses cascading single-select. For tools we need:
- **Multi-select** (bind multiple tools)
- **Search/filter** (50+ tools scale)
- **Grouped by type** (REST API / MCP)
- **Removable badges** for selected items

Use shadcn `Popover` + `Command` (ComboboxMulti) pattern:
1. Button click → open Popover
2. Fetch tools: `getTools()` returns REST API tools; `getMcpTools(toolId)` returns MCP sub-tools
3. `Command` provides built-in search filtering
4. Checkbox items for multi-select
5. Selected items render as `Badge` tags with `X` remove

**API Integration**: Need a new endpoint or expand `getTools()` to return a flat list including MCP sub-tools with their parent info. Current API requires calling `getMcpTools(toolId)` per MCP server, which is N+1.

### Decision on API: Add `GET /api/tools/available-functions` endpoint
Returns a flat list of all bindable tool functions:
- REST API tools → `{ id, name, description, type: "RestApi" }`
- MCP sub-tools → `{ id, name, description, type: "McpTool", parentName }`

This avoids N+1 queries on the frontend.

### Alternatives Considered
1. **Modal dialog**: Rejected — over-engineered.
2. **Inline checklist**: Rejected — doesn't scale.
3. **N+1 API calls per MCP server**: Rejected — poor UX with loading delays.

---

## R9 — Frontend Tool Call Card

### Decision: Collapsible card with status transitions, embedded in message flow

### State Machine
```
TOOL_CALL_START → { status: "calling", name: toolName }
TOOL_CALL_ARGS  → { status: "calling", args: JSON string }
TOOL_CALL_END   → { status: "completed" }
Error path      → { status: "failed", error: message }
```

### ChatMessage Type Extension
```typescript
interface ToolCall {
  toolCallId: string;
  toolName: string;
  status: "calling" | "completed" | "failed";
  args?: string;
  result?: string;
}

interface ChatMessage {
  index: number;
  role: "user" | "assistant" | "tool";
  content: string;
  toolCalls?: ToolCall[];
}
```

### Visual Design
- Card with tool icon + name header
- Status badge: blue "Calling..." / green "Completed" / red "Failed"
- Collapsible sections: Arguments (JSON) + Result (JSON)
- Positioned inline in chat flow, before assistant text
- Uses existing shadcn `Collapsible` + `Card` components

### Alternatives Considered
1. **Separate tool call messages**: Rejected — protocol associates tool calls with parent message.
2. **Side panel**: Rejected — breaks chat context flow.
