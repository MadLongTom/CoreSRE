# Data Model: 工作流定义 CRUD

**Feature**: 011-workflow-crud  
**Date**: 2026-02-11  
**Source**: [spec.md](spec.md) + [research.md](research.md)

## Entity Relationship Diagram

```text
┌─────────────────────────────────┐      WorkflowRef (Guid?)
│       AgentRegistration         │←─────────────────────────────┐
│  (existing, AgentType=Workflow) │                              │
└─────────────────────────────────┘                              │
                                                                 │
┌────────────────────────────────────────────────────────┐       │
│              WorkflowDefinition                        │───────┘
│  (Aggregate Root, extends BaseEntity)                  │
├────────────────────────────────────────────────────────┤
│  Id            : Guid          (PK, from BaseEntity)   │
│  Name          : string        (unique, max 200)       │
│  Description   : string?       (max 2000)              │
│  Status        : WorkflowStatus (Draft | Published)    │
│  Graph         : WorkflowGraphVO (JSONB)               │
│  CreatedAt     : DateTime      (from BaseEntity)       │
│  UpdatedAt     : DateTime?     (from BaseEntity)       │
└─────────────────┬──────────────────────────────────────┘
                  │ contains (JSONB)
                  ▼
┌────────────────────────────────────────────────────────┐
│              WorkflowGraphVO                           │
│  (Value Object, stored as JSONB column "graph")        │
├────────────────────────────────────────────────────────┤
│  Nodes         : List<WorkflowNodeVO>                  │
│  Edges         : List<WorkflowEdgeVO>                  │
└────┬──────────────────┬───────────────────────────────┘
     │                  │
     ▼                  ▼
┌──────────────────┐  ┌──────────────────────────────────┐
│  WorkflowNodeVO  │  │  WorkflowEdgeVO                  │
│  (Value Object)  │  │  (Value Object)                  │
├──────────────────┤  ├──────────────────────────────────┤
│  NodeId : string │  │  EdgeId      : string            │
│  NodeType        │  │  SourceNodeId: string            │
│  ReferenceId?    │  │  TargetNodeId: string            │
│  DisplayName     │  │  EdgeType    : WorkflowEdgeType  │
│  Config?  : str  │  │  Condition?  : string            │
└──────────────────┘  └──────────────────────────────────┘
         │                         │
         ▼                         ▼
   WorkflowNodeType          WorkflowEdgeType
   ┌─────────────┐          ┌──────────────┐
   │ Agent       │          │ Normal       │
   │ Tool        │          │ Conditional  │
   │ Condition   │          └──────────────┘
   │ FanOut      │
   │ FanIn       │
   └─────────────┘
```

## Entities

### WorkflowDefinition (Aggregate Root)

```csharp
namespace CoreSRE.Domain.Entities;

/// <summary>
/// 工作流定义聚合根。以 DAG 图描述 Agent 和 Tool 的编排关系。
/// </summary>
public class WorkflowDefinition : BaseEntity
{
    /// <summary>工作流名称，全局唯一，最长 200 字符</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>工作流描述（可选，最长 2000 字符）</summary>
    public string? Description { get; private set; }

    /// <summary>工作流状态（Draft/Published），仅 Draft 可编辑/删除</summary>
    public WorkflowStatus Status { get; private set; }

    /// <summary>DAG 图定义（节点 + 边），JSONB 存储</summary>
    public WorkflowGraphVO Graph { get; private set; } = new();
}
```

**Invariants**:
- Name: non-null, non-whitespace, max 200 characters
- Description: max 2000 characters (if provided)
- Status: `Draft` at creation; immutable transitions via explicit methods
- Graph: always valid DAG (validated before assignment)
- Update/Delete only allowed when Status == Draft

**Factory methods**:
- `Create(name, description, graph)` → sets Status = Draft, validates all invariants
- `Update(name, description, graph)` → guards Status == Draft, validates all invariants
- `Publish()` → transitions Draft → Published (stub for SPEC-026)

