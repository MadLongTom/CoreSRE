# Research: AgentSession PostgreSQL 持久化

**Feature**: 004-agent-session-persistence  
**Date**: 2026-02-10  
**Status**: Complete

---

## R1: AgentSessionStore 抽象类 API 精确签名

### Decision
直接继承 `Microsoft.Agents.AI.Hosting.AgentSessionStore` 抽象类，实现其 2 个抽象方法。

### Rationale
这是 Agent Framework 的唯一扩展点。`AgentSessionStore` 是 abstract class（非 interface），位于 `Microsoft.Agents.AI.Hosting` 命名空间。

### 精确 API 签名

```csharp
public abstract class AgentSessionStore
{
    public abstract ValueTask SaveSessionAsync(
        AIAgent agent, string conversationId, AgentSession session,
        CancellationToken cancellationToken = default);

    public abstract ValueTask<AgentSession> GetSessionAsync(
        AIAgent agent, string conversationId,
        CancellationToken cancellationToken = default);
}
```

**关键发现**：
- 返回值类型是 `ValueTask` / `ValueTask<AgentSession>`（非 `Task`）
- 两个方法都接收 `AIAgent` 实例（非字符串 ID）
- `GetSessionAsync` 在找不到会话时 **不返回 null**，而是调用 `agent.CreateSessionAsync()` 创建新会话

### InMemoryAgentSessionStore 参考实现模式

```csharp
// Save: 序列化 → 存储
var key = $"{agent.Id}:{conversationId}";
_threads[key] = agent.SerializeSession(session);

// Get: 查找 → 反序列化 or 新建
JsonElement? sessionContent = _threads.TryGetValue(key, out var existing) ? existing : null;
return sessionContent switch
{
    null => await agent.CreateSessionAsync(ct),
    _ => await agent.DeserializeSessionAsync(sessionContent.Value, cancellationToken: ct),
};
```

### Alternatives considered
- 实现 `IChatHistoryProvider`（Cosmos DB 方案）→ 拒绝，因为那是消息级存储，非会话级存储
- 自定义接口 → 拒绝，不符合框架集成要求

---

## R2: 序列化/反序列化机制

### Decision
会话序列化完全委托给 `AIAgent`，我们的 store 仅负责存储/读取不透明的 `JsonElement`。

### Rationale
`AIAgent.SerializeSession()` 和 `DeserializeSessionAsync()` 是多态方法——每种 Agent 子类（ChatClientAgent、WorkflowHostAgent 等）都有自己的序列化实现。Store 层不需要也不应该了解会话内部结构。

### 序列化调用链

```
SaveSessionAsync:
  AIAgent.SerializeSession(session)
    → SerializeSessionCore(session)     [abstract, 由子类实现]
      → ChatClientAgentSession.Serialize()  [具体实现]
        → SessionState { ConversationId, ChatHistoryProviderState, AIContextProviderState }
        → JsonSerializer.Serialize(sessionState)
    → 返回 JsonElement

GetSessionAsync:
  AIAgent.DeserializeSessionAsync(jsonElement)
    → DeserializeSessionCoreAsync(jsonElement)  [abstract, 由子类实现]
      → ChatClientAgentSession.DeserializeAsync(jsonElement, ...)
    → 返回 AgentSession
```

### 关键发现
- `SerializeSession` 是 **同步方法**，返回 `JsonElement`
- `DeserializeSessionAsync` 是 **异步方法**，返回 `ValueTask<AgentSession>`
- 会话类型在序列化后的 JSON 中隐含，反序列化由对应的 Agent 子类负责
- **spec 中 FR-009 的 session_type 字段**：虽然反序列化不需要此字段（Agent 自身知道类型），但保留它用于运维查询和诊断

### Alternatives considered
- 自定义序列化 → 拒绝，框架已提供完整的序列化机制
- 只存字符串不存 JSONB → 拒绝，JSONB 提供原生查询能力，且 Npgsql 原生支持 JsonElement

---

