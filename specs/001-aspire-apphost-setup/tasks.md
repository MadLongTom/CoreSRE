# Tasks: Aspire AppHost 编排与 ServiceDefaults 配置

**Input**: Design documents from `/specs/001-aspire-apphost-setup/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/health-api.yaml ✅, quickstart.md ✅

**Tests**: Not explicitly requested in the feature specification. This spec covers infrastructure/configuration (Aspire orchestration, OTel, health checks), not business logic. Tests are omitted per template guidelines.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)
- Include exact file paths in descriptions

## Path Conventions

- **Web app (DDD)**: `Backend/CoreSRE.{Project}/` at repository root
- **New Aspire projects**: `Backend/CoreSRE.AppHost/`, `Backend/CoreSRE.ServiceDefaults/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create new Aspire project scaffolding and update solution structure

- [x] T001 Create `CoreSRE.ServiceDefaults` project file with `IsAspireSharedProject=true`, `FrameworkReference Microsoft.AspNetCore.App`, and all OTel/Resilience/ServiceDiscovery package references in `Backend/CoreSRE.ServiceDefaults/CoreSRE.ServiceDefaults.csproj`
- [x] T002 Create `CoreSRE.AppHost` project file with `Aspire.AppHost.Sdk/13.1.0` SDK, `OutputType Exe`, and `Aspire.Hosting.PostgreSQL 13.1.0` package reference in `Backend/CoreSRE.AppHost/CoreSRE.AppHost.csproj`
- [x] T003 Add `CoreSRE.AppHost` and `CoreSRE.ServiceDefaults` projects to solution file in `/aspire/` folder in `Backend/CoreSRE/CoreSRE.slnx`
- [x] T004 Add `CoreSRE.ServiceDefaults` project reference and `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL 13.1.0` package reference to `Backend/CoreSRE/CoreSRE.csproj`
- [x] T005 Add `CoreSRE` API project reference to `Backend/CoreSRE.AppHost/CoreSRE.AppHost.csproj`
- [x] T006 Rename connection string key from `"DefaultConnection"` to `"coresre"` in `Backend/CoreSRE/appsettings.json` and `Backend/CoreSRE/appsettings.Development.json`
- [x] T007 Update `GetConnectionString("DefaultConnection")` to `GetConnectionString("coresre")` in `Backend/CoreSRE.Infrastructure/DependencyInjection.cs`

**Checkpoint**: All projects created, solution structure updated, connection string aligned with Aspire resource naming. Run `dotnet restore Backend/CoreSRE/CoreSRE.slnx` and `dotnet build Backend/CoreSRE/CoreSRE.slnx` to verify compilation.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Implement ServiceDefaults shared project — the cross-cutting infrastructure that ALL user stories depend on

**⚠️ CRITICAL**: No user story work can begin until ServiceDefaults `Extensions.cs` is complete, as US1 (AppHost), US2 (health checks), US3 (OTel), and US4 (resilience) all depend on it.

- [x] T008 Implement `AddServiceDefaults<TBuilder>()` extension method (entry point that calls ConfigureOpenTelemetry, AddDefaultHealthChecks, AddServiceDiscovery, ConfigureHttpClientDefaults) in `Backend/CoreSRE.ServiceDefaults/Extensions.cs`
- [x] T009 Implement `ConfigureOpenTelemetry<TBuilder>()` private extension method (Logging with FormattedMessage+Scopes, Metrics with AspNetCore+HttpClient+Runtime instrumentation, Tracing with AspNetCore+HttpClient instrumentation filtering /health and /alive paths, OTLP exporter conditional on env var) in `Backend/CoreSRE.ServiceDefaults/Extensions.cs`
- [x] T010 Implement `AddDefaultHealthChecks<TBuilder>()` private extension method (self check with "live" tag) in `Backend/CoreSRE.ServiceDefaults/Extensions.cs`
- [x] T011 Implement `MapDefaultEndpoints()` extension method on `WebApplication` (map `/health` for readiness, `/alive` for liveness filtered by "live" tag, only in Development environment) in `Backend/CoreSRE.ServiceDefaults/Extensions.cs`

