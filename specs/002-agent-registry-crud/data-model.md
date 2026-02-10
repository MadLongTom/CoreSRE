# Data Model: Agent 注册与 CRUD 管理（多类型）

**Feature**: 002-agent-registry-crud  
**Date**: 2026-02-09  
**Source**: [spec.md](spec.md) Key Entities + [research.md](research.md) R1/R3

---

## Entities

### AgentRegistration（聚合根）

代表一个已注册的 Agent，是 Agent Registry 模块的核心实体。通过 `AgentType` 鉴别器区分三种 Agent 类型，每种类型对应不同的可空值对象配置。

**Inherits from**: `BaseEntity`（提供 Id: Guid, CreatedAt: DateTime, UpdatedAt: DateTime?）

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| Name | string | ✅ | 全局唯一, max 200 chars |
| Description | string? | ❌ | 可选描述文本 |
| AgentType | AgentType | ✅ | 枚举, 注册后不可变更 |
| Status | AgentStatus | ✅ | 初始值 Registered, 状态流转由 SPEC-002 处理 |
| Endpoint | string? | A2A ✅ | A2A Agent 的远程端点 URL, max 2048 chars |
| AgentCard | AgentCardVO? | A2A ✅ | A2A Agent 的协议描述卡片 (JSONB) |
| LlmConfig | LlmConfigVO? | ChatClient ✅ | ChatClient Agent 的 LLM 配置 (JSONB) |
| WorkflowRef | Guid? | Workflow ✅ | Workflow Agent 引用的 WorkflowDefinition ID |
| HealthCheck | HealthCheckVO | ✅ | 健康检查状态 (JSONB), 本 spec 仅初始化默认值 |

**Factory Methods**:
- `CreateA2A(name, description?, endpoint, agentCard)` → 创建 A2A 类型 Agent
- `CreateChatClient(name, description?, llmConfig)` → 创建 ChatClient 类型 Agent
- `CreateWorkflow(name, description?, workflowRef)` → 创建 Workflow 类型 Agent

**Update Method**:
- `Update(name, description?, endpoint?, agentCard?, llmConfig?, workflowRef?)` → 按当前类型更新配置, agentType 不可变

**Invariants**:
- Name 非空, 非空白, ≤ 200 字符
- AgentType 注册后不可变更
- A2A 类型: endpoint 和 agentCard 必须非空
- ChatClient 类型: llmConfig 必须非空且 modelId 非空
- Workflow 类型: workflowRef 必须非空且非 Guid.Empty

---

## Enums

### AgentType

| Value | Description |
|-------|-------------|
| A2A | 远程 A2A 协议 Agent, 通过 AgentCard 描述能力 |
| ChatClient | 本地 LLM Agent, 通过 LlmConfig 配置模型与工具 |
| Workflow | 工作流 Agent, 引用 WorkflowDefinition |

### AgentStatus

| Value | Description |
|-------|-------------|
| Registered | 已注册 (初始状态) |
| Active | 活跃 (SPEC-002 健康检查通过后设置) |
| Inactive | 不活跃 (连续健康检查失败后设置) |
| Error | 错误状态 |

---

## Value Objects

### AgentCardVO

A2A Agent 的协议描述卡片。存储为 PostgreSQL JSONB 列。

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| Skills | List\<AgentSkillVO\> | ❌ | 可为空列表 |
| Interfaces | List\<AgentInterfaceVO\> | ❌ | 可为空列表 |
| SecuritySchemes | List\<SecuritySchemeVO\> | ❌ | 可为空列表 |

### AgentSkillVO

Agent 的单项技能描述，嵌套在 AgentCardVO 中。

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| Name | string | ✅ | 技能名称 |
| Description | string | ❌ | 技能描述 |

### AgentInterfaceVO

Agent 支持的通信接口类型，嵌套在 AgentCardVO 中。

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| Protocol | string | ✅ | 协议类型 (如 "HTTP+SSE", "WebSocket") |
| Path | string | ❌ | URL 路径 |

### SecuritySchemeVO

Agent 的安全认证方案，嵌套在 AgentCardVO 中。

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| Type | string | ✅ | 方案类型 (如 "apiKey", "oauth2", "bearer") |
| Parameters | string? | ❌ | 配置参数 (JSON string) |

### LlmConfigVO

ChatClient Agent 的 LLM 配置。存储为 PostgreSQL JSONB 列。

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| ModelId | string | ✅ | LLM 模型标识符 |
| Instructions | string? | ❌ | 系统指令 |
| ToolRefs | List\<Guid\> | ❌ | 工具引用列表 (M2 模块 ID, 可为空) |

### HealthCheckVO

Agent 健康检查状态。本 spec 仅定义结构和默认值，行为由 SPEC-002 实现。

| Field | Type | Required | Default | Notes |
|-------|------|----------|---------|-------|
| LastCheckTime | DateTime? | ❌ | null | 最后一次检查时间 |
| IsHealthy | bool | ✅ | false | 是否健康 |
| FailureCount | int | ✅ | 0 | 连续失败次数 |

---

## Relationships

```
AgentRegistration (Aggregate Root)
├── 1:1  AgentCardVO?     (owned, JSONB, A2A only)
│   ├── 1:N  AgentSkillVO       (nested in JSON)
│   ├── 1:N  AgentInterfaceVO   (nested in JSON)
│   └── 1:N  SecuritySchemeVO   (nested in JSON)
├── 1:1  LlmConfigVO?     (owned, JSONB, ChatClient only)
│   └── contains List<Guid> ToolRefs (nested in JSON)
├── 1:1  HealthCheckVO     (owned, JSONB)
└── ref  WorkflowRef: Guid? (cross-module, Workflow only, not FK)
```

---

## Database Table: `agent_registrations`

| Column | PostgreSQL Type | Constraint | Index |
|--------|----------------|------------|-------|
| id | uuid | PK | ✅ (PK) |
| name | varchar(200) | NOT NULL | ✅ UNIQUE |
| description | text | nullable | |
| agent_type | varchar(20) | NOT NULL | ✅ |
| status | varchar(20) | NOT NULL | |
| endpoint | varchar(2048) | nullable | |
| agent_card | jsonb | nullable | |
| llm_config | jsonb | nullable | |
| workflow_ref | uuid | nullable | |
| health_check | jsonb | NOT NULL | |
| created_at | timestamptz | NOT NULL | |
| updated_at | timestamptz | nullable | |

---

## Validation Rules Summary

| AgentType | Required Fields | Type-Specific Validation |
|-----------|----------------|------------------------|
| A2A | name, endpoint, agentCard | endpoint ≤ 2048 chars |
| ChatClient | name, llmConfig.modelId | modelId 非空 |
| Workflow | name, workflowRef | workflowRef ≠ Guid.Empty |
| All | name ≤ 200 chars | agentType ∈ {A2A, ChatClient, Workflow} |
