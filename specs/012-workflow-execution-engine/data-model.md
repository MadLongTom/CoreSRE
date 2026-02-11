# Data Model: 工作流执行引擎

**Feature**: 012-workflow-execution-engine  
**Date**: 2026-02-11

---

## New Entities

### WorkflowExecution（聚合根）

工作流的一次执行实例。

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Id | Guid | Yes | 主键（继承 BaseEntity） |
| WorkflowDefinitionId | Guid | Yes | 关联的工作流定义 ID |
| Status | ExecutionStatus | Yes | 执行状态（Pending→Running→Completed/Failed/Canceled） |
| Input | JsonElement | Yes | 执行输入数据 |
| Output | JsonElement? | No | 执行输出数据（完成后填充） |
| StartedAt | DateTime? | No | 实际开始执行时间（状态变为 Running 时设置） |
| CompletedAt | DateTime? | No | 执行完成时间 |
| ErrorMessage | string? | No | 失败时的错误信息 |
| TraceId | string? | No | OpenTelemetry Trace ID（占位字段） |
| GraphSnapshot | WorkflowGraphVO | Yes | 启动时的 DAG 图快照 |
| NodeExecutions | List\<NodeExecutionVO\> | Yes | 各节点的执行记录 |
| CreatedAt | DateTime | Yes | 创建时间（继承 BaseEntity） |
| UpdatedAt | DateTime? | No | 最后更新时间（继承 BaseEntity） |

**Factory method**: `WorkflowExecution.Create(workflowDefinitionId, input, graphSnapshot)` → 初始化 Status=Pending，从 graphSnapshot.Nodes 生成所有 NodeExecutionVO（均为 Pending 状态）。

**Domain methods**:
- `Start()` → Status=Running, StartedAt=UtcNow
- `StartNode(nodeId)` → 对应节点 Status=Running, StartedAt=UtcNow
- `CompleteNode(nodeId, output)` → 对应节点 Status=Completed, Output=output, CompletedAt=UtcNow
- `FailNode(nodeId, errorMessage)` → 对应节点 Status=Failed, ErrorMessage=error, CompletedAt=UtcNow
- `SkipNode(nodeId)` → 对应节点 Status=Skipped
- `Complete(output)` → Status=Completed, Output=output, CompletedAt=UtcNow（仅允许从 Running 状态转换）
- `Fail(errorMessage)` → Status=Failed, ErrorMessage=error, CompletedAt=UtcNow（仅允许从 Running 状态转换）

**State machine**:
```
Pending → Running → Completed
                  → Failed
                  → Canceled (reserved for SPEC-024)
```

**Database table**: `workflow_executions`
- `id` (uuid, PK)
- `workflow_definition_id` (uuid, NOT NULL, indexed)
- `status` (varchar(20), NOT NULL, indexed, enum as string)
- `input` (jsonb, NOT NULL)
- `output` (jsonb, nullable)
- `started_at` (timestamptz, nullable)
- `completed_at` (timestamptz, nullable)
- `error_message` (text, nullable)
- `trace_id` (varchar(64), nullable)
- `graph_snapshot` (jsonb, NOT NULL, uses OwnsOne/ToJson)
- `node_executions` (jsonb, NOT NULL, uses OwnsMany inside OwnsOne wrapper or ToJson)
- `created_at` (timestamptz, NOT NULL)
- `updated_at` (timestamptz, nullable)

---

### NodeExecutionVO（值对象）

单个节点的执行记录，嵌套在 WorkflowExecution 中。

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| NodeId | string | Yes | 对应 DAG 图中的节点 ID |
| Status | NodeExecutionStatus | Yes | 节点执行状态 |
| Input | string? | No | 节点输入数据（JSON 字符串） |
| Output | string? | No | 节点输出数据（JSON 字符串） |
| ErrorMessage | string? | No | 失败时的错误信息 |
| StartedAt | DateTime? | No | 节点开始执行时间 |
| CompletedAt | DateTime? | No | 节点执行完成时间 |

**Note**: 使用 `string?` 而非 `JsonElement?` 存储输入输出，因为 `JsonElement` 是 struct 类型（不可 null 且不可 `init`-only 赋值），在 JSONB OwnsMany 配置中存储为字符串更简洁。执行引擎内部在需要时自行解析为 `JsonNode`。

---

## New Enums

### ExecutionStatus