**Checkpoint**: ServiceDefaults compiles. Run `dotnet build Backend/CoreSRE.ServiceDefaults/CoreSRE.ServiceDefaults.csproj` to verify.

---

## Phase 3: User Story 1 — 开发者一键启动全栈开发环境 (Priority: P1) 🎯 MVP

**Goal**: 开发者运行 `dotnet run --project CoreSRE.AppHost` 即可自动启动 PostgreSQL 容器 + API 服务 + Aspire Dashboard，零配置体验。

**Independent Test**: 克隆项目后执行 `dotnet run --project Backend/CoreSRE.AppHost`，验证控制台输出 Dashboard URL，Dashboard 中显示 postgres、coresre、api 三个资源均为 Running 状态。

**Covers**: FR-001, FR-002, FR-003, FR-004, FR-010, FR-011

### Implementation for User Story 1

- [x] T012 [US1] Implement AppHost `Program.cs` with `DistributedApplication.CreateBuilder(args)`, `AddPostgres("postgres").WithDataVolume().AddDatabase("coresre")`, `AddProject<Projects.CoreSRE>("api").WithReference(db).WaitFor(db).WithHttpHealthCheck("/health")`, and `builder.Build().Run()` in `Backend/CoreSRE.AppHost/Program.cs`
- [x] T013 [US1] Create AppHost launch settings with Aspire Dashboard configuration in `Backend/CoreSRE.AppHost/Properties/launchSettings.json`
- [x] T014 [US1] Update API `Program.cs` to add `builder.AddServiceDefaults()` at the top of service registration, add `builder.EnrichNpgsqlDbContext<AppDbContext>()` after Infrastructure DI, add `app.MapDefaultEndpoints()` in middleware pipeline, and remove the manual `/api/health` endpoint in `Backend/CoreSRE/Program.cs`

**Checkpoint**: User Story 1 fully functional. Run `dotnet run --project Backend/CoreSRE.AppHost` → PostgreSQL container starts, API connects, Dashboard shows all resources Running. Verify with `curl http://localhost:{port}/health` → HTTP 200.

---

## Phase 4: User Story 2 — 服务健康检查与依赖等待 (Priority: P1)

**Goal**: API 服务在 PostgreSQL 就绪前处于等待状态，就绪后暴露 `/health`（含数据库检查）和 `/alive`（仅进程存活）标准端点。

**Independent Test**: 启动 AppHost，观察 API 在 PostgreSQL 就绪前显示 "Waiting"，就绪后转 "Running"；`GET /health` 返回 200 含 DbContext 检查；`GET /alive` 返回 200 仅含 self 检查。

**Covers**: FR-004, FR-007, FR-010

**Note**: This story's implementation is largely covered by Phase 2 (ServiceDefaults health check methods) and Phase 3 (AppHost `WaitFor` + API `MapDefaultEndpoints`). This phase focuses on **verification and edge case handling**.

### Implementation for User Story 2

- [ ] T015 [US2] Verify `WaitFor(db)` behavior — start AppHost, confirm API resource shows "Waiting" state in Dashboard while PostgreSQL is starting, then transitions to "Running" after PostgreSQL health check passes (manual verification step, document results)
- [ ] T016 [US2] Verify `/health` endpoint returns HTTP 200 with aggregated health report including DbContext health check when PostgreSQL is reachable, per contract in `specs/001-aspire-apphost-setup/contracts/health-api.yaml`
- [ ] T017 [US2] Verify `/alive` endpoint returns HTTP 200 with only "live"-tagged checks (self check), not including database check, per contract in `specs/001-aspire-apphost-setup/contracts/health-api.yaml`

**Checkpoint**: Health endpoints work as specified. `/health` returns DB status. `/alive` returns only process liveness. WaitFor blocks API until DB ready.

---

## Phase 5: User Story 3 — 全链路可观测性自动配置 (Priority: P1)

