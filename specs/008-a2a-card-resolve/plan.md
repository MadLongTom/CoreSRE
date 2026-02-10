# Implementation Plan: A2A AgentCard 自动解析

**Branch**: `008-a2a-card-resolve` | **Date**: 2026-02-10 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/008-a2a-card-resolve/spec.md`

## Summary

当用户创建 A2A Agent 时，系统通过用户输入的 Endpoint URL 自动解析远程 AgentCard（使用 `Microsoft.Agents.Client` 中的 `A2ACardResolver`），取代手动逐项填写。解析后自动填充表单字段（skills、interfaces、securitySchemes），并提供 URL 覆写选项。技术方案：后端新增 Minimal API 解析端点 + MediatR Query，前端在 A2A 表单中增加解析按钮和覆写开关。

## Technical Context

**Language/Version**: Backend: C# 14 / .NET 10.0; Frontend: TypeScript 5.9 / React 19.2  
**Primary Dependencies**: Backend: MediatR 12.4, FluentValidation 11.11, AutoMapper 13, EF Core 10, Microsoft.Agents.AI.* 1.0.0-preview; Frontend: Vite 7.3, react-router 7.13, shadcn/ui (radix-ui 1.4), react-hook-form 7.71, zod 4.3  
**Storage**: PostgreSQL (via .NET Aspire + Npgsql), JSONB columns for value objects  
**Testing**: xUnit + Moq (to be established in tasks phase; no test projects exist yet — historical tech debt)  
**Target Platform**: Web application (Linux server backend + browser frontend)  
**Project Type**: Web (backend + frontend)  
**Performance Goals**: AgentCard 解析 < 10 秒 (SC-001, dominated by remote endpoint response time)  
**Constraints**: 解析请求需有超时限制 (FR-007)；后端需能直接访问用户提供的外部 URL  
**Scale/Scope**: 单一功能增量，影响 Agent 创建流程（1 个新 API 端点 + 1 个 Query/Handler + 前端表单改造）

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

**Pre-Design (before Phase 0):**

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Spec-Driven Development | ✅ PASS | spec.md 已完成，包含输入/输出/边界条件/异常场景 |
| II | TDD — NON-NEGOTIABLE | ⚠️ VIOLATION (justified) | 仓库中无测试项目；本次计划阶段不产出代码，tasks 阶段必须先建立测试项目并遵循 Red-Green-Refactor |
| III | Domain-Driven Design | ✅ PASS | 新增功能遵循 API→Application→Domain←Infrastructure 分层；解析逻辑放在 Infrastructure，Query/Handler 在 Application |
| IV | Test Immutability | ✅ PASS (N/A) | 无现存测试可修改 |
| V | Interface-Before-Implementation | ✅ PASS | 将定义 `IAgentCardResolver` 接口在 Application/Interfaces，实现在 Infrastructure/Services |

**Gate Decision**: ✅ PASS — TDD 违规已确认为现有技术债，非本次功能引入。tasks 阶段将在 Step 2 前建立测试基础设施。

**Post-Design (after Phase 1):**

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | SDD | ✅ PASS | spec.md + data-model.md + contracts/ + research.md 全部完成 |
| II | TDD | ⚠️ VIOLATION (same) | 预存技术债未变；设计未引入新违规 |
| III | DDD | ✅ PASS | `IAgentCardResolver` 在 Application/Interfaces；`A2ACardResolverService` 在 Infrastructure/Services；Query/Handler 在 Application/Agents/Queries；endpoint 在 API/Endpoints。依赖方向正确 |
| IV | Test Immutability | ✅ PASS (N/A) | 无变化 |
| V | Interface-Before-Implementation | ✅ PASS | `IAgentCardResolver` 接口已在 data-model.md 中定义签名 |

**Post-Design Gate Decision**: ✅ PASS — 设计未引入任何新的 Constitution 违规。

## Project Structure

### Documentation (this feature)

```text
specs/008-a2a-card-resolve/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
Backend/
├── CoreSRE/                          # API layer (Minimal API)
│   └── Endpoints/
│       └── AgentEndpoints.cs         # + POST /api/agents/resolve-card
├── CoreSRE.Application/              # Application layer (CQRS)
│   ├── Agents/
│   │   └── Queries/
│   │       └── ResolveAgentCard/
│   │           ├── ResolveAgentCardQuery.cs
│   │           ├── ResolveAgentCardQueryHandler.cs
│   │           └── ResolveAgentCardQueryValidator.cs
│   └── Interfaces/
│       └── IAgentCardResolver.cs     # NEW interface
├── CoreSRE.Infrastructure/           # Infrastructure layer
│   └── Services/
│       └── A2ACardResolverService.cs # NEW implementation (wraps A2ACardResolver SDK)
└── CoreSRE.Domain/                   # Domain layer (no changes expected)

Frontend/
└── src/
    ├── lib/
    │   └── api/
    │       └── agents.ts             # + resolveAgentCard() API call
    ├── pages/
    │   └── AgentCreatePage.tsx        # Modified: resolve button, auto-fill, URL override switch
    └── types/
        └── agent.ts                  # + ResolvedAgentCard type
```

**Structure Decision**: Existing web application structure (Option 2). All changes fit within the established Clean Architecture layers. No new projects required — one new interface in Application, one new service in Infrastructure, one new query/handler set, one modified endpoint file, and frontend form changes.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| No test projects (TDD) | Existing technical debt, not introduced by this feature | Test infrastructure will be established during tasks phase before implementation begins |
