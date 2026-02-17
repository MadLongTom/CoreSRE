# Tasks: SPEC-204 Deployment / Git 查询 + Webhook + 定时健康检查

**Input**: [DATASOURCE-SPEC-INDEX](../../docs/specs/DATASOURCE-SPEC-INDEX.md) SPEC-204
**Prerequisites**: SPEC-200 ✅, SPEC-201 ✅, SPEC-202 ✅, SPEC-203 ✅

## Format: `[ID] [P?] Description`

- **[P]**: Can run in parallel (different files, no dependencies)

---

## Phase 1 — NuGet 依赖

- [x] T001 添加 `Octokit` NuGet 包到 `CoreSRE.Infrastructure.csproj`

## Phase 2 — KubernetesQuerier

- [x] T002 创建 `KubernetesQuerier` `Backend/CoreSRE.Infrastructure/Services/DataSources/KubernetesQuerier.cs`
- [x] T003 注册 `KubernetesQuerier` DI `Backend/CoreSRE.Infrastructure/DependencyInjection.cs`

## Phase 3 — ArgoCDQuerier

- [x] T004 创建 `ArgoCDQuerier` `Backend/CoreSRE.Infrastructure/Services/DataSources/ArgoCDQuerier.cs`
- [x] T005 注册 `ArgoCDQuerier` DI `Backend/CoreSRE.Infrastructure/DependencyInjection.cs`

## Phase 4 — GitHubQuerier

- [x] T006 创建 `GitHubQuerier` `Backend/CoreSRE.Infrastructure/Services/DataSources/GitHubQuerier.cs`
- [x] T007 注册 `GitHubQuerier` DI `Backend/CoreSRE.Infrastructure/DependencyInjection.cs`

## Phase 5 — GitLabQuerier

- [x] T008 创建 `GitLabQuerier` `Backend/CoreSRE.Infrastructure/Services/DataSources/GitLabQuerier.cs`
- [x] T009 注册 `GitLabQuerier` DI `Backend/CoreSRE.Infrastructure/DependencyInjection.cs`

## Phase 6 — DataSourceFunctionFactory 补全

- [x] T010 实现 `GenerateDeploymentFunctions` `Backend/CoreSRE.Infrastructure/Services/DataSources/DataSourceFunctionFactory.cs`
- [x] T011 实现 `GenerateGitFunctions` `Backend/CoreSRE.Infrastructure/Services/DataSources/DataSourceFunctionFactory.cs`

## Phase 7 — Webhook 端点

- [x] T012 创建 `WebhookEndpoints` `Backend/CoreSRE/Endpoints/WebhookEndpoints.cs`
- [x] T013 注册 Webhook 端点 `Backend/CoreSRE/Program.cs`

## Phase 8 — 定时健康检查 BackgroundService

- [x] T014 创建 `DataSourceHealthCheckBackgroundService` `Backend/CoreSRE.Infrastructure/Services/DataSources/DataSourceHealthCheckBackgroundService.cs`
- [x] T015 注册 BackgroundService DI `Backend/CoreSRE.Infrastructure/DependencyInjection.cs`

## Phase 9 — 构建与验证

- [x] T016 dotnet build 验证
- [x] T017 dotnet test 验证
