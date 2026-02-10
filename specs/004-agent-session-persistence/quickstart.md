# Quickstart: AgentSession PostgreSQL 持久化

**Feature**: 004-agent-session-persistence  
**Date**: 2026-02-10  
**Purpose**: 验证功能正确实现的快速步骤

---

## 前置条件

1. .NET 10 SDK 已安装
2. Docker Desktop 已运行（Aspire 需要）
3. 代码已编译成功: `dotnet build Backend/CoreSRE/CoreSRE.slnx`

---

## 验证步骤

### Step 1: 编译验证

```bash
cd E:\CoreSRE
dotnet build Backend/CoreSRE/CoreSRE.slnx
```

**预期结果**: 0 errors, 0 warnings（或仅有预期的 warnings）

### Step 2: EF Core Migration 验证

```bash
cd E:\CoreSRE\Backend\CoreSRE.Infrastructure
dotnet ef migrations list --startup-project ../CoreSRE
```

**预期结果**: 列出包含 `agent_sessions` 表创建的 migration

### Step 3: 数据库表结构验证

启动 Aspire AppHost（确保 PostgreSQL 容器启动）后，连接数据库验证表存在：

```sql
SELECT table_name FROM information_schema.tables 
WHERE table_name = 'agent_sessions';

-- 验证表结构
SELECT column_name, data_type, is_nullable 
FROM information_schema.columns 
WHERE table_name = 'agent_sessions'
ORDER BY ordinal_position;
```

**预期结果**:
| column_name | data_type | is_nullable |
|-------------|-----------|-------------|
| agent_id | character varying | NO |
| conversation_id | character varying | NO |
| session_data | jsonb | NO |
| session_type | character varying | NO |
| created_at | timestamp with time zone | NO |
| updated_at | timestamp with time zone | NO |

### Step 4: DI 注册验证

在 `Program.cs` 中确认 `PostgresAgentSessionStore` 可被正确注册（当 Agent 配置就绪时）。当前阶段验证编译通过即可，运行时验证需要完整的 Agent Framework 集成。

### Step 5: 单元测试验证（后续 tasks 阶段）

```bash
dotnet test Backend/CoreSRE/CoreSRE.slnx
```

**预期结果**: 所有新增和现有测试通过

---

## 关键验证点

| # | 验证项 | 方法 | 状态 |
|---|--------|------|------|
| 1 | 解决方案编译成功 | `dotnet build` | ☐ |
| 2 | `agent_sessions` 表 Migration 存在 | `dotnet ef migrations list` | ☐ |
| 3 | `AgentSessionRecord` 实体在 Domain 层 | 文件位置检查 | ☐ |
| 4 | `PostgresAgentSessionStore` 在 Infrastructure 层 | 文件位置检查 | ☐ |
| 5 | `IDbContextFactory<AppDbContext>` 已注册 | DI 配置检查 | ☐ |
| 6 | UPSERT SQL 使用参数化查询 | 代码审查 | ☐ |
| 7 | 现有功能不受影响 | 运行现有测试 | ☐ |
