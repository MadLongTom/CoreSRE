# Data Model: Team Agent 领域模型与 CRUD

**Feature**: 018-team-agent-model  
**Date**: 2026-02-17

## Overview

新增 `TeamMode` 枚举、`TeamConfigVO` / `HandoffTargetVO` 值对象，扩展 `AgentRegistration` 聚合根。不引入新的独立实体或数据库表——Team 配置以 JSONB 列存储在现有 `AgentRegistration` 表中。

## New Enums

### TeamMode

**Layer**: `CoreSRE.Domain/Enums/`

```csharp
public enum TeamMode
{
    Sequential,      // 顺序管道：A→B→C
    Concurrent,      // 并发聚合：A∥B∥C → merge
    RoundRobin,      // 轮询 GroupChat
    Handoffs,        // 交接/Swarm：Agent 自主路由
    Selector,        // LLM 动态选择下一个发言者
    MagneticOne,     // 双循环账本编排
}
```

## New Value Objects

### TeamConfigVO

**Layer**: `CoreSRE.Domain/ValueObjects/`  
**Storage**: JSONB column on `AgentRegistration` table

```
TeamConfigVO
├── Mode: TeamMode (required)
├── ParticipantIds: List<Guid> (required, non-empty)
├── MaxIterations: int = 40
│
├── [Handoffs]
│   ├── HandoffRoutes: Dictionary<Guid, List<HandoffTargetVO>>?
│   └── InitialAgentId: Guid?
│
├── [Selector]
│   ├── SelectorProviderId: Guid?
│   ├── SelectorModelId: string?
│   ├── SelectorPrompt: string?
│   └── AllowRepeatedSpeaker: bool = true
│
├── [MagneticOne]
│   ├── OrchestratorProviderId: Guid?
│   ├── OrchestratorModelId: string?
│   ├── MaxStalls: int = 3
│   └── FinalAnswerPrompt: string?
│
└── [Concurrent]
    └── AggregationStrategy: string?
```

### HandoffTargetVO

**Layer**: `CoreSRE.Domain/ValueObjects/`

```
HandoffTargetVO
├── TargetAgentId: Guid (required)
└── Reason: string?
```

## Modified Entities

### AgentRegistration

**Layer**: `CoreSRE.Domain/Entities/`

**New fields**:

| Field | Type | Nullable | Default | Storage |
|-------|------|----------|---------|---------|
| `TeamConfig` | `TeamConfigVO` | Yes | null | JSONB |

**New factory method**: `CreateTeam(name, description, teamConfig)` → sets `AgentType = Team`

**Validation per TeamMode**:

| Mode | Rules |
|------|-------|
| Sequential | `ParticipantIds.Count >= 2` |
| Concurrent | `ParticipantIds.Count >= 2` |
| RoundRobin | `ParticipantIds.Count >= 2` |
| Handoffs | `InitialAgentId != null` ∧ `InitialAgentId ∈ ParticipantIds` ∧ `HandoffRoutes != null` ∧ all route sources/targets ∈ `ParticipantIds` |
| Selector | `SelectorProviderId != null` ∧ `SelectorModelId != null` ∧ `ParticipantIds.Count >= 2` |
| MagneticOne | `OrchestratorProviderId != null` ∧ `OrchestratorModelId != null` ∧ `ParticipantIds.Count >= 1` |

## Modified Enum

### AgentType

**Layer**: `CoreSRE.Domain/Enums/`

```diff
 public enum AgentType
 {
     A2A,
     ChatClient,
     Workflow,
+    Team,
 }
```

## Database Changes

### Migration: AddTeamConfig

```sql
ALTER TABLE "AgentRegistrations"
ADD COLUMN "TeamConfig" jsonb NULL;
```

- **Nullable**: Yes — 仅 AgentType=Team 时有值
- **Index**: 无需索引（不按 TeamConfig 查询）
- **Backward compatible**: 现有数据 TeamConfig=NULL，不受影响

## DTOs

### TeamConfigDto

```
TeamConfigDto
├── mode: string (enum name)
├── participantIds: Guid[]
├── maxIterations: int
├── handoffRoutes: Record<Guid, HandoffTargetDto[]>?
├── initialAgentId: Guid?
├── selectorProviderId: Guid?
├── selectorModelId: string?
├── selectorPrompt: string?
├── allowRepeatedSpeaker: bool
├── orchestratorProviderId: Guid?
├── orchestratorModelId: string?
├── maxStalls: int
├── finalAnswerPrompt: string?
└── aggregationStrategy: string?
```

### HandoffTargetDto

```
HandoffTargetDto
├── targetAgentId: Guid
└── reason: string?
```

### AgentRegistrationDto (modified)

```diff
 AgentRegistrationDto
 ├── id, name, description, agentType, status
 ├── endpoint, agentCard    // A2A
 ├── llmConfig              // ChatClient
 ├── workflowRef            // Workflow
+├── teamConfig             // Team (TeamConfigDto?)
 └── healthCheck
```
