# Data Model: 工作流实时推送（SignalR）

**Feature**: 017-workflow-signalr-push  
**Date**: 2026-02-14

## Overview

本特性不引入新的数据库表或持久化实体。所有推送事件均为瞬态 DTO，通过 SignalR WebSocket 实时传输。现有 `WorkflowExecution` 实体及其 `NodeExecutions` 集合作为 REST 回退的状态快照源，无需修改。

## New Interfaces

### IWorkflowExecutionNotifier

**Layer**: `CoreSRE.Domain/Interfaces/`  
**Purpose**: 定义执行引擎与推送层之间的契约。执行引擎仅依赖此接口，不直接依赖 SignalR。

```
IWorkflowExecutionNotifier
├── ExecutionStartedAsync(executionId: Guid, workflowDefinitionId: Guid)
├── NodeExecutionStartedAsync(executionId: Guid, nodeId: string, input: string?)
├── NodeExecutionCompletedAsync(executionId: Guid, nodeId: string, output: string?)
├── NodeExecutionFailedAsync(executionId: Guid, nodeId: string, error: string)
├── NodeExecutionSkippedAsync(executionId: Guid, nodeId: string)
├── ExecutionCompletedAsync(executionId: Guid, output: string?)
└── ExecutionFailedAsync(executionId: Guid, error: string)
```

All methods return `Task` and accept `CancellationToken`.

### IWorkflowClient

**Layer**: `CoreSRE/Hubs/` (API project)  
**Purpose**: 强类型 Hub 的客户端接口，定义服务端可以在客户端上调用的方法。

```
IWorkflowClient
├── ExecutionStarted(executionId: Guid, workflowDefinitionId: Guid)
├── NodeExecutionStarted(executionId: Guid, nodeId: string, input: string?)
├── NodeExecutionCompleted(executionId: Guid, nodeId: string, output: string?)
├── NodeExecutionFailed(executionId: Guid, nodeId: string, error: string)
├── NodeExecutionSkipped(executionId: Guid, nodeId: string)
├── ExecutionCompleted(executionId: Guid, output: string?)
└── ExecutionFailed(executionId: Guid, error: string)
```

All methods return `Task`. Method names match exactly what the frontend registers via `.on("MethodName")`.

## New Implementations

### SignalRWorkflowNotifier

**Layer**: `CoreSRE.Infrastructure/Services/`  
**Implements**: `IWorkflowExecutionNotifier`  
**Dependencies**: `IHubContext<WorkflowHub, IWorkflowClient>`  
**Registration**: Scoped in `DependencyInjection.cs`

Delegates each notification call to `_hubContext.Clients.Group($"execution:{executionId}").MethodName(...)`.

### NullWorkflowExecutionNotifier

**Layer**: `CoreSRE.Infrastructure/Services/`  
**Implements**: `IWorkflowExecutionNotifier`  
**Purpose**: 空操作实现，当无需推送时使用（测试场景、向后兼容）。

All methods return `Task.CompletedTask`.

### WorkflowHub

**Layer**: `CoreSRE/Hubs/`  
**Extends**: `Hub<IWorkflowClient>`  
**Hub methods (callable by client)**:
- `JoinExecution(Guid executionId)` → `Groups.AddToGroupAsync(Context.ConnectionId, $"execution:{executionId}")`
- `LeaveExecution(Guid executionId)` → `Groups.RemoveFromGroupAsync(Context.ConnectionId, $"execution:{executionId}")`

**Endpoint**: `/hubs/workflow`

## Existing Entities (unchanged)

### WorkflowExecution

已有实体，不修改。作为 REST 回退的状态快照源。

| Field | Type | Role in this feature |
|-------|------|---------------------|
| `Id` | `Guid` | 作为 SignalR Group 名 `execution:{Id}` 和事件参数 |
| `Status` | `ExecutionStatus` | REST 回退时的整体状态 |
| `NodeExecutions` | `List<NodeExecutionVO>` | REST 回退时的节点级状态 |
| `WorkflowDefinitionId` | `Guid` | `ExecutionStarted` 事件携带 |
| `GraphSnapshot` | `WorkflowGraphVO` | REST 回退时前端渲染 DAG 图 |

### NodeExecutionVO

已有值对象，不修改。

| Field | Type | Role in this feature |
|-------|------|---------------------|
| `NodeId` | `string` | 事件中标识节点 |
| `Status` | `NodeExecutionStatus` | 前端据此确定节点颜色 |
| `Input` | `string?` | `NodeExecutionStarted` 事件载荷 |
| `Output` | `string?` | `NodeExecutionCompleted` 事件载荷 |
| `ErrorMessage` | `string?` | `NodeExecutionFailed` 事件载荷 |

## Event Flow

```
WorkflowEngine.ExecuteAsync()
    │
    ├── execution.Start() → repo.UpdateAsync() → notifier.ExecutionStartedAsync()
    │
    ├── (for each node)
    │   ├── execution.StartNode() → repo.UpdateAsync() → notifier.NodeExecutionStartedAsync()
    │   ├── DispatchNodeAsync()
    │   └── (success) execution.CompleteNode() → repo.UpdateAsync() → notifier.NodeExecutionCompletedAsync()
    │   └── (failure) execution.FailNode() → repo.UpdateAsync() → notifier.NodeExecutionFailedAsync()
    │   └── (skip)   execution.SkipNode() → repo.UpdateAsync() → notifier.NodeExecutionSkippedAsync()
    │
    ├── (success) execution.Complete() → repo.UpdateAsync() → notifier.ExecutionCompletedAsync()
    └── (failure) execution.Fail()     → repo.UpdateAsync() → notifier.ExecutionFailedAsync()
```

**Key invariant**: Notifier 调用始终在 `_executionRepo.UpdateAsync()` 之后，确保数据库状态已持久化后再推送事件。这保证 REST 回退加载的状态不会落后于 SignalR 事件。

## Frontend State Model

### useWorkflowSignalR hook state

```
{
  connectionState: "connecting" | "connected" | "reconnecting" | "disconnected",
  lastEvent: SignalREvent | null
}
```

Hook 不维护独立的 nodeExecutions 状态——它接收 SignalR 事件后通过回调函数通知页面组件，由页面组件更新 `nodeExecutions` 数组并传递给 `DagExecutionViewer`。

### SignalR Event → Node Color Mapping (DagExecutionViewer 已有逻辑)

| Event | NodeExecutionStatus | Color |
|-------|-------------------|-------|
| `NodeExecutionStarted` | `Running` | Blue |
| `NodeExecutionCompleted` | `Completed` | Green |
| `NodeExecutionFailed` | `Failed` | Red |
| `NodeExecutionSkipped` | `Skipped` | Amber |
| (default) | `Pending` | Gray |