## R3: 复合主键设计

### Decision
使用 `agent.Id`（字符串）+ `conversationId`（字符串）作为复合主键，与 InMemoryAgentSessionStore 保持一致。

### Rationale
InMemoryAgentSessionStore 的 key 构造为 `$"{agent.Id}:{conversationId}"`。我们必须与框架保持一致的键空间语义。

### AIAgent.Id 特性分析

| 属性 | 可空? | 稳定性 | 用途 |
|------|-------|--------|------|
| `AIAgent.Id` | 永不 null | 默认为随机 GUID（不稳定）；当通过 `AddAgent(name, ...)` 注册时 `IdCore` 返回 agent name → 稳定 | InMemory store 用此属性 |
| `AIAgent.Name` | 可空 | 稳定（开发者设置） | DI keyed service 的 key |

**关键发现**：在 Hosting 场景下，`AIHostAgent` 包装 inner agent 并通过 `InnerAgent` 传递给 store。`AddAgent(name, ...)` 注册时 `Id` 被设置为 agent name，确保跨重启稳定。

### Alternatives considered
- 使用 `agent.Name` 代替 `agent.Id` → 拒绝，Name 可空，且不符合参考实现的模式
- 使用自定义 GUID 主键 → 拒绝，不符合框架的 string-based 标识符模式

---

## R4: UPSERT 策略

### Decision
使用 PostgreSQL 原生 `INSERT ... ON CONFLICT DO UPDATE` 语句，通过 `AppDbContext.Database.ExecuteSqlAsync` 执行。

### Rationale
单次数据库往返完成 UPSERT，无需先 SELECT 再判断 INSERT/UPDATE。PostgreSQL 的 `ON CONFLICT` 语法成熟且性能优良。

### SQL 设计

```sql
INSERT INTO agent_sessions (agent_id, conversation_id, session_data, session_type, created_at, updated_at)
VALUES ({agentId}, {conversationId}, {sessionData}::jsonb, {sessionType}, {now}, {now})
ON CONFLICT (agent_id, conversation_id) 
DO UPDATE SET 
    session_data = EXCLUDED.session_data,
    session_type = EXCLUDED.session_type,
    updated_at = EXCLUDED.updated_at;
```

使用 `FormattableString` 的 `{parameter}` 语法确保 SQL 参数化，防止注入。这与 SPEC-003 中 `SearchBySkillAsync` 的成功模式一致。

### Alternatives considered
- **EF Core AddOrUpdate（Find → Add/Update）**→ 拒绝，需要 2 次数据库往返
- **EF Core `ExecuteUpdateAsync`** → 拒绝，只能 UPDATE 不能 INSERT
- **EF Core Upsert 第三方库（FlexLabs.Upsert）**→ 拒绝，引入不必要的额外依赖
- **直接使用 Npgsql 的 `NpgsqlCommand`** → 拒绝，绕过了 EF Core 的连接管理

---

## R5: JsonElement 到 JSONB 的映射

### Decision
在 EF Core 实体配置中将 `SessionData` 属性配置为 `jsonb` 列类型。在 UPSERT SQL 中使用 `::jsonb` 类型转换。

### Rationale
Npgsql.EntityFrameworkCore.PostgreSQL 10.x 原生支持 `System.Text.Json.JsonElement` 到 PostgreSQL `jsonb` 的映射。无需额外转换。

### 配置方式

```csharp
// EF Core Entity Configuration
builder.Property(e => e.SessionData)
    .HasColumnName("session_data")
    .HasColumnType("jsonb")
    .IsRequired();
```

### SQL 参数传递
在 `FormattableString` 中，`JsonElement` 需要先转换为 `string`（通过 `JsonSerializer.Serialize` 或 `GetRawText()`），然后在 SQL 中使用 `::jsonb` 转换：

```csharp
var sessionDataJson = sessionData.GetRawText();
// 在 FormattableString 中: {sessionDataJson}::jsonb
```

