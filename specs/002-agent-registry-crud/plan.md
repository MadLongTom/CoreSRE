# Implementation Plan: Agent 注册与 CRUD 管理（多类型）

**Branch**: `002-agent-registry-crud` | **Date**: 2026-02-09 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/002-agent-registry-crud/spec.md`

## Summary

实现多类型 Agent（A2A / ChatClient / Workflow）的完整 CRUD 生命周期管理。采用 DDD 分层架构：Domain 层定义 `AgentRegistration` 聚合根（含类型鉴别的值对象）；Application 层通过 MediatR CQRS 模式实现命令/查询处理，FluentValidation 按类型校验；Infrastructure 层使用 EF Core `ToJson()` 将复杂值对象存储为 PostgreSQL JSONB 列；API 层通过 Minimal API `MapGroup` 暴露 RESTful 端点，全局异常中间件统一错误格式。

## Technical Context

**Language/Version**: C# / .NET 10.0  
**Primary Dependencies**: MediatR 12.4.1, FluentValidation 11.11.0, AutoMapper 13.0.1, EF Core 10.0.2, Npgsql 10.0.0  
**Storage**: PostgreSQL (Aspire-orchestrated), EF Core `ToJson()` for JSONB value objects  
**Testing**: xUnit + FluentAssertions + Moq (Domain/Application unit tests), WebApplicationFactory (API integration tests)  
**Target Platform**: Linux container (via Aspire AppHost), development on Windows  
**Project Type**: Web — DDD 4-layer backend (Domain → Application → Infrastructure → API)  
**Performance Goals**: Agent 注册 < 1 秒响应, 列表查询 < 500ms (≤100 条)  
**Constraints**: Domain 层零外部包依赖, 值对象不可变, 聚合根通过工厂方法创建  
**Scale/Scope**: 初期 < 100 Agent 注册, 无分页需求

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Design Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. SDD | ✅ PASS | spec.md 已存在，15 条 FR，6 条 SC，所有行为有明确规约 |
| II. TDD | ✅ PASS | Plan 产出后，实现阶段将遵循 Red→Green→Refactor。Domain 95%, Application 90%, Infrastructure 80%, API 80% |
| III. DDD | ✅ PASS | AgentRegistration 聚合根在 Domain 层，工厂方法创建，值对象不可变。CQRS 在 Application 层，EF Core 在 Infrastructure 层，Minimal API 在 API 层 |
| IV. Test Immutability | ✅ PASS | 测试将从 spec 推导，提交后锁定断言 |
| V. Interface-Before-Implementation | ✅ PASS | IAgentRegistrationRepository 定义在 Domain/Interfaces，实现在 Infrastructure |

### Post-Design Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. SDD | ✅ PASS | data-model.md + contracts/ 与 spec.md 要求完全对齐 |
| II. TDD | ✅ PASS | 测试目标明确：Domain factory methods + entity 行为, Application handlers + validators, Infrastructure repository, API endpoints |
| III. DDD | ✅ PASS | 领域模型无外部包依赖，`ToJson()` 配置完全在 Infrastructure 层。Result<T> 增加 ErrorCode 不破坏分层 |
| IV. Test Immutability | ✅ PASS | 测试范围已从 spec 的 acceptance scenarios 推导，无需修改 |
| V. Interface-Before-Implementation | ✅ PASS | IAgentRegistrationRepository 扩展 IRepository<AgentRegistration>，定义在 Domain 层 |

## Project Structure

### Documentation (this feature)

```text
specs/002-agent-registry-crud/
├── plan.md              # This file
├── research.md          # Phase 0 output — technology research
├── data-model.md        # Phase 1 output — entity/VO definitions
├── quickstart.md        # Phase 1 output — developer quick start
├── contracts/           # Phase 1 output — API contracts
│   └── agents-api.yaml  # OpenAPI 3.0 spec for Agent endpoints
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
Backend/
├── CoreSRE.Domain/
│   ├── Entities/
│   │   ├── BaseEntity.cs              # existing
│   │   └── AgentRegistration.cs       # NEW — aggregate root
│   ├── Enums/
│   │   ├── AgentType.cs               # NEW
│   │   └── AgentStatus.cs             # NEW
│   ├── ValueObjects/
│   │   ├── AgentCardVO.cs             # NEW
│   │   ├── AgentSkillVO.cs            # NEW
│   │   ├── AgentInterfaceVO.cs        # NEW
│   │   ├── SecuritySchemeVO.cs        # NEW
│   │   ├── LlmConfigVO.cs            # NEW
│   │   └── HealthCheckVO.cs           # NEW (stub for SPEC-002)
│   └── Interfaces/
│       ├── IRepository.cs             # existing
│       ├── IUnitOfWork.cs             # existing
│       └── IAgentRegistrationRepository.cs  # NEW — extends IRepository<>
│
├── CoreSRE.Application/
│   ├── Common/
│   │   ├── Models/Result.cs           # MODIFIED — add ErrorCode
│   │   └── Behaviors/ValidationBehavior.cs  # existing (register in DI)
│   └── Agents/
│       ├── DTOs/
│       │   ├── AgentRegistrationDto.cs    # NEW
│       │   ├── AgentSummaryDto.cs         # NEW
│       │   ├── AgentCardDto.cs            # NEW
│       │   ├── LlmConfigDto.cs            # NEW
│       │   └── AgentMappingProfile.cs     # NEW — AutoMapper
│       ├── Commands/
│       │   ├── RegisterAgent/
│       │   │   ├── RegisterAgentCommand.cs
│       │   │   ├── RegisterAgentCommandHandler.cs
│       │   │   └── RegisterAgentCommandValidator.cs
│       │   ├── UpdateAgent/
│       │   │   ├── UpdateAgentCommand.cs
│       │   │   ├── UpdateAgentCommandHandler.cs
│       │   │   └── UpdateAgentCommandValidator.cs
│       │   └── DeleteAgent/
│       │       ├── DeleteAgentCommand.cs
│       │       └── DeleteAgentCommandHandler.cs
│       └── Queries/
│           ├── GetAgents/
│           │   ├── GetAgentsQuery.cs
│           │   └── GetAgentsQueryHandler.cs
│           └── GetAgentById/
│               ├── GetAgentByIdQuery.cs
│               └── GetAgentByIdQueryHandler.cs
│
├── CoreSRE.Infrastructure/
│   ├── Persistence/
│   │   ├── AppDbContext.cs            # MODIFIED — add DbSet<AgentRegistration>
│   │   ├── Repository.cs             # existing
│   │   ├── AgentRegistrationRepository.cs  # NEW — implements IAgentRegistrationRepository
│   │   └── Configurations/
│   │       └── AgentRegistrationConfiguration.cs  # NEW — EF Core ToJson() mapping
│   └── DependencyInjection.cs         # MODIFIED — register IAgentRegistrationRepository
│
├── CoreSRE/                           # API layer
│   ├── Endpoints/
│   │   └── AgentEndpoints.cs          # NEW — MapGroup + handlers
│   ├── Middleware/
│   │   └── ExceptionHandlingMiddleware.cs  # NEW — ValidationException → 400
│   └── Program.cs                     # MODIFIED — add middleware + endpoints
```

**Structure Decision**: 遵循现有 DDD 4 层架构，新增文件全部放入对应层的约定目录。Application 层采用 Vertical Slice 组织（按功能 `Agents/Commands/Queries` 分目录）。

## Complexity Tracking

> No constitution violations. Complexity justified by existing architecture patterns.
