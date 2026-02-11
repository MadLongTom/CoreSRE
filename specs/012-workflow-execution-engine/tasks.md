# Tasks: 工作流执行引擎（顺序 + 并行 + 条件分支）

**Input**: Design documents from `/specs/012-workflow-execution-engine/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api-contract.md, quickstart.md

**Tests**: Included — Constitution Check mandates TDD (Red-Green-Refactor). Tests are written FIRST and must FAIL before implementation.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)
- Include exact file paths in descriptions

## Path Conventions

- **Backend**: `Backend/CoreSRE.Domain/`, `Backend/CoreSRE.Application/`, `Backend/CoreSRE.Infrastructure/`, `Backend/CoreSRE/`
- **Tests**: `Backend/CoreSRE.Application.Tests/`, `Backend/CoreSRE.Infrastructure.Tests/`
- Paths relative to repository root (`E:\CoreSRE`)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add NuGet dependencies and create shared enum types needed by all user stories.

- [x] T001 Add NuGet package `Microsoft.Agents.AI.Workflows` 1.0.0-preview.260209.1 to Backend/CoreSRE.Infrastructure/CoreSRE.Infrastructure.csproj and `JsonPath.Net` 3.0.0 to Backend/CoreSRE.Application/CoreSRE.Application.csproj
- [x] T002 [P] Create ExecutionStatus enum (Pending, Running, Completed, Failed, Canceled) in Backend/CoreSRE.Domain/Enums/ExecutionStatus.cs
- [x] T003 [P] Create NodeExecutionStatus enum (Pending, Running, Completed, Failed, Skipped) in Backend/CoreSRE.Domain/Enums/NodeExecutionStatus.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain entities, value objects, interfaces, DTOs, persistence configuration, and repository. ALL user stories depend on this phase.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Interfaces & Value Objects

- [x] T004 [P] Create NodeExecutionVO value object (NodeId, Status, Input, Output, ErrorMessage, StartedAt, CompletedAt) in Backend/CoreSRE.Domain/ValueObjects/NodeExecutionVO.cs — use `string?` for Input/Output per data-model.md
- [x] T005 [P] Create IWorkflowExecutionRepository interface extending IRepository\<WorkflowExecution\> with GetByWorkflowIdAsync and GetByStatusAsync in Backend/CoreSRE.Domain/Interfaces/IWorkflowExecutionRepository.cs
- [x] T006 [P] Create IWorkflowEngine interface with ExecuteAsync(WorkflowExecution, CancellationToken) in Backend/CoreSRE.Domain/Interfaces/IWorkflowEngine.cs
- [x] T007 [P] Create IConditionEvaluator interface with Evaluate and TryEvaluate methods in Backend/CoreSRE.Application/Interfaces/IConditionEvaluator.cs

### Aggregate Root (TDD)

- [x] T008 Write WorkflowExecution aggregate root unit tests covering Create factory, Start, StartNode, CompleteNode, FailNode, SkipNode, Complete, Fail, and state machine guards in Backend/CoreSRE.Infrastructure.Tests/Workflows/WorkflowExecutionTests.cs
- [x] T009 Implement WorkflowExecution aggregate root entity with factory method, domain methods, and state transitions per data-model.md in Backend/CoreSRE.Domain/Entities/WorkflowExecution.cs

### DTOs & Mapping

- [x] T010 [P] Create NodeExecutionDto record (NodeId, Status, Input, Output, ErrorMessage, StartedAt, CompletedAt) in Backend/CoreSRE.Application/Workflows/DTOs/NodeExecutionDto.cs
- [x] T011 [P] Create WorkflowExecutionDto record (Id, WorkflowDefinitionId, Status, Input, Output, ErrorMessage, StartedAt, CompletedAt, TraceId, NodeExecutions, CreatedAt) in Backend/CoreSRE.Application/Workflows/DTOs/WorkflowExecutionDto.cs
- [x] T012 [P] Create WorkflowExecutionSummaryDto record (Id, Status, StartedAt, CompletedAt, CreatedAt) in Backend/CoreSRE.Application/Workflows/DTOs/WorkflowExecutionSummaryDto.cs
- [x] T013 Add WorkflowExecution → WorkflowExecutionDto, WorkflowExecution → WorkflowExecutionSummaryDto, and NodeExecutionVO → NodeExecutionDto mappings to Backend/CoreSRE.Application/Workflows/DTOs/WorkflowMappingProfile.cs

### Persistence

- [x] T014 [P] Create WorkflowExecutionConfiguration with snake_case table mapping, enum-as-string conversions, JSONB for GraphSnapshot (OwnsOne/ToJson) and NodeExecutions (OwnsMany/ToJson), and WorkflowDefinitionId index in Backend/CoreSRE.Infrastructure/Persistence/Configurations/WorkflowExecutionConfiguration.cs
- [x] T015 [P] Add `DbSet<WorkflowExecution> WorkflowExecutions` to Backend/CoreSRE.Infrastructure/Persistence/AppDbContext.cs
- [x] T016 Create WorkflowExecutionRepository implementing IWorkflowExecutionRepository with GetByWorkflowIdAsync and GetByStatusAsync in Backend/CoreSRE.Infrastructure/Persistence/WorkflowExecutionRepository.cs
- [x] T017 Register IWorkflowExecutionRepository → WorkflowExecutionRepository in Backend/CoreSRE.Infrastructure/DependencyInjection.cs

**Checkpoint**: Foundation ready — all domain model, persistence, and infrastructure in place. User story implementation can now begin.

---

## Phase 3: User Story 1 — 执行顺序编排工作流 (Priority: P1) 🎯 MVP

**Goal**: Engineers can submit a sequential workflow (A → B → C) for async execution, and the system creates a `WorkflowExecution` record, converts the DAG to an Agent Framework `Workflow`, executes nodes in topological order, and updates each node's status in real-time.

**Independent Test**: Create a 3-node sequential DAG Published workflow, POST `/api/workflows/{id}/execute`, verify the returned WorkflowExecution has correct node sequence and final output after execution completes.

### Tests for User Story 1 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T018 [P] [US1] Write ExecuteWorkflowCommandValidator tests covering: valid command passes, empty WorkflowDefinitionId fails, null input defaults to empty JSON in Backend/CoreSRE.Application.Tests/Workflows/Commands/ExecuteWorkflow/ExecuteWorkflowCommandValidatorTests.cs
- [x] T019 [P] [US1] Write ExecuteWorkflowCommandHandler tests covering: Draft workflow returns 400, non-existent workflow returns 404, invalid agent references return 400, Published workflow creates WorkflowExecution and enqueues to Channel in Backend/CoreSRE.Application.Tests/Workflows/Commands/ExecuteWorkflow/ExecuteWorkflowCommandHandlerTests.cs
- [x] T020 [P] [US1] Write WorkflowEngine sequential execution tests covering: 3-node sequential DAG executes in order, node output passes as next node input, node failure stops subsequent nodes in Backend/CoreSRE.Infrastructure.Tests/Workflows/WorkflowEngineTests.cs

### Implementation for User Story 1

- [x] T021 [P] [US1] Create ExecuteWorkflowCommand record (WorkflowDefinitionId, Input as JsonElement?) in Backend/CoreSRE.Application/Workflows/Commands/ExecuteWorkflow/ExecuteWorkflowCommand.cs
- [x] T022 [US1] Implement ExecuteWorkflowCommandValidator (WorkflowDefinitionId must not be empty) in Backend/CoreSRE.Application/Workflows/Commands/ExecuteWorkflow/ExecuteWorkflowCommandValidator.cs
- [x] T023 [US1] Implement ExecuteWorkflowCommandHandler: validate Published status (FR-014), validate agent/tool references exist (FR-015), snapshot graph (FR-023), create WorkflowExecution via factory, save via repository, enqueue to Channel\<ExecuteWorkflowRequest\>, return 202 with WorkflowExecutionDto in Backend/CoreSRE.Application/Workflows/Commands/ExecuteWorkflow/ExecuteWorkflowCommandHandler.cs
- [x] T024 [US1] Implement WorkflowEngine with sequential execution: topological sort, node-to-ExecutorBinding mapping (Agent → AIAgent.BindAsExecutor via IAgentResolver, Tool → FunctionExecutor), AddEdge for sequential chain, Build + InProcessExecution.RunAsync, per-node state updates via repository, 5-minute node timeout (FR-022) in Backend/CoreSRE.Infrastructure/Services/WorkflowEngine.cs
- [x] T025 [US1] Implement WorkflowExecutionBackgroundService: Channel\<ExecuteWorkflowRequest\> consumer, IServiceScopeFactory for scoped DI, call IWorkflowEngine.ExecuteAsync per request, match McpDiscoveryBackgroundService pattern in Backend/CoreSRE.Infrastructure/Services/WorkflowExecutionBackgroundService.cs
- [x] T026 [US1] Add `POST /{id}/execute` endpoint to WorkflowEndpoints: send ExecuteWorkflowCommand via MediatR, return 202 Accepted with WorkflowExecutionDto, handle 400/404 per api-contract.md in Backend/CoreSRE/Endpoints/WorkflowEndpoints.cs
- [x] T027 [US1] Register IWorkflowEngine → WorkflowEngine (Scoped), Channel\<ExecuteWorkflowRequest\> (Singleton), WorkflowExecutionBackgroundService (AddHostedService) in Backend/CoreSRE.Infrastructure/DependencyInjection.cs

**Checkpoint**: At this point, sequential workflow execution should be fully functional — POST execute returns 202, background service runs the workflow, node states update in real-time. This is the MVP.

---

## Phase 4: User Story 2 — 执行并行编排工作流 FanOut/FanIn (Priority: P1)

**Goal**: When a DAG contains FanOut/FanIn nodes, the engine concurrently executes all downstream nodes after FanOut and aggregates their outputs into a JSON array for the FanIn node.

**Independent Test**: Create a FanOut → 3 parallel Agents → FanIn workflow, execute, verify all 3 agents run concurrently (total time ≈ slowest node, not sum), and FanIn receives aggregated output array.

### Tests for User Story 2 ⚠️

- [x] T028 [US2] Write WorkflowEngine FanOut/FanIn tests covering: FanOut dispatches to multiple parallel nodes via AddFanOutEdge, FanIn aggregates all outputs via AddFanInEdge, partial node failure still completes other parallel nodes, all parallel nodes failed results in workflow Failed in Backend/CoreSRE.Infrastructure.Tests/Workflows/WorkflowEngineTests.cs

### Implementation for User Story 2

- [x] T029 [US2] Extend WorkflowEngine to detect FanOut/FanIn node types from DAG, use AddFanOutEdge for fan-out dispatch and AddFanInEdge for aggregation, bind FanOut → ChatForwardingExecutor, FanIn → AggregateTurnMessagesExecutor per research.md R5 in Backend/CoreSRE.Infrastructure/Services/WorkflowEngine.cs

**Checkpoint**: Parallel workflow execution now works — FanOut nodes dispatch concurrently, FanIn nodes aggregate results.

---

## Phase 5: User Story 3 — 执行条件分支工作流 (Priority: P1)

**Goal**: When a DAG contains Condition nodes with Conditional edges, the engine evaluates JSON Path expressions against the upstream node's output and routes to matching downstream nodes. Unmatched branches are marked Skipped.

**Independent Test**: Create a Condition node with two Conditional edges (`$.severity == "high"` and `$.severity == "low"`), trigger with different inputs, verify correct branch is taken and the other is Skipped.

### Tests for User Story 3 ⚠️

- [x] T030 [P] [US3] Write ConditionEvaluator tests covering: simple equality match `$.field == "value"`, numeric match, no match returns false, malformed expression throws/returns false via TryEvaluate, nested JSON Path `$.data.status == "ok"` in Backend/CoreSRE.Infrastructure.Tests/Workflows/ConditionEvaluatorTests.cs
- [x] T031 [P] [US3] Write WorkflowEngine conditional branching tests covering: matching branch executes and other is Skipped, no branch matches results in Failed with "无匹配的条件分支" (FR-009), condition expression parse error marks node as Failed (FR-017) in Backend/CoreSRE.Infrastructure.Tests/Workflows/WorkflowEngineTests.cs

### Implementation for User Story 3

- [x] T032 [US3] Implement ConditionEvaluator using JsonPath.Net: split expression on ` == `, parse JSON Path via JsonPath.Parse, evaluate against JsonNode, compare result with expected value per research.md R4 in Backend/CoreSRE.Infrastructure/Services/ConditionEvaluator.cs
- [x] T033 [US3] Extend WorkflowEngine to detect Condition nodes, use AddSwitch + SwitchBuilder.AddCase with IConditionEvaluator for each Conditional edge, mark unmatched branches as Skipped, fail workflow if no branch matches (FR-009) in Backend/CoreSRE.Infrastructure/Services/WorkflowEngine.cs
- [x] T034 [US3] Register IConditionEvaluator → ConditionEvaluator (Scoped) in Backend/CoreSRE.Infrastructure/DependencyInjection.cs

**Checkpoint**: Conditional branching now works — the engine evaluates JSON Path conditions, routes to matching branches, and skips unmatched ones.

---

## Phase 6: User Story 4 — 查询工作流执行记录列表与详情 (Priority: P2)

**Goal**: Engineers can query execution history for a workflow (list with optional status filter) and view detailed execution records including per-node execution information.

**Independent Test**: Execute a workflow multiple times, then query the list endpoint to verify all records returned, and query the detail endpoint to verify full node execution information.

### Tests for User Story 4 ⚠️

- [x] T035 [P] [US4] Write GetWorkflowExecutionsQueryHandler tests covering: returns executions for given workflow ID, filters by status, returns empty list for no executions, returns 404 for non-existent workflow in Backend/CoreSRE.Application.Tests/Workflows/Queries/GetWorkflowExecutions/GetWorkflowExecutionsQueryHandlerTests.cs
- [x] T036 [P] [US4] Write GetWorkflowExecutionByIdQueryHandler tests covering: returns full execution with node details, returns 404 for non-existent execution, returns 404 for non-existent workflow in Backend/CoreSRE.Application.Tests/Workflows/Queries/GetWorkflowExecutionById/GetWorkflowExecutionByIdQueryHandlerTests.cs

### Implementation for User Story 4

- [x] T037 [P] [US4] Create GetWorkflowExecutionsQuery record (WorkflowDefinitionId, Status as string?) in Backend/CoreSRE.Application/Workflows/Queries/GetWorkflowExecutions/GetWorkflowExecutionsQuery.cs
- [x] T038 [P] [US4] Create GetWorkflowExecutionByIdQuery record (WorkflowDefinitionId, ExecutionId) in Backend/CoreSRE.Application/Workflows/Queries/GetWorkflowExecutionById/GetWorkflowExecutionByIdQuery.cs
- [x] T039 [P] [US4] Implement GetWorkflowExecutionsQueryHandler: verify workflow exists (404), query IWorkflowExecutionRepository.GetByWorkflowIdAsync, optional status filter via GetByStatusAsync, map to WorkflowExecutionSummaryDto list in Backend/CoreSRE.Application/Workflows/Queries/GetWorkflowExecutions/GetWorkflowExecutionsQueryHandler.cs
- [x] T040 [P] [US4] Implement GetWorkflowExecutionByIdQueryHandler: verify workflow exists (404), get execution by ID (404), map to WorkflowExecutionDto with NodeExecutionDto list in Backend/CoreSRE.Application/Workflows/Queries/GetWorkflowExecutionById/GetWorkflowExecutionByIdQueryHandler.cs
- [x] T041 [US4] Add `GET /{id}/executions` (list with optional ?status filter) and `GET /{id}/executions/{execId}` (detail) endpoints to Backend/CoreSRE/Endpoints/WorkflowEndpoints.cs per api-contract.md

**Checkpoint**: All query endpoints functional — engineers can view execution history and detailed per-node execution state.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Migrations, validation, and cleanup that span multiple user stories.

- [x] T042 Generate EF Core migration for workflow_executions table via `dotnet ef migrations add AddWorkflowExecution` in Backend/CoreSRE.Infrastructure/
- [x] T043 Run quickstart.md validation scenarios (sequential, parallel, conditional, error cases) against running application
- [x] T044 Code review: verify all FR requirements (FR-001 through FR-023) are covered, ensure no existing tests modified (Constitution IV), verify build compiles with zero warnings

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1 (Setup) ──→ Phase 2 (Foundational) ──→ Phase 3 (US1: Sequential) ──→ Phase 4 (US2: Parallel) ──→ Phase 5 (US3: Conditional)
                                             ↘ Phase 6 (US4: Query) ─────────────────────────────────────→ Phase 7 (Polish)
```

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Foundational — creates the core engine and background service
- **US2 (Phase 4)**: Depends on US1 — extends WorkflowEngine.cs with FanOut/FanIn
- **US3 (Phase 5)**: Depends on US1 — extends WorkflowEngine.cs with conditional branching; ordered after US2 because both modify same file
- **US4 (Phase 6)**: Depends on Foundational ONLY — independent of US1-US3 (only needs repo + DTOs)
- **Polish (Phase 7)**: Depends on all desired user stories being complete