### 现有项目 JSONB 模式参考
`AgentRegistrationConfiguration.cs` 使用 `OwnsOne + ToJson()` 模式存储值对象为 JSONB。但我们的场景不同——`SessionData` 是不透明的 JSON 数据（非强类型值对象），应使用 `JsonElement` 属性类型 + `HasColumnType("jsonb")`。

### Alternatives considered
- 存储为 `string` 类型（text 列） → 拒绝，失去 JSONB 的原生查询和索引能力
- 使用 `JsonDocument` 而非 `JsonElement` → 拒绝，`JsonDocument` 是 `IDisposable`，不适合作为实体属性

---

## R6: NuGet 包依赖

### Decision
Infrastructure 项目需新增 `Microsoft.Agents.AI.Hosting` 包引用。使用与本地 `.reference/codes/agent-framework` 源码匹配的预览版本。

### Rationale
`AgentSessionStore` 抽象类定义在 `Microsoft.Agents.AI.Hosting` 中。`AIAgent` 定义在 `Microsoft.Agents.AI` 中（是 Hosting 的传递依赖）。

### 包依赖树

```
Microsoft.Agents.AI.Hosting
  ├── Microsoft.Agents.AI          (AIAgent, AgentSession)
  ├── Microsoft.Agents.Core        (基础类型)
  ├── Microsoft.Agents.Builder     (DI builder)
  └── System.Text.Json             (JsonElement)
```

### 影响分析
- `CoreSRE.Infrastructure` 需添加 `Microsoft.Agents.AI.Hosting` 包引用
- `CoreSRE.Domain` 保持零外部依赖（AgentSessionRecord 是纯 POCO）
- `CoreSRE` (API 项目) 已通过 Infrastructure 间接获得 Hosting 引用

### Alternatives considered
- 在 Domain 层引用 Agent Framework 包 → 拒绝，违反 DDD 原则（Domain 零外部依赖）
- 自定义抽象避免包引用 → 拒绝，无法与 Framework 的 `WithSessionStore` DI 集成

---

## R7: DI 注册模式

### Decision
使用 `WithSessionStore` 的工厂重载 `Func<IServiceProvider, string, AgentSessionStore>` 注册 PostgresAgentSessionStore，从 DI 容器获取 `AppDbContext`。

### Rationale
`WithSessionStore` 注册为 **keyed singleton**（keyed by agent name）。使用工厂重载可以从 `IServiceProvider` 获取 scoped 的 `AppDbContext`——但注意 singleton 和 scoped 的生命周期不匹配。

### 生命周期问题及解决方案

**问题**：`AgentSessionStore` 注册为 keyed **singleton**，但 `AppDbContext` 注册为 **scoped**。直接注入 AppDbContext 到 singleton 会导致 captive dependency。

**解决方案**：注入 `IServiceScopeFactory` 或 `IDbContextFactory<AppDbContext>` 到 store，在每次 Save/Get 调用时创建新的 scope/context。

```csharp
// 推荐方式：使用 IDbContextFactory
public class PostgresAgentSessionStore(IDbContextFactory<AppDbContext> contextFactory) 
    : AgentSessionStore
{
    public override async ValueTask SaveSessionAsync(...)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        // 使用 context 执行 UPSERT
    }
}
```

### DI 注册代码

```csharp
// 在 Infrastructure DependencyInjection.cs 中
services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(...));

// 在 Program.cs 中（当 Agent 启用时）
builder.WithSessionStore((sp, agentName) =>
    new PostgresAgentSessionStore(
        sp.GetRequiredService<IDbContextFactory<AppDbContext>>()));
```

### Alternatives considered
- 直接注入 `AppDbContext` → 拒绝，singleton 捕获 scoped 依赖会导致内存泄漏和线程安全问题
- 使用 `IServiceScopeFactory` → 可行但更低级；`IDbContextFactory` 是 EF Core 官方推荐的模式
- 注册 store 为 scoped 而非 singleton → 拒绝，Framework 强制 keyed singleton 注册
