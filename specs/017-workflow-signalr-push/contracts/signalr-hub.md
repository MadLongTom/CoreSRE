# SignalR Hub Contract: WorkflowHub

**Feature**: 017-workflow-signalr-push  
**Date**: 2026-02-14  
**Endpoint**: `/hubs/workflow`  
**Transport**: WebSocket (default), fallback to Server-Sent Events / Long Polling  
**Protocol**: JSON

## Hub Methods (Client → Server)

### JoinExecution

加入指定执行的观察组。

```
Method: JoinExecution
Args:   executionId (Guid)
Returns: void
Group:  Adds caller to "execution:{executionId}"
```

**Behavior**:
- 客户端连接建立后调用
- 客户端重连后必须重新调用（Group 不跨连接持久化）
- 重复加入同一 Group 是安全的（幂等）

### LeaveExecution

离开指定执行的观察组。

```
Method: LeaveExecution
Args:   executionId (Guid)
Returns: void
Group:  Removes caller from "execution:{executionId}"
```

**Behavior**:
- 页面卸载前可选调用（连接断开时框架自动清理）
- 用于从一个执行切换到另一个执行的场景

## Client Methods (Server → Client)

所有方法推送到 Group `execution:{executionId}` 中的所有连接。

### ExecutionStarted

工作流执行已开始。

```
Method: ExecutionStarted
Args:
  - executionId: Guid      // 执行 ID
  - workflowDefinitionId: Guid  // 工作流定义 ID
```

### NodeExecutionStarted

节点开始执行。

```
Method: NodeExecutionStarted
Args:
  - executionId: Guid      // 执行 ID
  - nodeId: string         // 节点 ID (from WorkflowNodeVO.NodeId)
  - input: string | null   // 节点输入数据 (JSON string)
```

### NodeExecutionCompleted

节点执行成功完成。

```
Method: NodeExecutionCompleted
Args:
  - executionId: Guid      // 执行 ID
  - nodeId: string         // 节点 ID
  - output: string | null  // 节点输出数据 (JSON string)
```

### NodeExecutionFailed

节点执行失败。

```
Method: NodeExecutionFailed
Args:
  - executionId: Guid      // 执行 ID
  - nodeId: string         // 节点 ID
  - error: string          // 错误信息
```

### NodeExecutionSkipped

节点被跳过（条件路由未匹配）。

```
Method: NodeExecutionSkipped
Args:
  - executionId: Guid      // 执行 ID
  - nodeId: string         // 节点 ID
```

### ExecutionCompleted

工作流执行成功完成。

```
Method: ExecutionCompleted
Args:
  - executionId: Guid      // 执行 ID
  - output: string | null  // 最终输出数据 (JSON string)
```

### ExecutionFailed

工作流执行失败。

```
Method: ExecutionFailed
Args:
  - executionId: Guid      // 执行 ID
  - error: string          // 错误信息
```

## Connection Lifecycle

```
┌─────────────────────────────────────────────────────────┐
│ Client connects to /hubs/workflow                        │
├─────────────────────────────────────────────────────────┤
│ 1. GET /api/workflows/{id}/executions/{execId}          │ ← REST 加载当前状态快照
│ 2. connection.start()                                    │ ← WebSocket 握手
│ 3. connection.invoke("JoinExecution", executionId)       │ ← 加入观察组
│ 4. connection.on("NodeExecutionStarted", handler)        │ ← 接收实时事件
│    connection.on("NodeExecutionCompleted", handler)      │
│    connection.on("NodeExecutionFailed", handler)         │
│    connection.on("NodeExecutionSkipped", handler)        │
│    connection.on("ExecutionStarted", handler)            │
│    connection.on("ExecutionCompleted", handler)          │
│    connection.on("ExecutionFailed", handler)             │
├─────────────────────────────────────────────────────────┤
│ On reconnect (automatic):                                │
│ 1. connection.onreconnected → JoinExecution(executionId) │ ← 重新加入组
│ 2. GET /api/workflows/{id}/executions/{execId}          │ ← REST 补偿丢失事件
├─────────────────────────────────────────────────────────┤
│ On permanent disconnect:                                 │
│ 1. Fall back to setInterval(fetchData, 5000)            │ ← REST 轮询降级
│ 2. Show "实时连接不可用" toast                            │
├─────────────────────────────────────────────────────────┤
│ Page unmount:                                            │
│ 1. connection.stop()                                     │ ← 清理连接
└─────────────────────────────────────────────────────────┘
```

## Group Naming Convention

```
Pattern:  "execution:{executionId}"
Example:  "execution:a1b2c3d4-e5f6-7890-abcd-ef1234567890"
```

- 每次执行有一个独立的 Group
- 所有观察该执行的前端连接共享同一 Group
- 执行完成后 Group 自然清空（所有观察者最终离开或断开）

## Error Handling

| Scenario | Behavior |
|----------|----------|
| Hub method throws | SignalR 返回错误到调用客户端，连接保持 |
| Group 内无观察者 | 事件推送静默完成（无异常），不影响执行引擎 |
| 推送过程中连接断开 | SignalR 自动跳过断开的连接，其他观察者不受影响 |
| executionId 无效 | `JoinExecution` 成功（加入空 Group），客户端收不到事件 |
