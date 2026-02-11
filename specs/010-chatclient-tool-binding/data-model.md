# Data Model: ChatClient 工具绑定与对话调用

**Date**: 2026-02-11 | **Branch**: `010-chatclient-tool-binding`

## Existing Entities (No Schema Changes)

### LlmConfigVO (Value Object — JSONB column on AgentRegistration)

| Field | Type | Notes |
|-------|------|-------|
| `ProviderId` | `Guid?` | FK to LlmProvider |
| `ModelId` | `string` | LLM model identifier |
| `Instructions` | `string?` | System prompt |
| **`ToolRefs`** | `List<Guid>` | **This feature's binding target** — stores mixed IDs referencing `ToolRegistration.Id` (REST API) or `McpToolItem.Id` (MCP sub-tools) |

> No schema change needed — `ToolRefs` already exists and is persisted as JSONB array.

### ToolRegistration (Aggregate Root)

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `Guid` | PK |
| `Name` | `string` | Unique, max 200 |
| `Description` | `string?` | |
| `ToolType` | `ToolType` | `RestApi` or `McpServer` |
| `Status` | `ToolStatus` | `Active` or `Inactive` |
| `ConnectionConfig` | `ConnectionConfigVO` | Endpoint, TransportType, HttpMethod |
| `AuthConfig` | `AuthConfigVO` | AuthType, encrypted credentials |
| `ToolSchema` | `ToolSchemaVO?` | InputSchema (string), OutputSchema (string) |
| `McpToolItems` | `IReadOnlyCollection<McpToolItem>` | Child collection (MCP only) |

### McpToolItem (Entity — child of ToolRegistration)

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `Guid` | PK |
| `ToolRegistrationId` | `Guid` | FK to parent |
| `ToolName` | `string` | MCP tool name, max 200 |
| `Description` | `string?` | |
| `InputSchema` | `JsonElement?` | **Raw JSON** (not string!) |
| `OutputSchema` | `JsonElement?` | |
| `Annotations` | `ToolAnnotationsVO?` | |

### ToolSchemaVO (Value Object — JSONB on ToolRegistration)

| Field | Type | Notes |
|-------|------|-------|
| `InputSchema` | `string?` | **Serialized JSON string** (different from McpToolItem!) |
| `OutputSchema` | `string?` | Serialized JSON string |
| `Annotations` | `ToolAnnotationsVO?` | ReadOnly, Destructive, Idempotent, OpenWorldHint |

> **Critical difference**: `ToolSchemaVO.InputSchema` is `string?`; `McpToolItem.InputSchema` is `JsonElement?`. The `ToolFunctionFactory` must normalize both to `JsonElement` for `AIFunction.JsonSchema`.

---

## New Interfaces

### IToolFunctionFactory (Application Layer)

```csharp
// CoreSRE.Application/Interfaces/IToolFunctionFactory.cs
namespace CoreSRE.Application.Interfaces;

public interface IToolFunctionFactory
{
    /// <summary>
    /// Resolves tool references to invocable AIFunction instances.
    /// Skips unresolved (deleted) IDs with a logged warning.
    /// </summary>
    Task<IReadOnlyList<AIFunction>> CreateFunctionsAsync(
        IReadOnlyList<Guid> toolRefs,
        CancellationToken cancellationToken = default);
}
```

**Relationship**: Called by `AgentResolverService.ResolveChatClientAgent()` before building the `IChatClient` pipeline.

---

## New Classes (Infrastructure Layer)

### ToolFunctionFactory

Implements `IToolFunctionFactory`. Dependencies:
- `IToolRegistrationRepository` — fetch REST API tools by IDs
- `IMcpToolItemRepository` — fetch MCP sub-tools by IDs
- `IToolInvokerFactory` — get the appropriate invoker for each tool type

**Flow**:
1. Call `toolRegistrationRepo.GetByIdsAsync(toolRefs)` and `mcpToolItemRepo.GetByIdsAsync(toolRefs)` in parallel
2. For each `ToolRegistration` match: create `ToolRegistrationAIFunction` with `RestApiToolInvoker`
3. For each `McpToolItem` match: load parent `ToolRegistration` (via navigation prop or repo), create `McpToolAIFunction` with `McpToolInvoker`
4. Log warnings for unmatched IDs
5. Return combined `AIFunction` list

