# Data Model: 工作流引擎基础修复

**Feature**: 015-workflow-engine-fix | **Date**: 2026-02-12

## Overview

This feature modifies existing domain entities and DTOs — no new entities are introduced. One new Infrastructure service class is added (`MockChatClient`).

## Entity Changes

### NodeExecutionVO (Value Object — EXISTING, NO SCHEMA CHANGE)

**File**: `Backend/CoreSRE.Domain/ValueObjects/NodeExecutionVO.cs`

```csharp
public sealed record NodeExecutionVO
{
    public string NodeId { get; init; } = string.Empty;
    public NodeExecutionStatus Status { get; init; }
    public string? Input { get; init; }      // ← Already exists, currently never written
    public string? Output { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
}
```

**Change**: None to the record itself. The `Input` field already exists with the correct type. The fix is in the write path.

### WorkflowExecution (Entity — METHOD SIGNATURE CHANGE)

**File**: `Backend/CoreSRE.Domain/Entities/WorkflowExecution.cs`

**Before**:
```csharp
public void StartNode(string nodeId)
{
    var node = FindNode(nodeId);
    UpdateNode(nodeId, node with
    {
        Status = NodeExecutionStatus.Running,
        StartedAt = DateTime.UtcNow
    });
}
```

**After**:
```csharp
public void StartNode(string nodeId, string? input)
{
    var node = FindNode(nodeId);
    UpdateNode(nodeId, node with
    {
        Status = NodeExecutionStatus.Running,
        Input = input,
        StartedAt = DateTime.UtcNow
    });
}
```

**Validation**: `input` is nullable — null is acceptable for control nodes (FanOut, FanIn) that don't have meaningful string input. Agent and Tool nodes should always pass a non-null value.

**State Transition**: `Pending → Running` — Input is locked at this transition. It is never modified again (immutable once set).

## DTO Changes

### WorkflowExecutionDto (PROPERTY ADDITION)

**File**: `Backend/CoreSRE.Application/Workflows/DTOs/WorkflowExecutionDto.cs`

**Before**:
```csharp
public record WorkflowExecutionDto
{
    public Guid Id { get; init; }
    public Guid WorkflowDefinitionId { get; init; }
    public string Status { get; init; } = string.Empty;
    public JsonElement Input { get; init; }
    public JsonElement? Output { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? TraceId { get; init; }
    public List<NodeExecutionDto> NodeExecutions { get; init; } = [];
    public DateTime CreatedAt { get; init; }
}
```

**After** (single property addition):
```csharp
public record WorkflowExecutionDto
{
    public Guid Id { get; init; }
    public Guid WorkflowDefinitionId { get; init; }
    public string Status { get; init; } = string.Empty;
    public JsonElement Input { get; init; }
    public JsonElement? Output { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? TraceId { get; init; }
    public WorkflowGraphDto? GraphSnapshot { get; init; }    // ← NEW
    public List<NodeExecutionDto> NodeExecutions { get; init; } = [];
    public DateTime CreatedAt { get; init; }
}
```

**Backward Compatibility**: Adding a new nullable property to a JSON response is backward-compatible. Existing clients that don't expect `GraphSnapshot` will simply ignore it.

## New Service Class

### MockChatClient (Infrastructure Service — NEW)

**File**: `Backend/CoreSRE.Infrastructure/Services/MockChatClient.cs`

**Purpose**: Implements `IChatClient` from `Microsoft.Extensions.AI.Abstractions` to provide deterministic mock responses for Agent nodes without requiring a real LLM provider.

**Interface**: `IChatClient` (existing — from Microsoft.Extensions.AI.Abstractions)

**Behavior**:
- `GetResponseAsync()`: Returns a `ChatResponse` with a single assistant message containing:
  ```json
  {
    "mock": true,
    "agentName": "<configured agent name>",
    "inputSummary": "<first 200 chars of user message>",
    "timestamp": "<UTC ISO 8601>"
  }
  ```
- `GetStreamingResponseAsync()`: Not implemented (throws `NotSupportedException`). Workflow engine uses non-streaming path.

**Construction**: Takes `string agentName` for response identification.

## Relationship Diagram

```
WorkflowExecution (entity)
  ├── GraphSnapshot: WorkflowGraphVO ──AutoMapper──→ WorkflowGraphDto (in WorkflowExecutionDto)
  │     ├── Nodes: List<WorkflowNodeVO> ──→ List<WorkflowNodeDto>
  │     └── Edges: List<WorkflowEdgeVO> ──→ List<WorkflowEdgeDto>
  └── NodeExecutions: List<NodeExecutionVO> ──AutoMapper──→ List<NodeExecutionDto>
        └── Input: string? ← NOW WRITTEN by StartNode(nodeId, input)

AgentResolverService
  └── ResolveAsync() ──mock mode──→ MockChatClient : IChatClient
                     ──real mode──→ OpenAI IChatClient
```

## Database Impact

**None**. The `NodeExecutionVO.Input` field already exists in the database schema (persisted as part of the owned JSON column for `NodeExecutions`). No migration needed — we are simply writing to a field that was already defined but never populated.
