# Tasks: 工作流实时推送（SignalR）

**Input**: Design documents from `/specs/017-workflow-signalr-push/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/signalr-hub.md ✅, quickstart.md ✅

**Tests**: Included — Constitution mandates TDD (NON-NEGOTIABLE).

**Organization**: Tasks grouped by user story. Each story is independently testable.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Backend**: `Backend/CoreSRE.Domain/`, `Backend/CoreSRE.Infrastructure/`, `Backend/CoreSRE/`
- **Frontend**: `Frontend/src/`
- **Tests**: `Backend/CoreSRE.Infrastructure.Tests/`, `Backend/CoreSRE.Application.Tests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Install dependencies, scaffold directories

- [X] T001 Install `@microsoft/signalr` npm package in `Frontend/package.json`
- [X] T002 [P] Create `Backend/CoreSRE/Hubs/` directory for SignalR hub files
- [X] T003 [P] Add `builder.Services.AddSignalR()` in `Backend/CoreSRE/Program.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Define interfaces and null implementations that ALL user stories depend on. These establish the notifier contract consumed by the execution engine.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Tests for Foundational Phase ⚠️

> **Write tests FIRST — they MUST FAIL before implementation (Red phase)**

- [X] T004 [P] Write unit tests for `NullWorkflowExecutionNotifier` (all 7 methods return completed tasks) in `Backend/CoreSRE.Infrastructure.Tests/Services/NullWorkflowExecutionNotifierTests.cs`

### Interfaces

- [X] T005 [P] Define `IWorkflowExecutionNotifier` interface with 7 async methods (`ExecutionStartedAsync`, `NodeExecutionStartedAsync`, `NodeExecutionCompletedAsync`, `NodeExecutionFailedAsync`, `NodeExecutionSkippedAsync`, `ExecutionCompletedAsync`, `ExecutionFailedAsync`) in `Backend/CoreSRE.Domain/Interfaces/IWorkflowExecutionNotifier.cs`
- [X] T006 [P] Define `IWorkflowClient` strongly-typed Hub client interface with 7 methods (`ExecutionStarted`, `NodeExecutionStarted`, `NodeExecutionCompleted`, `NodeExecutionFailed`, `NodeExecutionSkipped`, `ExecutionCompleted`, `ExecutionFailed`) in `Backend/CoreSRE/Hubs/IWorkflowClient.cs`

### Implementation

- [X] T007 Implement `NullWorkflowExecutionNotifier` (all methods return `Task.CompletedTask`) in `Backend/CoreSRE.Infrastructure/Services/NullWorkflowExecutionNotifier.cs`
- [X] T008 Register `IWorkflowExecutionNotifier` → `NullWorkflowExecutionNotifier` as Scoped in `Backend/CoreSRE.Infrastructure/DependencyInjection.cs`
- [X] T009 Add `IWorkflowExecutionNotifier` as constructor parameter to `WorkflowEngine` in `Backend/CoreSRE.Infrastructure/Services/WorkflowEngine.cs`

**Checkpoint**: All existing tests still pass. `WorkflowEngine` compiles with new parameter resolved via DI. `NullWorkflowExecutionNotifier` tests green.

---

## Phase 3: User Story 1 — 实时观察工作流执行进度 (Priority: P1) 🎯 MVP

**Goal**: 后端执行引擎在节点生命周期钩子处调用 `IWorkflowExecutionNotifier`，`SignalRWorkflowNotifier` 通过 SignalR 将事件推送到前端。前端 `useWorkflowSignalR` hook 接收事件并更新 `DagExecutionViewer` 节点颜色。

**Independent Test**: 启动 3 节点顺序工作流，在执行页面观察节点颜色依次 灰→蓝→绿 过渡，无需手动刷新。

### Tests for User Story 1 ⚠️

> **Write tests FIRST — they MUST FAIL before implementation (Red phase)**

- [X] T010 [P] [US1] Write unit tests for `SignalRWorkflowNotifier` — mock `IHubContext<WorkflowHub, IWorkflowClient>`, verify each of 7 methods calls correct group method with correct args — in `Backend/CoreSRE.Infrastructure.Tests/Services/SignalRWorkflowNotifierTests.cs`
- [X] T011 [P] [US1] Write integration tests for `WorkflowEngine` notifier calls — mock `IWorkflowExecutionNotifier`, execute a simple workflow, verify `ExecutionStartedAsync`, `NodeExecutionStartedAsync`, `NodeExecutionCompletedAsync`, `ExecutionCompletedAsync` called in correct order — in `Backend/CoreSRE.Infrastructure.Tests/Services/WorkflowEngineNotifierTests.cs`

### Backend Implementation for User Story 1

- [X] T012 [US1] Implement `SignalRWorkflowNotifier` — inject `IHubContext<WorkflowHub, IWorkflowClient>`, delegate 7 methods to `_hubContext.Clients.Group($"execution:{executionId}").MethodName(...)` — in `Backend/CoreSRE.Infrastructure/Services/SignalRWorkflowNotifier.cs`
- [X] T013 [US1] Implement `WorkflowHub` — extend `Hub<IWorkflowClient>`, add `JoinExecution(Guid executionId)` and `LeaveExecution(Guid executionId)` methods using `Groups.AddToGroupAsync`/`RemoveFromGroupAsync` with group name `execution:{executionId}` — in `Backend/CoreSRE/Hubs/WorkflowHub.cs`
- [X] T014 [US1] Add `app.MapHub<WorkflowHub>("/hubs/workflow")` endpoint mapping in `Backend/CoreSRE/Program.cs`
- [X] T015 [US1] Update DI registration: replace `NullWorkflowExecutionNotifier` with `SignalRWorkflowNotifier` as Scoped `IWorkflowExecutionNotifier` in `Backend/CoreSRE.Infrastructure/DependencyInjection.cs`
- [X] T016 [US1] Insert notifier calls at 7 lifecycle hook points in `WorkflowEngine.ExecuteAsync()` — after each `_executionRepo.UpdateAsync()` call: `ExecutionStartedAsync`, `NodeExecutionStartedAsync`, `NodeExecutionCompletedAsync`, `NodeExecutionFailedAsync`, `NodeExecutionSkippedAsync`, `ExecutionCompletedAsync`, `ExecutionFailedAsync` — in `Backend/CoreSRE.Infrastructure/Services/WorkflowEngine.cs`

### Frontend Implementation for User Story 1

- [X] T017 [P] [US1] Create `signalr.ts` HubConnection factory helper — `createWorkflowHubConnection(url)` returning configured `HubConnection` with `withAutomaticReconnect()` and JSON protocol — in `Frontend/src/lib/signalr.ts`
- [X] T018 [US1] Create `useWorkflowSignalR` hook — accepts `executionId`, manages connection lifecycle via `useEffect`, registers 7 `.on()` handlers before `.start()`, invokes `JoinExecution` on connect, returns `connectionState` and accepts event callback props — in `Frontend/src/hooks/useWorkflowSignalR.ts`
- [X] T019 [US1] Integrate `useWorkflowSignalR` into `WorkflowExecutionDetailPage` — replace `setInterval(fetchData, 3000)` polling with SignalR events, update `nodeExecutions` state on each event, pass updated props to `DagExecutionViewer` — in `Frontend/src/pages/WorkflowExecutionDetailPage.tsx`

**Checkpoint**: Backend tests green. Execute a workflow → observe real-time node color transitions in browser. No polling. All FR-001 through FR-008, FR-010, FR-011 validated.

---

## Phase 4: User Story 2 — 断线重连与状态恢复 (Priority: P2)

**Goal**: 断线后自动重连并恢复最新执行状态。SignalR 连接不可用时降级为 REST 轮询。

**Independent Test**: 工作流执行一半时关闭页面，重新打开后 DAG 图在 3 秒内恢复到当前真实状态。

### Tests for User Story 2 ⚠️

> **Write tests FIRST — they MUST FAIL before implementation (Red phase)**

- [X] T020 [P] [US2] Write integration test for reconnection state recovery — verify that `WorkflowExecutionDetailPage` loads REST snapshot on mount and applies subsequent SignalR events on top — in `Backend/CoreSRE.Infrastructure.Tests/Services/WorkflowEngineNotifierTests.cs` (add test case for mid-execution state sync)

### Implementation for User Story 2

- [X] T021 [US2] Add `onreconnected` handler to `useWorkflowSignalR` — re-invoke `JoinExecution(executionId)` and trigger REST state refresh via callback — in `Frontend/src/hooks/useWorkflowSignalR.ts`
- [X] T022 [US2] Add `onclose` handler with REST polling fallback — when connection permanently closes, start `setInterval(fetchData, 5000)` and show "实时连接不可用" toast — in `Frontend/src/hooks/useWorkflowSignalR.ts`
- [X] T023 [US2] Update `WorkflowExecutionDetailPage` to load REST snapshot first, then layer SignalR events on top — ensure mount sequence: (1) REST fetch → render → (2) SignalR connect + join → (3) real-time updates — in `Frontend/src/pages/WorkflowExecutionDetailPage.tsx`

**Checkpoint**: FR-009 validated. SC-004 (3s recovery) and SC-005 (5s fallback) testable. Disconnect network → reconnect → state accurate.

---

## Phase 5: User Story 3 — 多用户同时观察同一执行 (Priority: P3)

**Goal**: 多个浏览器标签页/用户同时观察同一执行时，所有观察者同步收到事件流。

**Independent Test**: 两个浏览器标签页打开同一执行页面，执行工作流后两个页面同步更新节点状态。

### Implementation for User Story 3

- [X] T024 [US3] Add `OnDisconnectedAsync` override to `WorkflowHub` — log disconnection, verify framework auto-removes from group — in `Backend/CoreSRE/Hubs/WorkflowHub.cs`
- [X] T025 [US3] Add logging to `SignalRWorkflowNotifier` — log group name and event type on each push for observability of multi-observer scenarios — in `Backend/CoreSRE.Infrastructure/Services/SignalRWorkflowNotifier.cs`

**Checkpoint**: SC-003 (10 concurrent observers) testable. Two tabs sync. Closing one tab doesn't affect the other.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Edge case handling, cleanup, validation

- [X] T026 [P] Add error handling in `SignalRWorkflowNotifier` — catch and log exceptions from SignalR push without failing the execution engine — in `Backend/CoreSRE.Infrastructure/Services/SignalRWorkflowNotifier.cs`
- [X] T027 [P] Add connection state indicator UI — show WebSocket connection status badge (connected/reconnecting/disconnected) in execution detail page header — in `Frontend/src/pages/WorkflowExecutionDetailPage.tsx`
- [X] T028 Run `quickstart.md` validation — build backend, run all tests, build frontend, start AppHost, execute workflow end-to-end

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Phase 2 — BLOCKS US2 (US2 builds on US1's hook)
- **US2 (Phase 4)**: Depends on US1 completion (extends `useWorkflowSignalR` and page integration)
- **US3 (Phase 5)**: Depends on Phase 2 only — can run in parallel with US1 if desired (backend-only additions)
- **Polish (Phase 6)**: Depends on US1 at minimum

### Within Each Phase

- Tests MUST be written and FAIL before implementation (TDD Red-Green)
- Interfaces before implementations
- Backend before frontend (within same story)
- Notifier calls in engine after SignalRWorkflowNotifier exists

### Parallel Opportunities

**Phase 1** (all parallel):
```
T001 (npm install) | T002 (create dir) | T003 (AddSignalR)
```

**Phase 2** (tests + interfaces parallel, then implementations sequential):
```
T004 (NullNotifier test) | T005 (IWorkflowExecutionNotifier) | T006 (IWorkflowClient)
  └─→ T007 (NullNotifier impl) → T008 (DI register) → T009 (Engine param)