### User Story Dependencies

- **US1 (P1)**: Can start after Phase 2 — no dependencies on other stories
- **US2 (P1)**: Depends on US1 (WorkflowEngine.cs must exist) — extends parallel support
- **US3 (P1)**: Depends on US1 (WorkflowEngine.cs must exist) — sequential with US2 (same file)
- **US4 (P2)**: Can start after Phase 2 — fully independent of US1/US2/US3

### Within Each User Story

- Tests MUST be written and FAIL before implementation (TDD Red-Green)
- Interfaces/records before implementations
- Handlers before endpoints
- Core logic before DI registration
- Story complete → checkpoint validation before moving to next

### Parallel Opportunities

- Phase 1: T002, T003 can run in parallel
- Phase 2: T004-T007 (all interfaces/VOs) can run in parallel; T010-T012 (DTOs) can run in parallel; T014-T015 can run in parallel
- Phase 3: T018-T020 (all tests) can run in parallel; T021 parallel with tests
- Phase 5: T030, T031 (tests) can run in parallel
- Phase 6: T035-T036 (tests) can run in parallel; T037-T040 (records + handlers) can run in parallel
- **Cross-story**: US4 can run in parallel with US1-US3 (different files, independent functionality)

---

## Parallel Example: User Story 1

```bash
# Launch all US1 tests together (TDD Red phase):
Task T018: "ExecuteWorkflowCommandValidator tests"
Task T019: "ExecuteWorkflowCommandHandler tests"
Task T020: "WorkflowEngine sequential tests"
Task T021: "ExecuteWorkflowCommand record"  # No deps, different file

# Then implement sequentially (TDD Green phase):
Task T022: "Validator" (makes T018 pass)
Task T023: "Handler"   (makes T019 pass)
Task T024: "Engine"    (makes T020 pass)
Task T025: "BackgroundService"
Task T026: "Endpoint"
Task T027: "DI registration"
```

