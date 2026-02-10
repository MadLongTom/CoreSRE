# Implementation Plan: LLM Provider 配置与模型发现

**Branch**: `006-llm-provider-config` | **Date**: 2026-02-10 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/006-llm-provider-config/spec.md`

## Summary

为 ChatClient Agent 引入 LLM Provider 管理能力。新增 `LlmProvider` 聚合根（Domain 层），支持注册 OpenAI 兼容的 API 端点（名称/Base URL/API Key），通过 `GET {baseUrl}/models` 发现可用模型列表并持久化。Application 层通过 MediatR CQRS 模式实现 Provider CRUD + 模型发现命令；Infrastructure 层使用 `IHttpClientFactory` 调用外部 API，EF Core 新表 `llm_providers` 存储 Provider 及发现的模型列表（JSONB）。扩展现有 `LlmConfigVO` 新增 `ProviderId` 字段，创建 ChatClient 时从 Provider 下拉选择后加载可用模型。前端改造 ChatClient 创建/编辑表单，替换自由文本为 Provider→Model 级联选择。

## Technical Context

**Language/Version**: C# / .NET 10.0 (Backend), TypeScript ~5.9.3 (Frontend)
**Primary Dependencies**: MediatR 12.4.1, FluentValidation 11.11.0, AutoMapper 13.0.1, EF Core 10.0.2, Npgsql 10.0.0, IHttpClientFactory (Backend); React 19, React Router 7, shadcn/ui (Frontend)
**Storage**: PostgreSQL (Aspire-orchestrated), EF Core `ToJson()` for JSONB, 新表 `llm_providers`
**Testing**: xUnit + FluentAssertions + Moq (Domain/Application), WebApplicationFactory (API)
**Target Platform**: Linux container (via Aspire AppHost), development on Windows
**Project Type**: Web — DDD 4-layer backend + React SPA frontend
**Performance Goals**: Provider CRUD < 1s, 模型发现 < 10s (取决于外部 API 响应)
**Constraints**: Domain 层零外部包依赖, API Key 掩码返回（永不返回明文）, 现有 LlmConfig 向后兼容（ProviderId 可为 null）
**Scale/Scope**: Provider 数量 < 50, 每个 Provider 模型 < 1000, 无分页需求

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Design Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. SDD | ✅ PASS | spec.md 已存在，16 条 FR，6 条 SC，5 个 User Story 含完整 acceptance scenarios |
| II. TDD | ✅ PASS | Plan 产出后，实现阶段将遵循 Red→Green→Refactor。Domain 95%, Application 90%, Infrastructure 80%, API 80% |
| III. DDD | ✅ PASS | LlmProvider 聚合根在 Domain 层（工厂方法创建、模型列表为值对象集合）。CQRS 在 Application，HttpClient 调用在 Infrastructure，Minimal API 在 API 层 |
| IV. Test Immutability | ✅ PASS | 测试将从 spec 推导，提交后锁定断言 |
| V. Interface-Before-Implementation | ✅ PASS | ILlmProviderRepository + IModelDiscoveryService 定义在 Domain/Application Interfaces，实现在 Infrastructure |

### Post-Design Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. SDD | ✅ PASS | data-model.md + contracts/ 与 spec.md 16 条 FR 完全对齐 |
| II. TDD | ✅ PASS | 测试目标明确：Domain factory + entity 行为, Application handlers + validators, Infrastructure HttpClient 模型发现, API endpoints |
| III. DDD | ✅ PASS | LlmProvider 领域模型无外部包依赖。HttpClient 模型发现完全在 Infrastructure 层，通过 IModelDiscoveryService 接口解耦。LlmConfigVO 扩展 ProviderId 不破坏分层 |
| IV. Test Immutability | ✅ PASS | 测试范围已从 spec acceptance scenarios 推导，现有 Agent 测试不受影响（ProviderId 可为 null 保证向后兼容） |
| V. Interface-Before-Implementation | ✅ PASS | ILlmProviderRepository 扩展 IRepository<LlmProvider>（Domain 层），IModelDiscoveryService 定义在 Application/Interfaces |

## Project Structure

### Documentation (this feature)

```text
specs/006-llm-provider-config/
├── plan.md              # This file
├── research.md          # Phase 0 output — technology research
├── data-model.md        # Phase 1 output — entity/VO definitions
├── quickstart.md        # Phase 1 output — developer quick start
├── contracts/           # Phase 1 output — API contracts
│   └── api-contract.md  # Provider endpoints + Agent LlmConfig changes
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
Backend/
├── CoreSRE.Domain/
│   ├── Entities/
│   │   ├── BaseEntity.cs                      # existing
│   │   ├── AgentRegistration.cs               # existing
│   │   └── LlmProvider.cs                     # NEW — aggregate root
│   ├── Enums/
│   │   ├── AgentType.cs                       # existing
│   │   └── AgentStatus.cs                     # existing
│   ├── ValueObjects/
│   │   ├── LlmConfigVO.cs                     # MODIFIED — add ProviderId (Guid?)
│   │   ├── DiscoveredModelVO.cs               # NEW — value object (model ID string)
│   │   └── ...                                # existing VOs unchanged
│   └── Interfaces/
│       ├── IRepository.cs                     # existing
│       ├── IAgentRegistrationRepository.cs    # existing
│       └── ILlmProviderRepository.cs          # NEW — extends IRepository<LlmProvider>
│
├── CoreSRE.Application/
│   ├── Common/
│   │   └── Interfaces/
│   │       └── IModelDiscoveryService.cs      # NEW — external HTTP call interface
│   └── Providers/
│       ├── DTOs/
│       │   ├── LlmProviderDto.cs              # NEW — full detail (masked API Key)
│       │   ├── LlmProviderSummaryDto.cs       # NEW — list item
│       │   ├── DiscoveredModelDto.cs           # NEW — model ID + discovered time
│       │   └── ProviderMappingProfile.cs       # NEW — AutoMapper
│       ├── Commands/
│       │   ├── RegisterProvider/
│       │   │   ├── RegisterProviderCommand.cs
│       │   │   ├── RegisterProviderCommandHandler.cs
│       │   │   └── RegisterProviderCommandValidator.cs
│       │   ├── UpdateProvider/
│       │   │   ├── UpdateProviderCommand.cs
│       │   │   ├── UpdateProviderCommandHandler.cs
│       │   │   └── UpdateProviderCommandValidator.cs
│       │   ├── DeleteProvider/
│       │   │   ├── DeleteProviderCommand.cs
│       │   │   └── DeleteProviderCommandHandler.cs
│       │   └── DiscoverModels/
│       │       ├── DiscoverModelsCommand.cs
│       │       └── DiscoverModelsCommandHandler.cs
│       └── Queries/
│           ├── GetProviders/
│           │   ├── GetProvidersQuery.cs
│           │   └── GetProvidersQueryHandler.cs
│           ├── GetProviderById/
│           │   ├── GetProviderByIdQuery.cs
│           │   └── GetProviderByIdQueryHandler.cs
│           └── GetProviderModels/
│               ├── GetProviderModelsQuery.cs
│               └── GetProviderModelsQueryHandler.cs
│
│   └── Agents/
│       └── DTOs/
│           └── LlmConfigDto.cs                # MODIFIED — add ProviderId (Guid?)
│
├── CoreSRE.Infrastructure/
│   ├── Persistence/
│   │   ├── AppDbContext.cs                    # MODIFIED — add DbSet<LlmProvider>
│   │   ├── LlmProviderRepository.cs          # NEW
│   │   └── Configurations/
│   │       └── LlmProviderConfiguration.cs   # NEW — EF Core mapping
│   ├── Services/
│   │   └── ModelDiscoveryService.cs           # NEW — HttpClient impl of IModelDiscoveryService
│   └── DependencyInjection.cs                 # MODIFIED — register ILlmProviderRepository + IModelDiscoveryService + HttpClient
│
├── CoreSRE/                                   # API layer
│   ├── Endpoints/
│   │   ├── AgentEndpoints.cs                  # existing (unchanged)
│   │   └── ProviderEndpoints.cs               # NEW — MapGroup("/api/providers")
│   └── Program.cs                             # MODIFIED — add ProviderEndpoints

