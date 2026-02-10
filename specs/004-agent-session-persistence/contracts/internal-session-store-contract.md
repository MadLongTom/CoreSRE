# Internal Contract: PostgresAgentSessionStore

**Feature**: 004-agent-session-persistence  
**Date**: 2026-02-10  
**Type**: Internal Framework Integration（无外部 REST API 端点）

---

## Contract Overview

本功能不暴露任何外部 API 端点。`PostgresAgentSessionStore` 实现 Agent Framework 的 `AgentSessionStore` 抽象类，由 Framework 内部调用。

---

## AgentSessionStore 契约（继承自 Framework）

### SaveSessionAsync

```
Input:
  - agent: AIAgent         (Agent 实例，提供 Id 和 SerializeSession)
  - conversationId: string (对话标识符)
  - session: AgentSession  (会话状态对象)
  - cancellationToken: CancellationToken

Output: ValueTask (void)

Behavior:
  - 序列化: agent.SerializeSession(session) → JsonElement
  - 首次保存: INSERT 新记录
  - 后续保存: UPDATE 现有记录 (UPSERT 语义)
  - updated_at 时间戳刷新

Errors:
  - 数据库不可达 → 抛出异常（不静默吞掉）
```

### GetSessionAsync

```
Input:
  - agent: AIAgent         (Agent 实例，提供 Id 和 DeserializeSessionAsync)
  - conversationId: string (对话标识符)
  - cancellationToken: CancellationToken

Output: ValueTask<AgentSession>

Behavior:
  - 查询: SELECT by (agent.Id, conversationId)
  - 找到记录: agent.DeserializeSessionAsync(sessionData) → AgentSession
  - 未找到记录: agent.CreateSessionAsync() → 新 AgentSession
  - 永不返回 null

Errors:
  - 数据库不可达 → 抛出异常
  - 反序列化失败 → 由 agent.DeserializeSessionAsync 抛出异常
```

---

## DI 注册契约

```
Registration: IHostedAgentBuilder.WithSessionStore(factory)
Lifetime: Keyed Singleton (keyed by agent name)
Dependencies: IDbContextFactory<AppDbContext>

Usage:
  agentBuilder.WithSessionStore((sp, agentName) =>
      new PostgresAgentSessionStore(
          sp.GetRequiredService<IDbContextFactory<AppDbContext>>()));
```

---

## 数据库 UPSERT SQL 契约

```sql
INSERT INTO agent_sessions (agent_id, conversation_id, session_data, session_type, created_at, updated_at)
VALUES ({agentId}, {conversationId}, {sessionDataJson}::jsonb, {sessionType}, {now}, {now})
ON CONFLICT (agent_id, conversation_id) 
DO UPDATE SET 
    session_data = EXCLUDED.session_data,
    session_type = EXCLUDED.session_type,
    updated_at = EXCLUDED.updated_at;
```

参数化通过 EF Core `FormattableString` 实现，防止 SQL 注入。