---

## Parallel Example: US4 alongside US1

```bash
# US4 only needs Phase 2 complete — can start with US1:
Developer A (US1): T018 → T019 → T020 → T021 → T022 → T023 → T024 → T025 → T026 → T027
Developer B (US4): T035 → T036 → T037 → T038 → T039 → T040 → T041
# No file conflicts between US1 and US4
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (3 tasks)
2. Complete Phase 2: Foundational (14 tasks)
3. Complete Phase 3: User Story 1 — Sequential Execution (10 tasks)
4. **STOP and VALIDATE**: POST execute, verify 202 + background execution + node state tracking
5. Deploy/demo if ready — sequential workflows can execute

### Incremental Delivery

1. Setup + Foundational → Foundation ready (17 tasks)
2. Add US1 (Sequential) → Test independently → **MVP!** (10 tasks)
3. Add US2 (Parallel) → Test FanOut/FanIn independently (2 tasks)
4. Add US3 (Conditional) → Test branching independently (5 tasks)
5. Add US4 (Query) → Test list/detail endpoints (7 tasks, can parallel with US1-US3)
6. Polish → Migrations, quickstart validation, final review (3 tasks)
7. Each story adds execution capability without breaking previous stories

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: US1 → US2 → US3 (sequential — same WorkflowEngine.cs file)
   - Developer B: US4 (independent — query handlers and endpoints only)
3. Stories integrate independently, no merge conflicts

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable (except US2/US3 extend US1's engine)
- TDD: write tests FIRST, verify they FAIL, then implement to make them pass
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Match existing codebase patterns: `Result<T>` for handler returns, `ISender` in endpoints, assembly scanning for MediatR/AutoMapper/FluentValidation
- No global usings in test projects — each file needs explicit `using Xunit;`, `using FluentAssertions;`, `using Moq;`
- Avoid: vague tasks, same file conflicts within parallel batches, cross-story dependencies that break independence
