# Implementation Plan: AgentSession PostgreSQL 持久化

**Branch**: `004-agent-session-persistence` | **Date**: 2026-02-10 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/004-agent-session-persistence/spec.md`

## Summary

实现 `PostgresAgentSessionStore`，继承 Agent Framework 的 `AgentSessionStore` 抽象类，将 Agent 会话序列化为 JSONB 存储到 PostgreSQL `agent_sessions` 表。通过 `AIAgent.SerializeSession()` / `DeserializeSessionAsync()` 完成序列化/反序列化，使用 EF Core UPSERT 实现保存，复合主键 `(agent_id, conversation_id)` 标识记录。通过 `IHostedAgentBuilder.WithSessionStore()` 工厂方法注册为 DI 的 keyed singleton。

## Technical Context

**Language/Version**: C# / .NET 10.0  
**Primary Dependencies**: Microsoft.Agents.AI.Hosting（AgentSessionStore 抽象基类）、EF Core 10.0.2、Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0  
**Storage**: PostgreSQL（Aspire 编排），`agent_sessions` 表，JSONB 存储会话数据  
**Testing**: xUnit + FluentAssertions（遵循 Constitution TDD 流程，但本 Plan 阶段不创建测试）  
**Target Platform**: Linux server / Windows（.NET 跨平台）  
**Project Type**: Web application — Backend DDD 4-layer architecture  
**Performance Goals**: SaveSessionAsync < 2s, GetSessionAsync < 1s（正常负载）  
**Constraints**: 10,000 条并发活跃会话记录  
**Scale/Scope**: 单表 `agent_sessions`，2 个抽象方法实现，1 个 EF 实体配置，1 个 DI 扩展方法

## Constitution Check (Pre-Design)

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Spec-Driven Development | ✅ PASS | spec.md 已创建并通过 12/12 checklist 验证 |
| II | Test-Driven Development | ✅ PASS | Plan 阶段不产出代码；后续 tasks 阶段将遵循 Red-Green-Refactor |
| III | Domain-Driven Design | ✅ PASS | AgentSessionRecord 实体在 Domain 层；PostgresAgentSessionStore 在 Infrastructure 层；DI 注册在 API 层。依赖方向内向 |
| IV | Test Immutability | ✅ PASS | 无已提交测试受影响 |
| V | Interface-Before-Implementation | ⚠️ 特殊 | AgentSessionStore 是 Agent Framework 提供的抽象类（非接口），作为外部契约等同于接口。本项目不新增自有接口，直接实现框架抽象类 |

**Gate Result**: ✅ PASS — 所有原则通过或有正当理由

## Project Structure

### Documentation (this feature)

```text
specs/004-agent-session-persistence/
├── plan.md              # This file
├── research.md          # Phase 0: 技术研究
├── data-model.md        # Phase 1: 数据模型设计
├── quickstart.md        # Phase 1: 快速验证步骤
├── contracts/           # Phase 1: 无外部 API 契约（内部 Framework 调用）
└── tasks.md             # Phase 2 output (by /speckit.tasks)
```

### Source Code (repository root)

```text
Backend/
├── CoreSRE.Domain/
│   └── Entities/
│       └── AgentSessionRecord.cs          # 新增：会话持久化实体
├── CoreSRE.Infrastructure/
│   ├── Persistence/
│   │   ├── AppDbContext.cs                # 修改：添加 DbSet<AgentSessionRecord>
│   │   ├── Configurations/
│   │   │   └── AgentSessionRecordConfiguration.cs  # 新增：EF 实体配置
│   │   └── Sessions/
│   │       └── PostgresAgentSessionStore.cs        # 新增：核心实现
│   └── DependencyInjection.cs             # 修改：注册 Session Store 相关服务
├── CoreSRE/
│   └── Program.cs                         # 修改：WithSessionStore 注册（当 Agent 启用时）
└── CoreSRE.AppHost/
    └── (无修改)
```

**Structure Decision**: 沿用现有 DDD 4-layer 架构。`AgentSessionRecord` 作为 Domain 实体（独立于 BaseEntity，使用复合字符串主键）。`PostgresAgentSessionStore` 放在 Infrastructure/Persistence/Sessions/ 子目录下，与现有 Repository 模式并列但独立（因为它不遵循 `IRepository<T>` 接口，而是继承 Agent Framework 的 `AgentSessionStore` 抽象类）。

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| AgentSessionRecord 不继承 BaseEntity | 使用复合字符串主键 (agentId, conversationId) 而非 Guid 单主键 | BaseEntity 强制 Guid Id，与 Agent Framework 的 string-based 标识符不兼容 |
| PostgresAgentSessionStore 不走 IRepository 接口 | 继承外部框架的 AgentSessionStore 抽象类 | IRepository<T> 的 CRUD 语义不匹配 Save/Get 双方法模式，且 DI 注册方式完全不同（keyed singleton vs scoped） |

## Constitution Check (Post-Design)

*Re-evaluated after Phase 1 design completion.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Spec-Driven Development | ✅ PASS | spec.md 已更新 FR-006（GetSessionAsync 返回新会话而非 null，基于 R1 研究）|
| II | Test-Driven Development | ✅ PASS | Plan 阶段无代码产出；设计已考虑可测试性（IDbContextFactory 可 mock） |
| III | Domain-Driven Design | ✅ PASS | AgentSessionRecord 在 Domain 层（零外部依赖）；PostgresAgentSessionStore 在 Infrastructure 层（持有框架+EF依赖）；DI 注册在 API 层。依赖方向严格内向 |
| IV | Test Immutability | ✅ PASS | 无已提交测试受影响 |
| V | Interface-Before-Implementation | ✅ PASS | `AgentSessionStore` 抽象类由 Agent Framework 提供，等同于外部接口契约。`AgentSessionRecord` 是纯 POCO 无需接口 |

**Gate Result**: ✅ PASS — 所有原则通过，设计与 Constitution 一致