## Value Objects

### WorkflowGraphVO

```csharp
namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 工作流 DAG 图值对象，包含节点列表和边列表。JSONB 存储。
/// </summary>
public sealed record WorkflowGraphVO
{
    /// <summary>节点列表</summary>
    public List<WorkflowNodeVO> Nodes { get; init; } = [];

    /// <summary>边列表</summary>
    public List<WorkflowEdgeVO> Edges { get; init; } = [];
}
```

### WorkflowNodeVO

```csharp
namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 工作流图中的节点。描述一个执行单元。
/// </summary>
public sealed record WorkflowNodeVO
{
    /// <summary>节点 ID，图内唯一标识（用户指定，非数据库 GUID）</summary>
    public string NodeId { get; init; } = string.Empty;

    /// <summary>节点类型（Agent/Tool/Condition/FanOut/FanIn）</summary>
    public WorkflowNodeType NodeType { get; init; }

    /// <summary>引用 ID（Agent 或 Tool 的注册 ID，仅 Agent/Tool 类型必填）</summary>
    public Guid? ReferenceId { get; init; }

    /// <summary>显示名称</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>配置参数（JSON 格式字符串，可选）</summary>
    public string? Config { get; init; }
}
```

### WorkflowEdgeVO

```csharp
namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 工作流图中的边。描述节点间的执行流向。
/// </summary>
public sealed record WorkflowEdgeVO
{
    /// <summary>边 ID，图内唯一标识</summary>
    public string EdgeId { get; init; } = string.Empty;

    /// <summary>源节点 ID</summary>
    public string SourceNodeId { get; init; } = string.Empty;

    /// <summary>目标节点 ID</summary>
    public string TargetNodeId { get; init; } = string.Empty;

    /// <summary>边类型（Normal/Conditional）</summary>
    public WorkflowEdgeType EdgeType { get; init; }

    /// <summary>条件表达式（仅 Conditional 类型必填）</summary>
    public string? Condition { get; init; }
}
```

## Enums

### WorkflowNodeType

```csharp
namespace CoreSRE.Domain.Enums;

/// <summary>工作流节点类型</summary>
public enum WorkflowNodeType
{
    /// <summary>Agent 节点，引用 AgentRegistration</summary>
    Agent,
    /// <summary>Tool 节点，引用 ToolRegistration</summary>
    Tool,
    /// <summary>条件分支节点</summary>
    Condition,
    /// <summary>并行分发节点</summary>
    FanOut,
    /// <summary>聚合汇总节点</summary>
    FanIn
}
```

### WorkflowEdgeType

```csharp
namespace CoreSRE.Domain.Enums;

/// <summary>工作流边类型</summary>
public enum WorkflowEdgeType
{
    /// <summary>无条件执行边</summary>
    Normal,
    /// <summary>条件执行边（需条件表达式）</summary>
    Conditional
}
```

### WorkflowStatus

```csharp
namespace CoreSRE.Domain.Enums;

/// <summary>工作流状态</summary>
public enum WorkflowStatus
{
    /// <summary>草稿状态，可编辑删除</summary>
    Draft,
    /// <summary>已发布状态，不可编辑删除</summary>
    Published
}
```

## DTOs (Application Layer)

### WorkflowDefinitionDto (Full Detail)

```csharp
public record WorkflowDefinitionDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Status { get; init; } = string.Empty;
    public WorkflowGraphDto Graph { get; init; } = new();
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
```

### WorkflowSummaryDto (List View)

```csharp
public record WorkflowSummaryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Status { get; init; } = string.Empty;
    public int NodeCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
```

### WorkflowGraphDto / WorkflowNodeDto / WorkflowEdgeDto

