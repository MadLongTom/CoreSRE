# Data Model: AgentSession PostgreSQL 持久化

**Feature**: 004-agent-session-persistence  
**Date**: 2026-02-10  
**Source**: [spec.md](spec.md) + [research.md](research.md)

---

## Entity: AgentSessionRecord

**Purpose**: 持久化一条 Agent 会话记录到 PostgreSQL。  
**Aggregate**: 独立实体（不从属于其他聚合根，不继承 `BaseEntity`）。  
**Identity**: 复合主键 `(AgentId, ConversationId)`，均为字符串类型。

### 属性

| 属性 | 类型 | 约束 | 说明 |
|------|------|------|------|
| AgentId | `string` | NOT NULL, PK part 1, max 255 | Agent 标识符（来自 `AIAgent.Id`） |
| ConversationId | `string` | NOT NULL, PK part 2, max 255 | 对话标识符 |
| SessionData | `JsonElement` | NOT NULL, JSONB | 序列化的会话数据（不透明 JSON） |
| SessionType | `string` | NOT NULL, max 100 | 会话类型名称（如 `ChatClientAgentSession`） |
| CreatedAt | `DateTime` | NOT NULL, default UTC now | 记录创建时间 |
| UpdatedAt | `DateTime` | NOT NULL, default UTC now | 最后更新时间 |

### 不继承 BaseEntity 的原因

`BaseEntity` 定义了 `Guid Id`（单一 GUID 主键）。`AgentSessionRecord` 使用复合字符串主键 `(AgentId, ConversationId)`，与 Agent Framework 的标识符模式匹配，无法适配 `BaseEntity`。

### 工厂方法

```
Create(agentId, conversationId, sessionData, sessionType)
  → 创建新的 AgentSessionRecord 实例
  → 验证: agentId/conversationId 不为空
  → 设置 CreatedAt = UpdatedAt = DateTime.UtcNow

Update(sessionData, sessionType)
  → 更新 session 数据
  → 设置 UpdatedAt = DateTime.UtcNow
```

---

## 数据库表: agent_sessions

```sql
CREATE TABLE agent_sessions (
    agent_id        VARCHAR(255)    NOT NULL,
    conversation_id VARCHAR(255)    NOT NULL,
    session_data    JSONB           NOT NULL,
    session_type    VARCHAR(100)    NOT NULL,
    created_at      TIMESTAMPTZ     NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ     NOT NULL DEFAULT NOW(),
    PRIMARY KEY (agent_id, conversation_id)
);
```

### 索引

| 索引 | 列 | 类型 | 说明 |
|------|-----|------|------|
| PK | `(agent_id, conversation_id)` | B-tree (主键自带) | 唯一标识查询 |
| IX_agent_sessions_agent_id | `agent_id` | B-tree | 按 Agent 查询所有会话 |

### 列映射

| 属性 | 列名 | PostgreSQL 类型 | EF Core 配置 |
|------|------|----------------|-------------|
| AgentId | `agent_id` | `VARCHAR(255)` | `HasColumnName("agent_id").HasMaxLength(255)` |
| ConversationId | `conversation_id` | `VARCHAR(255)` | `HasColumnName("conversation_id").HasMaxLength(255)` |
| SessionData | `session_data` | `JSONB` | `HasColumnName("session_data").HasColumnType("jsonb")` |
| SessionType | `session_type` | `VARCHAR(100)` | `HasColumnName("session_type").HasMaxLength(100)` |
| CreatedAt | `created_at` | `TIMESTAMPTZ` | `HasColumnName("created_at")` |
| UpdatedAt | `updated_at` | `TIMESTAMPTZ` | `HasColumnName("updated_at")` |

---

## 关系图

```
┌──────────────────────────────────────────┐
│           AgentSessionRecord              │
│  ─────────────────────────────────────── │
│  PK: (AgentId, ConversationId)           │
│                                          │
│  AgentId        : string [255]           │
│  ConversationId : string [255]           │
│  SessionData    : JsonElement (JSONB)    │
│  SessionType    : string [100]           │
│  CreatedAt      : DateTime               │
│  UpdatedAt      : DateTime               │
└──────────────────────────────────────────┘
         ↑ 独立实体，无外键关系
         │ 通过 AgentId 逻辑关联到 Agent Framework 的 AIAgent
         │ （非数据库外键，因 Agent 注册在不同的表/系统中）
```

---

## 状态转换

AgentSessionRecord 无复杂状态机。生命周期为：

```
[不存在] → Create (首次 SaveSessionAsync) → [已创建]
[已创建] → Update (后续 SaveSessionAsync) → [已更新] → Update → [已更新] → ...
```

每次 `SaveSessionAsync` 调用时：
- 如果记录不存在 → INSERT（创建）
- 如果记录已存在 → UPDATE（更新 session_data + updated_at）
- 由 PostgreSQL `ON CONFLICT DO UPDATE` 原子完成

---

## PostgresAgentSessionStore 与 AgentSessionRecord 的关系

`PostgresAgentSessionStore` **不使用** `AgentSessionRecord` 实体进行 CRUD。它直接通过 `IDbContextFactory<AppDbContext>` 获取数据库连接，执行原生 SQL UPSERT/SELECT。

`AgentSessionRecord` 的作用：
1. EF Core Migration 的模型定义来源（自动生成 `agent_sessions` 表）
2. `GetSessionAsync` 中 SELECT 查询结果的映射目标
3. Domain 层的领域概念表达

这种设计是因为：
- UPSERT 需要原生 SQL（EF Core 不原生支持 INSERT ON CONFLICT）
- `AgentSessionStore` 是 singleton，不能直接持有 scoped 的 DbContext
- 读取时可以使用 EF Core 查询（通过 IDbContextFactory 创建临时 context）