| Value | Description |
|-------|-------------|
| Pending | 等待启动（初始状态） |
| Running | 执行中 |
| Completed | 成功完成 |
| Failed | 执行失败 |
| Canceled | 已取消（预留给 SPEC-024） |

### NodeExecutionStatus

| Value | Description |
|-------|-------------|
| Pending | 等待执行（初始状态） |
| Running | 执行中 |
| Completed | 成功完成 |
| Failed | 执行失败 |
| Skipped | 被条件分支跳过 |

---

## Existing Entities (Referenced, Not Modified)

### WorkflowDefinition（已有，SPEC-020）

通过 `WorkflowDefinitionId` 关联。执行引擎读取其 `Graph`（`WorkflowGraphVO`）并创建快照。仅 Published 状态可执行。

### AgentRegistration（已有，SPEC-001）

Agent 节点通过 `ReferenceId` 指向 `AgentRegistration.Id`。执行前校验存在性。通过 `IAgentResolver` 解析为 `AIAgent` 实例。

### ToolRegistration（已有，SPEC-010）

Tool 节点通过 `ReferenceId` 指向 `ToolRegistration.Id`。执行前校验存在性。通过 `IToolInvokerFactory` 调用。

---

## New Interfaces

### IWorkflowExecutionRepository

```
继承 IRepository<WorkflowExecution>

扩展方法:
- GetByWorkflowIdAsync(Guid workflowDefinitionId, CancellationToken) → IEnumerable<WorkflowExecution>
- GetByStatusAsync(ExecutionStatus status, CancellationToken) → IEnumerable<WorkflowExecution>
```

### IWorkflowEngine

```
定义位置: CoreSRE.Domain/Interfaces/

方法:
- ExecuteAsync(WorkflowExecution execution, CancellationToken) → Task
  接收已创建的 WorkflowExecution 实体，构建 Workflow 并执行，
  执行过程中实时更新 execution 的节点状态并通过 Repository 持久化。
```

### IConditionEvaluator

```
定义位置: CoreSRE.Application/Interfaces/

方法:
- Evaluate(string condition, string jsonOutput) → bool
  评估条件表达式（如 $.severity == "high"）是否对给定 JSON 输出匹配。
  
- TryEvaluate(string condition, string jsonOutput, out bool result) → bool
  尝试评估条件，失败时返回 false（不抛异常）。
```

---

## Relationships

```
WorkflowDefinition (1) ─── (0..*) WorkflowExecution
                                      │
                                      ├── GraphSnapshot : WorkflowGraphVO
                                      └── NodeExecutions : List<NodeExecutionVO>

AgentRegistration (1) ─── (0..*) WorkflowNodeVO (via ReferenceId, NodeType=Agent)
ToolRegistration  (1) ─── (0..*) WorkflowNodeVO (via ReferenceId, NodeType=Tool)
```

---

## DTOs

### WorkflowExecutionDto (Detail)

| Field | Type | Description |
|-------|------|-------------|
| Id | Guid | 执行 ID |
| WorkflowDefinitionId | Guid | 关联工作流 ID |
| Status | string | 执行状态（Pending/Running/Completed/Failed/Canceled） |
| Input | object | 执行输入（JSON） |
| Output | object? | 执行输出（JSON，可选） |
| ErrorMessage | string? | 错误信息 |
| StartedAt | DateTime? | 开始时间 |
| CompletedAt | DateTime? | 完成时间 |
| TraceId | string? | OpenTelemetry TraceId |
| NodeExecutions | List\<NodeExecutionDto\> | 各节点执行详情 |
| CreatedAt | DateTime | 创建时间 |

### WorkflowExecutionSummaryDto (List)

| Field | Type | Description |
|-------|------|-------------|
| Id | Guid | 执行 ID |
| Status | string | 执行状态 |
| StartedAt | DateTime? | 开始时间 |
| CompletedAt | DateTime? | 完成时间 |
| CreatedAt | DateTime | 创建时间 |

### NodeExecutionDto

| Field | Type | Description |
|-------|------|-------------|
| NodeId | string | 节点 ID |
| Status | string | 节点状态 |
| Input | string? | 节点输入（JSON 字符串） |
| Output | string? | 节点输出（JSON 字符串） |
| ErrorMessage | string? | 错误信息 |
| StartedAt | DateTime? | 开始时间 |
| CompletedAt | DateTime? | 完成时间 |