**Goal**: API 请求的 Traces、Metrics、Logs 自动导出到 Aspire Dashboard，`/health` 和 `/alive` 请求不出现在 Traces 中。

**Independent Test**: 启动 AppHost，向 API 发送请求，打开 Aspire Dashboard 验证 Traces 页面有请求 Span、Metrics 页面有 HTTP 指标和 Runtime 指标、Logs 页面有结构化日志且可按服务名过滤。

**Covers**: FR-006, FR-010

**Note**: OTel configuration is implemented in Phase 2 (T009). This phase focuses on **end-to-end verification**.

### Implementation for User Story 3

- [ ] T018 [US3] Verify Traces — send HTTP requests to API, confirm Trace Spans appear in Aspire Dashboard within 5 seconds (SC-003), including HTTP method, path, status code, and duration
- [ ] T019 [US3] Verify Metrics — confirm Aspire Dashboard Metrics page shows ASP.NET Core HTTP metrics (request rate, latency distribution, error rate) and .NET Runtime metrics (GC, thread pool)
- [ ] T020 [US3] Verify Logs — confirm Aspire Dashboard Logs page shows structured logs with Message, Scope, LogLevel, and filtering by service name "api" works (SC-004)
- [ ] T021 [US3] Verify health endpoint trace filtering — send requests to `/health` and `/alive`, confirm these do NOT appear in Traces page (filtered by ServiceDefaults)

**Checkpoint**: Full observability pipeline verified. Traces, Metrics, and Logs all visible in Aspire Dashboard. Health check requests filtered from traces.

---

## Phase 6: User Story 4 — HTTP 客户端弹性策略 (Priority: P2)

**Goal**: 所有通过 `IHttpClientFactory` 创建的 `HttpClient` 实例自动具备 Polly 重试、超时和熔断策略。

**Independent Test**: 在 API 中注入 `IHttpClientFactory`，发送请求到不可达服务，验证自动重试行为和超时保护。

**Covers**: FR-008, FR-009

**Note**: HTTP resilience is configured in Phase 2 (T008, `ConfigureHttpClientDefaults` with `AddStandardResilienceHandler`). This phase focuses on **verification**.

### Implementation for User Story 4

- [ ] T022 [US4] Verify HTTP resilience configuration — confirm `IHttpClientFactory` is available via DI, and created HttpClient instances have Polly standard resilience handler attached (retry, timeout, circuit breaker, rate limiter) by inspecting ServiceDefaults `ConfigureHttpClientDefaults` configuration
- [ ] T023 [US4] Verify service discovery — confirm `AddServiceDiscovery()` is configured on HttpClient defaults, enabling service-name-based resolution via Aspire resource names

**Checkpoint**: HTTP clients have automatic resilience and service discovery. Verified via configuration inspection.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, documentation, and cleanup

- [x] T024 [P] Run full solution build — `dotnet build Backend/CoreSRE/CoreSRE.slnx` — ensure zero errors and zero warnings
- [ ] T025 [P] Validate quickstart.md — follow the 3-step process in `specs/001-aspire-apphost-setup/quickstart.md` from scratch, verify all steps succeed
- [ ] T026 Verify SC-001 (≤3 steps from clone to running) — document the exact steps and confirm compliance
- [ ] T027 Verify SC-002 (`GET /health` responds within 1 second with HTTP 200 including DB check)
- [ ] T028 Verify SC-005 (PostgreSQL unreachable → `/health` returns HTTP 503)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 completion — BLOCKS all user stories
- **User Story 1 (Phase 3)**: Depends on Phase 2 — AppHost + API integration
- **User Story 2 (Phase 4)**: Depends on Phase 3 — requires running AppHost for verification
- **User Story 3 (Phase 5)**: Depends on Phase 3 — requires running AppHost for verification
- **User Story 4 (Phase 6)**: Depends on Phase 2 — configuration verification only
- **Polish (Phase 7)**: Depends on Phases 3-6

### User Story Dependencies

