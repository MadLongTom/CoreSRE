# Implementation Plan: Agent 能力语义搜索

**Branch**: `003-agent-semantic-search` | **Date**: 2026-02-10 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/003-agent-semantic-search/spec.md`

## Summary

为平台提供 Agent 技能搜索能力。P1（MVP）实现关键词搜索——通过 PostgreSQL JSONB 查询在 A2A Agent 的 skill name/description 中执行大小写不敏感的模糊匹配。P2 实现语义搜索——通过 `IEmbeddingGenerator` 生成 skill description 的向量嵌入，使用 pgvector 存储并执行余弦相似度匹配，Embedding 服务不可用时自动降级为关键词模式。搜索端点 `GET /api/agents/search?q={query}` 集成到现有 Minimal API MapGroup。

## Technical Context

**Language/Version**: C# / .NET 10.0  
**Primary Dependencies**: MediatR 12.4.1, FluentValidation 11.11.0, AutoMapper 13.0.1, EF Core 10.0.2, Npgsql 10.0.0  
**P2 Additional Dependencies**: Microsoft.Extensions.AI.Abstractions, Pgvector 0.3.2, Pgvector.EntityFrameworkCore 0.3.0  
**Storage**: PostgreSQL (Aspire-orchestrated), JSONB for AgentCard/skills, pgvector for embeddings (P2)  
**Testing**: xUnit + FluentAssertions + Moq  
**Target Platform**: Linux container (via Aspire AppHost), development on Windows  
**Project Type**: Web — DDD 4-layer backend (Domain → Application → Infrastructure → API)  
**Performance Goals**: 关键词搜索 < 1 秒（≤ 100 Agent）  
**Constraints**: Domain 层零外部包依赖, 搜索仅限 A2A Agent（AgentCard.Skills）  
**Scale/Scope**: ≤ 100 Agent, 每 Agent ≤ 20 skills, 搜索结果不分页

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Design Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. SDD | ✅ PASS | spec.md 已存在，2 个 User Story，13 条 FR，5 条 SC，所有行为有明确规约 |
| II. TDD | ✅ PASS | 实现阶段将遵循 Red→Green→Refactor；测试从 spec 推导 |
| III. DDD | ✅ PASS | 搜索查询在 Application 层（CQRS Query），JSONB SQL 在 Infrastructure 层，端点在 API 层。Domain 层仅扩展接口 |
| IV. Test Immutability | ✅ PASS | 测试将从 spec acceptance scenarios 推导，提交后锁定断言 |
| V. Interface-Before-Implementation | ✅ PASS | IAgentRegistrationRepository 先扩展搜索方法签名，再在 Infrastructure 实现 |

### Post-Design Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. SDD | ✅ PASS | data-model.md + contracts/ 与 spec.md 要求完全对齐 |
| II. TDD | ✅ PASS | 测试目标明确：Repository JSONB 查询、Query Handler 排序/映射逻辑、Endpoint 参数校验 |
| III. DDD | ✅ PASS | 搜索方法接口在 Domain 层（`SearchBySkillAsync`），SQL 实现在 Infrastructure 层。Domain 层保持零外部包依赖 |
| IV. Test Immutability | ✅ PASS | 测试范围已从 spec 的 10 个 acceptance scenarios 推导 |
| V. Interface-Before-Implementation | ✅ PASS | `IAgentRegistrationRepository.SearchBySkillAsync()` 先定义后实现 |

## Project Structure

### Documentation (this feature)

```text
specs/003-agent-semantic-search/
├── plan.md              # This file
├── research.md          # Phase 0 output — technology research (6 decisions)
├── data-model.md        # Phase 1 output — DTO/entity definitions
├── quickstart.md        # Phase 1 output — developer quick start
├── contracts/           # Phase 1 output — API contracts
│   └── agents-search-api.yaml  # OpenAPI 3.0 spec for search endpoint
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
Backend/
├── CoreSRE.Domain/
│   └── Interfaces/
│       └── IAgentRegistrationRepository.cs  # MODIFIED — add SearchBySkillAsync()
│
├── CoreSRE.Application/
│   └── Agents/
│       ├── DTOs/
│       │   ├── AgentSearchResultDto.cs        # NEW — search result item
│       │   ├── MatchedSkillDto.cs              # NEW — matched skill detail
│       │   └── AgentSearchResponse.cs          # NEW — search response envelope
│       └── Queries/
│           └── SearchAgents/
│               ├── SearchAgentsQuery.cs         # NEW — MediatR IRequest
│               ├── SearchAgentsQueryHandler.cs   # NEW — handler with keyword matching logic
│               └── SearchAgentsQueryValidator.cs # NEW — FluentValidation (q required, max 500 chars)
│
├── CoreSRE.Infrastructure/
│   └── Persistence/
│       └── AgentRegistrationRepository.cs  # MODIFIED — implement SearchBySkillAsync()
│
├── CoreSRE/                               # API layer
│   └── Endpoints/
│       └── AgentEndpoints.cs              # MODIFIED — add GET /search route
```

**Structure Decision**: 遵循现有 DDD 4 层架构。新增文件集中在 Application 层（搜索 Query + DTOs）和 Infrastructure 层（JSONB 查询实现）。API 层仅在现有 `AgentEndpoints.MapGroup("/api/agents")` 中添加一个 `/search` 路由处理函数。Domain 层仅扩展 `IAgentRegistrationRepository` 接口添加搜索方法签名，不引入任何新包依赖。

## Implementation Approach

### P1: Keyword Search (MVP)

**数据流**: API endpoint → MediatR `SearchAgentsQuery` → `SearchAgentsQueryHandler` → `IAgentRegistrationRepository.SearchBySkillAsync()` → PostgreSQL JSONB query → C# matched skills extraction → `AgentSearchResponse`

**核心技术决策**（来自 research.md）:

1. **JSONB 查询策略 (R1)**: 使用 `EXISTS(SELECT 1 FROM jsonb_array_elements(...) WHERE value->>'Name' ILIKE ... OR value->>'Description' ILIKE ...)` 在 SQL 层面精确过滤含匹配 skill 的 Agent
2. **EF Core 集成方式 (R2)**: Hybrid approach — 先通过 `FromSqlInterpolated` / `SqlQuery` 获取匹配 Agent ID 列表，再用标准 EF Core `Include` 查询完整 Entity，最后在 C# 中提取 matched skills
3. **DTO 设计 (R5)**: 新建 `AgentSearchResultDto`（含 MatchedSkills 列表）和 `AgentSearchResponse`（含 SearchMode、Query、TotalCount），不扩展已有 `AgentSummaryDto`
4. **路由设计 (R6)**: `group.MapGet("/search", ...)` 置于 `AgentEndpoints` 中 `/{id:guid}` 路由之前，避免路由歧义

**排序逻辑**: 在 `SearchAgentsQueryHandler` 中通过 C# 计算每个 Agent 的匹配 skill 数量，按匹配数降序排列。

### P2: Semantic Search (Future)

> P2 不在本 plan 的实现范围内，但列出架构决策以指导后续工作。

1. **Embedding 接口 (R3)**: 使用 `IEmbeddingGenerator<string, Embedding<float>>` from `Microsoft.Extensions.AI.Abstractions`，通过 DI 注册具体实现
2. **向量存储 (R4)**: 新增 `SkillEmbedding` 实体 + `skill_embeddings` 表，使用 `Pgvector.EntityFrameworkCore` 映射 `Vector` 类型
3. **后台同步**: Agent 注册/更新时通过 `IHostedService` 后台计算 skill description 的向量嵌入
4. **降级策略**: `SearchAgentsQueryHandler` 中 try-catch `IEmbeddingGenerator` 调用，失败时降级为关键词模式，在 `AgentSearchResponse.SearchMode` 中标明 `"keyword-fallback"`

## Complexity Tracking

> No constitution violations. All design decisions align with existing DDD 4-layer architecture and CQRS patterns established in SPEC-001.