### ToolRegistrationAIFunction (subclass of AIFunction)

Custom `AIFunction` subclass for REST API tools:

| Override | Source |
|----------|--------|
| `Name` | `ToolRegistration.Name` |
| `Description` | `ToolRegistration.Description` |
| `JsonSchema` | Parse `ToolRegistration.ToolSchema.InputSchema` (string → JsonElement) |
| `InvokeCoreAsync()` | Deserialize args → `IToolInvoker.InvokeAsync(tool, null, params)` → serialize result |

### McpToolAIFunction (subclass of AIFunction)

Custom `AIFunction` subclass for MCP sub-tools:

| Override | Source |
|----------|--------|
| `Name` | `McpToolItem.ToolName` |
| `Description` | `McpToolItem.Description` |
| `JsonSchema` | `McpToolItem.InputSchema` (already JsonElement) |
| `InvokeCoreAsync()` | Deserialize args → `IToolInvoker.InvokeAsync(parentTool, mcpToolName, params)` → serialize result |

---

## Repository Extensions

### IToolRegistrationRepository — add method

```csharp
Task<IEnumerable<ToolRegistration>> GetByIdsAsync(
    IEnumerable<Guid> ids, 
    CancellationToken cancellationToken = default);
```

### IMcpToolItemRepository — add method

```csharp
Task<IEnumerable<McpToolItem>> GetByIdsAsync(
    IEnumerable<Guid> ids, 
    CancellationToken cancellationToken = default);
```

Both use EF Core `.Where(x => ids.Contains(x.Id))` for single-query batch fetch.

---

## Frontend Type Extensions

### ChatMessage (extended)

```typescript
export interface ToolCall {
  toolCallId: string;
  toolName: string;
  status: "calling" | "completed" | "failed";
  args?: string;   // JSON string
  result?: string;  // JSON string or error message
}

export interface ChatMessage {
  index: number;
  role: "user" | "assistant";
  content: string;
  toolCalls?: ToolCall[];  // present on assistant messages with tool usage
}
```

### BindableTool (new — for tool picker)

```typescript
export interface BindableTool {
  id: string;
  name: string;
  description?: string;
  toolType: "RestApi" | "McpTool";
  parentName?: string;   // MCP server name for McpTool type
  status: "Active" | "Inactive";
}
```

---

## Entity Relationships (updated)

```
AgentRegistration
  └─ LlmConfigVO (JSONB)
       └─ ToolRefs: Guid[] ──────┬──► ToolRegistration (RestApi)
                                 │     └─ ToolSchemaVO.InputSchema (string)
                                 └──► McpToolItem (MCP sub-tool)  
                                       └─ InputSchema (JsonElement)
                                       └─ ToolRegistration (parent, for connection config)
```

---

## State Transitions

### Tool Call Card (Frontend)

```
          TOOL_CALL_START
               │
               ▼
         ┌─────────────┐
         │   calling    │ ← spinner + tool name
         └──────┬──────┘
                │ TOOL_CALL_ARGS
                ▼
         ┌─────────────┐
         │   calling    │ ← spinner + tool name + args (collapsed)
         └──────┬──────┘
                │ TOOL_CALL_END
                ▼
    ┌───────────┴───────────┐
    ▼                       ▼
┌──────────┐         ┌──────────┐
│ completed│         │  failed  │
│  (green) │         │  (red)   │
└──────────┘         └──────────┘
```

---

## Validation Rules

| Rule | Where | Details |
|------|-------|---------|
| ToolRefs max 20 | `UpdateAgentValidator` | Warning only, not blocking |
| ToolRefs unique | Frontend picker | Prevent duplicate selection |
| ToolRefs valid format | Existing (GUID list) | Already validated by JSON deserialization |
| Deleted tool ref | `ToolFunctionFactory` | Silently skip + log warning |
| Inactive tool ref | `ToolFunctionFactory` | Include in AIFunction list (LLM may call; invocation will return error) |