```csharp
public record WorkflowGraphDto
{
    public List<WorkflowNodeDto> Nodes { get; init; } = [];
    public List<WorkflowEdgeDto> Edges { get; init; } = [];
}

public record WorkflowNodeDto
{
    public string NodeId { get; init; } = string.Empty;
    public string NodeType { get; init; } = string.Empty;   // enum as string
    public Guid? ReferenceId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? Config { get; init; }
}

public record WorkflowEdgeDto
{
    public string EdgeId { get; init; } = string.Empty;
    public string SourceNodeId { get; init; } = string.Empty;
    public string TargetNodeId { get; init; } = string.Empty;
    public string EdgeType { get; init; } = string.Empty;    // enum as string
    public string? Condition { get; init; }
}
```

## Repository Interface

```csharp
namespace CoreSRE.Domain.Interfaces;

/// <summary>
/// WorkflowDefinition 专用仓储接口
/// </summary>
public interface IWorkflowDefinitionRepository : IRepository<WorkflowDefinition>
{
    /// <summary>按名称查询（用于唯一性检查）</summary>
    Task<WorkflowDefinition?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>按状态过滤查询</summary>
    Task<IEnumerable<WorkflowDefinition>> GetByStatusAsync(WorkflowStatus status, CancellationToken cancellationToken = default);

    /// <summary>检查名称是否已存在（排除指定 ID，用于更新场景）</summary>
    Task<bool> ExistsWithNameAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>检查工作流是否被 AgentRegistration 引用</summary>
    Task<bool> IsReferencedByAgentAsync(Guid workflowId, CancellationToken cancellationToken = default);
}
```

## Database Table

```sql
CREATE TABLE workflow_definitions (
    id              UUID            NOT NULL PRIMARY KEY DEFAULT gen_random_uuid(),
    name            VARCHAR(200)    NOT NULL UNIQUE,
    description     TEXT,
    status          VARCHAR(20)     NOT NULL DEFAULT 'Draft',
    graph           JSONB           NOT NULL DEFAULT '{}'::jsonb,
    created_at      TIMESTAMPTZ     NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ
);

CREATE INDEX idx_workflow_definitions_name   ON workflow_definitions (name);
CREATE INDEX idx_workflow_definitions_status ON workflow_definitions (status);
```

JSONB `graph` column structure:
```json
{
  "Nodes": [
    {
      "NodeId": "alert-receiver",
      "NodeType": "Agent",
      "ReferenceId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "DisplayName": "告警接收 Agent",
      "Config": null
    },
    {
      "NodeId": "rca-analyzer",
      "NodeType": "Agent",
      "ReferenceId": "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
      "DisplayName": "根因分析 Agent",
      "Config": null
    },
    {
      "NodeId": "severity-check",
      "NodeType": "Condition",
      "ReferenceId": null,
      "DisplayName": "严重度判断",
      "Config": "$.severity == 'critical'"
    }
  ],
  "Edges": [
    {
      "EdgeId": "e1",
      "SourceNodeId": "alert-receiver",
      "TargetNodeId": "severity-check",
      "EdgeType": "Normal",
      "Condition": null
    },
    {
      "EdgeId": "e2",
      "SourceNodeId": "severity-check",
      "TargetNodeId": "rca-analyzer",
      "EdgeType": "Conditional",
      "Condition": "$.severity == 'critical'"
    }
  ]
}
```

## Cross-Entity Relationships

| Source | Target | Relationship | Direction |
|--------|--------|-------------|-----------|
| `AgentRegistration` (Workflow type) | `WorkflowDefinition` | `AgentRegistration.WorkflowRef → WorkflowDefinition.Id` | FK (logical, not EF-navigated) |
| `WorkflowNodeVO` (Agent type) | `AgentRegistration` | `WorkflowNodeVO.ReferenceId → AgentRegistration.Id` | Validated at write-time, not FK |
| `WorkflowNodeVO` (Tool type) | `ToolRegistration` | `WorkflowNodeVO.ReferenceId → ToolRegistration.Id` | Validated at write-time, not FK |

**Note**: Cross-aggregate references are validated at write-time (create/update) only. No EF Core navigation properties or foreign keys. If the referenced Agent/Tool is later deleted, the workflow definition retains the stale reference — handled at execution time (SPEC-021).
