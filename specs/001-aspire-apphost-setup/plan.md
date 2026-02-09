# Implementation Plan: Aspire AppHost 编排与 ServiceDefaults 配置

**Branch**: `001-aspire-apphost-setup` | **Date**: 2026-02-09 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-aspire-apphost-setup/spec.md`

## Summary

搭建 .NET Aspire 编排基础设施：创建 `CoreSRE.AppHost` 项目作为分布式应用编排入口，声明 PostgreSQL 容器和后端 API 服务的依赖关系；创建 `CoreSRE.ServiceDefaults` 共享项目，统一配置 OpenTelemetry（Traces/Metrics/Logs 导出到 Aspire Dashboard）、标准健康检查端点（`/health` + `/alive`）、HTTP 弹性策略（Polly 重试/超时/熔断）和服务发现。实现开发者一键启动全栈开发环境的零配置体验。

## Technical Context

**Language/Version**: C# / .NET 10.0 (`net10.0`)
**Primary Dependencies**:
  - Aspire.AppHost.Sdk `13.1.0` (AppHost 项目 SDK)
  - Aspire.Hosting.PostgreSQL `13.1.0` (PostgreSQL 容器编排)
  - Microsoft.Extensions.Http.Resilience `10.2.0` (Polly 标准弹性策略)
  - Microsoft.Extensions.ServiceDiscovery `10.2.0` (服务发现)
  - OpenTelemetry.Exporter.OpenTelemetryProtocol `1.15.0` (OTLP 导出器)
  - OpenTelemetry.Extensions.Hosting `1.15.0` (OTel 宿主集成)
  - OpenTelemetry.Instrumentation.AspNetCore `1.15.0` (ASP.NET Core 仪表化)
  - OpenTelemetry.Instrumentation.Http `1.15.0` (HttpClient 仪表化)
  - OpenTelemetry.Instrumentation.Runtime `1.15.0` (运行时指标仪表化)
**Storage**: PostgreSQL（由 Aspire `AddPostgres()` 容器化管理，自动生成连接字符串）
**Testing**: xUnit + `dotnet test`（TDD 流程，Constitution 要求）
**Target Platform**: Linux/Windows 服务器（开发环境需 Docker Desktop）
**Project Type**: Web（Backend DDD 4层 + Frontend React）
**Performance Goals**: `/health` 端点 < 1s 响应（SC-002），Trace Span 5s 内出现在 Dashboard（SC-003）
**Constraints**: 开发者克隆到运行 ≤ 3 步（SC-001），需 Docker Desktop
**Scale/Scope**: 单 API 服务 + PostgreSQL，后续扩展到多服务编排

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Gate | Status | Notes |
|---|------|--------|-------|
| 1 | **SDD**: Specification exists and confirmed? | ✅ PASS | `spec.md` 已完成，含 5 个 User Story、11 个 FR、5 个 SC |
| 2 | **TDD**: Tests will be written before implementation? | ✅ PASS | Plan 阶段不产出代码；tasks 阶段将按 Red-Green-Refactor 流程执行 |
| 3 | **DDD**: Domain layer zero external dependencies? | ✅ PASS | 本 Spec 不修改 Domain 层。AppHost 和 ServiceDefaults 是基础设施项目 |
| 4 | **DDD**: Dependencies flow inward only? | ✅ PASS | ServiceDefaults 被 API 引用（API → ServiceDefaults），AppHost 编排 API（外部编排器，不违反 DDD 分层） |
| 5 | **Test Immutability**: No committed tests modified? | ✅ PASS | 尚无已提交测试。新测试将在 tasks 阶段创建 |
| 6 | **Interface-Before-Implementation**: Interfaces defined before concrete classes? | ⚠️ N/A | 本 Spec 主要涉及 Aspire 编排配置和扩展方法，非业务逻辑接口。无需定义 Domain/Application 接口 |
| 7 | **5-Step Workflow**: Step 1 (Spec) → Step 2 (Test) → Step 3 (Interface) → Step 4 (Implement) → Step 5 (Verify)? | ✅ PASS | 当前处于 Step 1 → Plan 阶段。Implementation 将严格遵循 5-step 流程 |

**Gate Result**: ✅ ALL GATES PASS — proceed to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/001-aspire-apphost-setup/
├── plan.md              # This file
├── research.md          # Phase 0 output — Aspire 技术选型研究
├── data-model.md        # Phase 1 output — Aspire 资源模型
├── quickstart.md        # Phase 1 output — 开发者快速上手指南
├── contracts/           # Phase 1 output — 健康检查 API 契约
│   └── health-api.yaml  # OpenAPI 健康检查端点规范
├── checklists/
│   └── requirements.md  # 需求质量检查清单（已完成）
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
Backend/
├── CoreSRE.AppHost/                    # [NEW] Aspire 编排入口
│   ├── CoreSRE.AppHost.csproj          # SDK: Aspire.AppHost.Sdk/13.1.0
│   ├── Program.cs                      # AddPostgres → AddDatabase → AddProject → WithReference → WaitFor
│   └── Properties/
│       └── launchSettings.json         # Aspire Dashboard 启动配置
│
├── CoreSRE.ServiceDefaults/            # [NEW] 共享基础设施默认配置
│   ├── CoreSRE.ServiceDefaults.csproj  # IsAspireSharedProject=true
│   └── Extensions.cs                   # AddServiceDefaults() + MapDefaultEndpoints()
│
├── CoreSRE/                            # [MODIFY] API 入口项目
│   ├── CoreSRE.csproj                  # 添加 ServiceDefaults 项目引用
│   ├── CoreSRE.slnx                    # 添加 AppHost + ServiceDefaults 项目
│   └── Program.cs                      # 添加 AddServiceDefaults() + MapDefaultEndpoints()
│
├── CoreSRE.Domain/                     # [UNCHANGED]
├── CoreSRE.Application/                # [UNCHANGED]
└── CoreSRE.Infrastructure/             # [UNCHANGED]
```