Frontend/
└── src/
    ├── types/
    │   ├── agent.ts                           # MODIFIED — LlmConfig adds providerId
    │   └── provider.ts                        # NEW — Provider types
    ├── lib/api/
    │   ├── agents.ts                          # existing (unchanged)
    │   └── providers.ts                       # NEW — Provider API client
    ├── components/agents/
    │   ├── LlmConfigSection.tsx               # MODIFIED — Provider/Model selects
    │   └── ProviderModelSelect.tsx            # NEW — cascading Provider→Model select
    ├── pages/
    │   ├── AgentCreatePage.tsx                # MODIFIED — ChatClient uses ProviderModelSelect
    │   ├── AgentDetailPage.tsx                # MODIFIED — edit mode uses ProviderModelSelect
    │   ├── ProviderListPage.tsx               # NEW
    │   └── ProviderDetailPage.tsx             # NEW
    ├── components/layout/
    │   └── Sidebar.tsx                        # MODIFIED — add Provider nav link
    └── App.tsx                                # MODIFIED — add /providers routes
```

**Structure Decision**: 遵循现有 DDD 4 层架构。新增 `Providers` 功能目录与 `Agents` 平行在 Application 层。新增 `IModelDiscoveryService` 接口在 Application/Common/Interfaces（因为模型发现是跨越领域边界的外部调用，接口定义在 Application 层，实现在 Infrastructure 层）。前端新增 Provider 页面和级联选择组件。

## Complexity Tracking

> No constitution violations. Complexity justified by existing architecture patterns.

| Decision | Rationale |
|----------|-----------|
| IModelDiscoveryService 接口在 Application 层而非 Domain 层 | 模型发现涉及 HTTP 外部调用，属于应用服务而非纯领域逻辑。接口在 Application/Common/Interfaces，实现在 Infrastructure/Services |
| API Key 不加密存储（v1） | 初期 MVP 阶段，API Key 以明文存储于 PostgreSQL（仅在 API 响应中掩码）。后续迭代可引入 AES-256 或 Vault 加密。这避免了引入额外加密依赖的复杂度 |
| DiscoveredModels 作为 LlmProvider 聚合内的值对象集合 | 模型列表随 Provider 一起加载/更新，无独立生命周期，不需要单独的表/仓储 |