```

**Phase 3** (tests parallel, then backend sequential, frontend parallel after T017):
```
T010 (SignalR notifier tests) | T011 (Engine notifier tests)
  └─→ T012 (SignalRWorkflowNotifier) → T013 (WorkflowHub) → T014 (MapHub) → T015 (DI swap) → T016 (Engine hooks)
T017 (signalr.ts helper) ── can start after T006 ──→ T018 (hook) → T019 (page integration)
```

**Phase 5** (parallel with Phase 3/4 if desired):
```
T024 (Hub disconnect) | T025 (Notifier logging)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (3 tasks)
2. Complete Phase 2: Foundational (6 tasks)
3. Complete Phase 3: User Story 1 (10 tasks)
4. **STOP and VALIDATE**: Execute workflow → observe real-time DAG node color transitions
5. Deploy/demo if ready — this alone eliminates the manual-refresh pain point

### Incremental Delivery

1. Setup + Foundational → Foundation ready (9 tasks)
2. Add US1 → Real-time execution observation works (MVP! 19 tasks total)
3. Add US2 → Robust reconnection and REST fallback (23 tasks total)
4. Add US3 → Multi-observer support (25 tasks total)
5. Polish → Production-ready (28 tasks total)

### Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story
- Each story is independently completable and testable
- Verify tests fail before implementing (Constitution Principle II)
- Commit after each task or logical group
- Notifier calls MUST be placed AFTER `_executionRepo.UpdateAsync()` (data-model.md invariant)
