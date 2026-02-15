# Implementation Plan: 工作流实时推送（SignalR）

**Branch**: `017-workflow-signalr-push` | **Date**: 2026-02-14 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/017-workflow-signalr-push/spec.md`

## Summary

为工作流执行引擎引入 SignalR 实时推送能力。在 Domain 层定义 `IWorkflowExecutionNotifier` 接口（7 个生命周期事件），在 Infrastructure 层通过 `SignalRWorkflowNotifier` 实现（使用强类型 `Hub<IWorkflowClient>`），在执行引擎的关键节点生命周期钩子处调用通知接口，将节点状态变更实时推送到前端。前端通过 `@microsoft/signalr` 建立 WebSocket 连接，接收事件后更新 `DagExecutionViewer` 的节点可视化状态。支持断线重连（`withAutomaticReconnect` + REST 回退）和多用户同时观察（SignalR Groups）。

## Technical Context

**Language/Version**: C# / .NET 10, TypeScript 5.9 / React 19.2  
**Primary Dependencies**: ASP.NET Core SignalR (framework-included), `@microsoft/signalr` (npm), MediatR, AutoMapper  
**Storage**: PostgreSQL (EF Core 10) — 不涉及新表或 migration，节点执行状态已持久化在 `WorkflowExecution.NodeExecutions`  
**Testing**: xUnit + Moq (`CoreSRE.Application.Tests`, `CoreSRE.Infrastructure.Tests`)  
**Target Platform**: Linux/Windows server + SPA (Vite, localhost:5173)  
**Project Type**: Web application (backend + frontend)  
**Performance Goals**: <500ms event latency, 30fps DAG rendering with 50 nodes, 10 concurrent observers  
**Constraints**: 单实例部署（无 Azure SignalR 背板），WebSocket 默认传输，JSON 协议  
**Scale/Scope**: 每次执行最多 100 个节点，一次执行最多 10 个观察者

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Spec-Driven Development | ✅ PASS | `specs/017-workflow-signalr-push/spec.md` 已完成，含 3 User Stories, 11 FRs, 5 SCs |
| II. TDD (NON-NEGOTIABLE) | ✅ PASS | 实现前将编写测试：`SignalRWorkflowNotifier` 单元测试 (mock `IHubContext`)、`WorkflowEngine` 集成测试 (验证 notifier 被调用), 前端 hook 测试 |
| III. DDD Layer Rules | ✅ PASS | `IWorkflowExecutionNotifier` 接口放在 `Domain/Interfaces`，实现 `SignalRWorkflowNotifier` 放在 `Infrastructure/Services`，`WorkflowHub` 放在 API 层 |
| IV. Test Immutability (NON-NEGOTIABLE) | ✅ PASS | 不修改任何已有测试。新增接口不影响 `WorkflowEngine` 现有测试（接口默认可选注入）|
| V. Interface-Before-Implementation | ✅ PASS | 先定义 `IWorkflowExecutionNotifier` 接口 + `IWorkflowClient` 接口，然后实现 `SignalRWorkflowNotifier` 和 `WorkflowHub` |
| Dev Workflow (5-step) | ✅ PASS | Spec ✅ → Test (Red) → Interface → Implementation → Verify (Green) |
| DDD Dependency Direction | ✅ PASS | Domain ← Application ← Infrastructure → API。`IWorkflowExecutionNotifier` in Domain, 被 Infrastructure 的 `WorkflowEngine` 消费，实现在 Infrastructure |

**Gate Result**: ALL PASS — proceed to Phase 0.

### Post-Design Re-check (after Phase 1)

| Principle | Status | Post-Design Verification |
|-----------|--------|--------------------------|
| I. SDD | ✅ PASS | spec.md → plan.md → data-model.md + contracts/signalr-hub.md 全链路完整 |
| II. TDD | ✅ PASS | 测试计划：SignalRWorkflowNotifierTests (mock IHubContext), WorkflowEngine notifier 集成测试 |
| III. DDD | ✅ PASS | `IWorkflowExecutionNotifier` ∈ Domain/Interfaces, `SignalRWorkflowNotifier` ∈ Infrastructure/Services, `WorkflowHub` ∈ API/Hubs — 无依赖反转 |
| IV. Test Immutability | ✅ PASS | 不修改已有测试。`WorkflowEngine` 新增可选参数 + NullNotifier 默认值，向后兼容 |
| V. Interface-First | ✅ PASS | `IWorkflowExecutionNotifier` + `IWorkflowClient` 接口先于实现定义 |

**Post-Design Gate Result**: ALL PASS — ready for Phase 2 (tasks).

## Project Structure

### Documentation (this feature)

```text
specs/017-workflow-signalr-push/
├── plan.md              # This file
├── research.md          # Phase 0: SignalR patterns research
├── data-model.md        # Phase 1: Entities and event DTOs
├── quickstart.md        # Phase 1: Setup and dev guide
├── contracts/           # Phase 1: SignalR hub contract
│   └── signalr-hub.md
└── tasks.md             # Phase 2 output (NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
Backend/
├── CoreSRE.Domain/
│   └── Interfaces/
│       └── IWorkflowExecutionNotifier.cs    # NEW — 7-method notifier interface
├── CoreSRE.Infrastructure/
│   ├── Services/
│   │   ├── WorkflowEngine.cs                # MODIFIED — inject + call IWorkflowExecutionNotifier
│   │   └── SignalRWorkflowNotifier.cs       # NEW — IWorkflowExecutionNotifier impl via IHubContext
│   └── DependencyInjection.cs               # MODIFIED — register SignalRWorkflowNotifier
├── CoreSRE/
│   ├── Hubs/
│   │   ├── IWorkflowClient.cs               # NEW — strongly-typed client interface
│   │   └── WorkflowHub.cs                   # NEW — Hub<IWorkflowClient>
│   └── Program.cs                           # MODIFIED — AddSignalR(), MapHub
├── CoreSRE.Infrastructure.Tests/
│   └── Services/
│       └── SignalRWorkflowNotifierTests.cs   # NEW — unit tests
└── CoreSRE.Application.Tests/
    └── Workflows/
        └── WorkflowEngineNotifierTests.cs    # NEW — integration tests

Frontend/
├── src/
│   ├── hooks/
│   │   └── useWorkflowSignalR.ts            # NEW — SignalR connection hook
│   ├── pages/
│   │   └── WorkflowExecutionDetailPage.tsx   # MODIFIED — replace polling with SignalR
│   └── lib/
│       └── signalr.ts                       # NEW — HubConnection factory helper
└── package.json                             # MODIFIED — add @microsoft/signalr
```

**Structure Decision**: Web application (Option 2). All new backend code follows existing DDD layers. Hub and client interface placed in API project (`CoreSRE/Hubs/`) as they are presentation-layer concerns. Domain interface in `Domain/Interfaces/`, implementation in `Infrastructure/Services/`.

## Complexity Tracking

> No constitution violations. No complexity justifications needed.