**Structure Decision**: 采用 Web 应用结构（Option 2 变体）。新增 2 个项目：`CoreSRE.AppHost`（编排入口）和 `CoreSRE.ServiceDefaults`（共享基础设施配置），均位于 `Backend/` 目录下，与现有 DDD 分层项目平级。AppHost 不属于 DDD 任何层，它是编排器；ServiceDefaults 是横切关注点共享项目。

## Complexity Tracking

> 本 Spec 新增 2 个项目，需要在此说明合理性。

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| 新增 `CoreSRE.AppHost` 项目 | Aspire 编排器必须是独立的可执行项目（`OutputType=Exe`，使用 `Aspire.AppHost.Sdk`），不能合并到 API 项目中 | Aspire SDK 要求 AppHost 是独立项目，这是框架约束，非可选 |
| 新增 `CoreSRE.ServiceDefaults` 项目 | 共享 OTel/健康检查/弹性策略配置，避免在每个服务中重复。`IsAspireSharedProject=true` 标记要求独立项目 | 直接在 API 项目中配置会导致多服务场景下配置重复，且不符合 Aspire 的约定模式 |

## Constitution Check — Post-Design Re-evaluation

*Re-checked after Phase 1 design completion.*

| # | Gate | Status | Post-Design Notes |
|---|------|--------|-------------------|
| 1 | **SDD**: Spec + Plan + Contracts exist? | ✅ PASS | spec.md 完成，plan.md 完成，data-model.md + contracts/health-api.yaml + quickstart.md 已生成 |
| 2 | **TDD**: No implementation code produced? | ✅ PASS | Plan 阶段未产出任何实现代码。Tasks 阶段将严格执行 Red-Green-Refactor |
| 3 | **DDD**: Domain 层零外部依赖? | ✅ PASS | Domain 层完全不受本 Spec 影响，无任何变更 |
| 4 | **DDD**: 依赖仅向内流动? | ✅ PASS | ServiceDefaults 是横切关注点共享项目（非 DDD 层）。API → ServiceDefaults 是合理的基础设施引用。AppHost 是外部编排器，不参与 DDD 分层。`EnrichNpgsqlDbContext` 在 API 层调用是对 Infrastructure 注册的增强，不违反分层 |
| 5 | **Test Immutability**: 无已提交测试被修改? | ✅ PASS | 项目中尚无已提交测试。新测试将在 tasks 阶段按 TDD 流程创建 |
| 6 | **Interface-Before-Implementation**: 接口优先? | ⚠️ N/A | 本 Spec 涉及的是 Aspire 编排配置和扩展方法（基础设施关注点），不涉及业务领域接口 |
| 7 | **5-Step Workflow**: 流程合规? | ✅ PASS | Step 1 (Spec) ✅ → Plan (当前) ✅ → Tasks 阶段将执行 Step 2-5 |

**Post-Design Gate Result**: ✅ ALL GATES PASS — ready for Phase 2 (tasks generation via `/speckit.tasks`).