- **User Story 1 (P1)**: Depends on Foundational (Phase 2). This is the MVP — cannot be parallelized with other stories because other stories need a running AppHost to verify.
- **User Story 2 (P1)**: Depends on User Story 1 (running AppHost needed for health check verification).
- **User Story 3 (P1)**: Depends on User Story 1 (running AppHost needed for OTel verification). Can run in parallel with User Story 2.
- **User Story 4 (P2)**: Depends only on Foundational. Can run in parallel with User Stories 2 and 3 (configuration inspection, not runtime verification).

### Within Each User Story

- Models/configuration before services
- Services before endpoints
- Core implementation before verification
- Story complete before moving to next priority

### Parallel Opportunities

- **Phase 1**: T001 and T002 can run in parallel (different project files). T004 and T005 can run in parallel. T006 and T007 can run in parallel (different files).
- **Phase 2**: T008 → T009, T010, T011 are sequential (same file `Extensions.cs`)
- **Phase 3**: T012 and T013 can run in parallel (different files). T014 depends on T008/T012.
- **Phase 4-5**: US2 verification (T015-T017) and US3 verification (T018-T021) can run in parallel after Phase 3.
- **Phase 6**: US4 (T022-T023) can run in parallel with Phase 4-5.
- **Phase 7**: T024 and T025 can run in parallel.

---

## Parallel Example: Phase 1 Setup

```
# Group 1: Create new project files (parallel — different files)
T001: Create CoreSRE.ServiceDefaults.csproj
T002: Create CoreSRE.AppHost.csproj

# Group 2: Update existing project references (parallel — different files)  
T004: Add ServiceDefaults ref to CoreSRE.csproj
T005: Add CoreSRE ref to AppHost.csproj

# Group 3: Connection string migration (parallel — different files)
T006: Update appsettings.json key name
T007: Update Infrastructure DependencyInjection.cs
```

## Parallel Example: Verification Phases (after Phase 3 complete)

```
# These can run simultaneously once AppHost is running:
US2 Verification: T015, T016, T017 (health check endpoints)
US3 Verification: T018, T019, T020, T021 (OTel in Dashboard)
US4 Verification: T022, T023 (resilience config inspection)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001-T007)
2. Complete Phase 2: Foundational — ServiceDefaults (T008-T011)
3. Complete Phase 3: User Story 1 — AppHost + API integration (T012-T014)
4. **STOP and VALIDATE**: `dotnet run --project Backend/CoreSRE.AppHost` → all resources Running
5. This is the MVP — a working one-command dev environment

### Incremental Delivery

1. Setup + Foundational → Projects compile, ServiceDefaults ready
2. Add User Story 1 → One-command startup works → **MVP! 🎯**
3. Verify User Story 2 → Health checks confirmed → Readiness/Liveness endpoints working
4. Verify User Story 3 → OTel confirmed → Full observability pipeline
5. Verify User Story 4 → Resilience confirmed → Production-grade HTTP clients
6. Polish → All success criteria validated, documentation verified

### Single Developer Strategy (Recommended)

This spec is best implemented by a single developer sequentially:

1. Phase 1 → Phase 2 → Phase 3 (MVP milestone)
2. Phase 4 + Phase 5 + Phase 6 (verification, can be interleaved)
3. Phase 7 (final polish)

Total estimated effort: ~2-3 hours for implementation (Phases 1-3), ~1 hour for verification (Phases 4-7).

---

## Notes

- All implementation tasks produce configuration/infrastructure code, not business logic
- ServiceDefaults follows the official Aspire template pattern exactly (see research.md R6)
- Connection string key migration from `"DefaultConnection"` to `"coresre"` (see research.md R2)
- `EnrichNpgsqlDbContext<AppDbContext>()` preserves DDD layering (see research.md R3)
- `.WithDataVolume()` on PostgreSQL ensures dev data survives restarts (see research.md R5)
- Verification tasks (Phases 4-6) require Docker Desktop running
- Domain and Application layers are completely untouched by this spec
