# Tasks: 工作流定义 CRUD

**Input**: Design documents from `/specs/011-workflow-crud/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅, quickstart.md ✅

**Tests**: Included — constitution mandates TDD (non-negotiable). Tests are written before implementation for each user story.

**Organization**: Tasks grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)
- All paths relative to repository root

## Path Conventions

- **Domain**: `Backend/CoreSRE.Domain/` (Entities, ValueObjects, Enums, Interfaces)
- **Application**: `Backend/CoreSRE.Application/Workflows/` (Commands, Queries, DTOs)
- **Infrastructure**: `Backend/CoreSRE.Infrastructure/` (Persistence, Configurations, Repositories)
- **API**: `Backend/CoreSRE/` (Endpoints, Program.cs)
- **Tests (App)**: `Backend/CoreSRE.Application.Tests/Workflows/`
- **Tests (Domain/Infra)**: `Backend/CoreSRE.Infrastructure.Tests/Workflows/`

---

## Phase 1: Setup

**Purpose**: Verify clean starting point on feature branch

- [ ] T001 Verify project compiles and existing tests pass on `011-workflow-crud` branch

---

## Phase 2: Foundational (Domain Model & Infrastructure)

**Purpose**: Core domain model, value objects, repository, EF Core configuration, and migration. ALL user stories depend on this phase.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Domain Types (Interface-Before-Implementation)

- [ ] T002 [P] Create WorkflowNodeType, WorkflowEdgeType, WorkflowStatus enums in Backend/CoreSRE.Domain/Enums/ (3 files)
- [ ] T003 [P] Create WorkflowNodeVO value object in Backend/CoreSRE.Domain/ValueObjects/WorkflowNodeVO.cs
- [ ] T004 [P] Create WorkflowEdgeVO value object in Backend/CoreSRE.Domain/ValueObjects/WorkflowEdgeVO.cs
- [ ] T005 Create WorkflowGraphVO with DAG validation via Kahn's algorithm in Backend/CoreSRE.Domain/ValueObjects/WorkflowGraphVO.cs
- [ ] T006 Create WorkflowDefinition aggregate root with Create/Update factory methods and status guards in Backend/CoreSRE.Domain/Entities/WorkflowDefinition.cs
- [ ] T007 Create IWorkflowDefinitionRepository interface in Backend/CoreSRE.Domain/Interfaces/IWorkflowDefinitionRepository.cs

### Domain Tests (TDD — verify entity invariants and DAG validation)

- [ ] T008 [P] Write WorkflowDefinition entity unit tests (Create factory, Update guard, Publish transition, invalid inputs) in Backend/CoreSRE.Infrastructure.Tests/Workflows/WorkflowDefinitionTests.cs
- [ ] T009 [P] Write DAG validation unit tests (cycle detection, orphan nodes, self-loop, duplicate node ID, invalid edge refs, duplicate edges, empty graph, max node warning) in Backend/CoreSRE.Infrastructure.Tests/Workflows/DagValidationTests.cs

### Application Shared (DTOs & Mapping)

- [ ] T010 [P] Create Workflow DTOs (WorkflowDefinitionDto, WorkflowSummaryDto, WorkflowGraphDto, WorkflowNodeDto, WorkflowEdgeDto) in Backend/CoreSRE.Application/Workflows/DTOs/
- [ ] T011 [P] Create WorkflowMappingProfile (AutoMapper) in Backend/CoreSRE.Application/Workflows/DTOs/WorkflowMappingProfile.cs

### Infrastructure (Repository & EF Core)

- [ ] T012 Create WorkflowDefinitionConfiguration (OwnsOne+ToJson, OwnsMany for nodes/edges, enum string conversion) in Backend/CoreSRE.Infrastructure/Persistence/Configurations/WorkflowDefinitionConfiguration.cs
- [ ] T013 Add DbSet\<WorkflowDefinition\> to AppDbContext in Backend/CoreSRE.Infrastructure/Persistence/AppDbContext.cs
- [ ] T014 Create WorkflowDefinitionRepository (GetByNameAsync, GetByStatusAsync, ExistsWithNameAsync, IsReferencedByAgentAsync) in Backend/CoreSRE.Infrastructure/Persistence/WorkflowDefinitionRepository.cs
- [ ] T015 Register IWorkflowDefinitionRepository → WorkflowDefinitionRepository in Backend/CoreSRE.Infrastructure/DependencyInjection.cs
- [ ] T016 Generate EF Core migration for workflow_definitions table

### Verify Foundation

- [ ] T017 Run domain entity and DAG validation tests — all must pass

**Checkpoint**: Foundation ready — user story implementation can now begin

---

## Phase 3: User Story 1 — 创建工作流定义 (Priority: P1) 🎯 MVP

**Goal**: Admin can create a workflow definition with DAG graph validation via POST /api/workflows

**Independent Test**: Submit a DAG with agent/tool nodes and edges → get 201 with complete definition (Draft status). Submit invalid DAG (cycle, orphan) → get 400 with clear error.

### Tests for User Story 1 ⚠️

> **Write tests FIRST. They will fail until implementation is complete.**

- [ ] T018 [P] [US1] Write CreateWorkflowCommandHandler unit tests (success creates Draft, name conflict returns 409, invalid Agent/Tool reference returns 400, DAG cycle returns 400, orphan node returns 400) in Backend/CoreSRE.Application.Tests/Workflows/Commands/CreateWorkflowCommandHandlerTests.cs
- [ ] T019 [P] [US1] Write CreateWorkflowCommandValidator unit tests (name empty, name too long >200, graph null, nodes empty, nodeId empty, displayName empty, invalid nodeType, edgeId empty, sourceNodeId empty, conditional edge missing condition) in Backend/CoreSRE.Application.Tests/Workflows/Commands/CreateWorkflowCommandValidatorTests.cs

### Implementation for User Story 1

- [ ] T020 [US1] Create CreateWorkflowCommand record in Backend/CoreSRE.Application/Workflows/Commands/CreateWorkflow/CreateWorkflowCommand.cs
- [ ] T021 [US1] Create CreateWorkflowCommandValidator (FluentValidation rules per contracts) in Backend/CoreSRE.Application/Workflows/Commands/CreateWorkflow/CreateWorkflowCommandValidator.cs
- [ ] T022 [US1] Create CreateWorkflowCommandHandler (uniqueness check → DAG validation → reference validation → Create entity → save) in Backend/CoreSRE.Application/Workflows/Commands/CreateWorkflow/CreateWorkflowCommandHandler.cs
- [ ] T023 [US1] Create WorkflowEndpoints with POST /api/workflows in Backend/CoreSRE/Endpoints/WorkflowEndpoints.cs
- [ ] T024 [US1] Register app.MapWorkflowEndpoints() in Backend/CoreSRE/Program.cs

**Checkpoint**: POST /api/workflows works. DAG validation rejects cycles, orphan nodes, invalid references. Tests pass.

---

## Phase 4: User Story 2 — 查询工作流定义列表与详情 (Priority: P1)

**Goal**: Admin can list all workflows (with status filter) and view full detail of a specific workflow

**Independent Test**: Create several workflows → GET /api/workflows returns summaries with nodeCount → GET /api/workflows/{id} returns full graph detail. Non-existent ID → 404.

### Tests for User Story 2 ⚠️

> **Write tests FIRST.**

- [ ] T025 [P] [US2] Write GetWorkflowsQueryHandler unit tests (returns all summaries with nodeCount, filters by status, empty result) in Backend/CoreSRE.Application.Tests/Workflows/Queries/GetWorkflowsQueryHandlerTests.cs
- [ ] T026 [P] [US2] Write GetWorkflowByIdQueryHandler unit tests (found returns full detail, not found returns 404) in Backend/CoreSRE.Application.Tests/Workflows/Queries/GetWorkflowByIdQueryHandlerTests.cs

### Implementation for User Story 2

- [ ] T027 [P] [US2] Create GetWorkflowsQuery and GetWorkflowsQueryHandler in Backend/CoreSRE.Application/Workflows/Queries/GetWorkflows/
- [ ] T028 [P] [US2] Create GetWorkflowByIdQuery and GetWorkflowByIdQueryHandler in Backend/CoreSRE.Application/Workflows/Queries/GetWorkflowById/
- [ ] T029 [US2] Add GET /api/workflows and GET /api/workflows/{id} endpoints to WorkflowEndpoints in Backend/CoreSRE/Endpoints/WorkflowEndpoints.cs

**Checkpoint**: List and detail queries work. Status filtering works. 404 for missing IDs. Tests pass.

---

## Phase 5: User Story 3 — 更新工作流定义 (Priority: P1)

**Goal**: Admin can update name, description, and DAG graph of a Draft workflow via PUT /api/workflows/{id}

**Independent Test**: Create a Draft workflow → PUT with updated name and new node → 200 with updated definition. PUT on Published workflow → 400. PUT with name conflict → 409. PUT with DAG cycle → 400.

### Tests for User Story 3 ⚠️

> **Write tests FIRST.**

- [ ] T030 [P] [US3] Write UpdateWorkflowCommandHandler unit tests (success updates Draft, not found returns 404, published guard returns 400, name conflict returns 409, DAG cycle returns 400, invalid reference returns 400) in Backend/CoreSRE.Application.Tests/Workflows/Commands/UpdateWorkflowCommandHandlerTests.cs
- [ ] T031 [P] [US3] Write UpdateWorkflowCommandValidator unit tests (id empty, name empty, name too long, graph null, nodes empty, same rules as create) in Backend/CoreSRE.Application.Tests/Workflows/Commands/UpdateWorkflowCommandValidatorTests.cs

### Implementation for User Story 3

- [ ] T032 [US3] Create UpdateWorkflowCommand record in Backend/CoreSRE.Application/Workflows/Commands/UpdateWorkflow/UpdateWorkflowCommand.cs
- [ ] T033 [US3] Create UpdateWorkflowCommandValidator in Backend/CoreSRE.Application/Workflows/Commands/UpdateWorkflow/UpdateWorkflowCommandValidator.cs
- [ ] T034 [US3] Create UpdateWorkflowCommandHandler (fetch → status guard → uniqueness check → DAG validation → reference validation → Update entity → save) in Backend/CoreSRE.Application/Workflows/Commands/UpdateWorkflow/UpdateWorkflowCommandHandler.cs
- [ ] T035 [US3] Add PUT /api/workflows/{id} endpoint to WorkflowEndpoints in Backend/CoreSRE/Endpoints/WorkflowEndpoints.cs

**Checkpoint**: Update works for Draft workflows. Published guard, name uniqueness, and DAG validation enforced. Tests pass.

---

## Phase 6: User Story 4 — 删除工作流定义 (Priority: P2)

**Goal**: Admin can delete a Draft workflow that is not referenced by any AgentRegistration via DELETE /api/workflows/{id}

**Independent Test**: Create a Draft workflow → DELETE → 204 → GET returns 404. Attempt delete on Published workflow → 400. Attempt delete on workflow referenced by AgentRegistration → 400.

### Tests for User Story 4 ⚠️

> **Write tests FIRST.**

- [ ] T036 [P] [US4] Write DeleteWorkflowCommandHandler unit tests (success deletes Draft, not found returns 404, published guard returns 400, agent reference guard returns 400) in Backend/CoreSRE.Application.Tests/Workflows/Commands/DeleteWorkflowCommandHandlerTests.cs
- [ ] T037 [P] [US4] Write DeleteWorkflowCommandValidator unit tests (id must not be Guid.Empty) in Backend/CoreSRE.Application.Tests/Workflows/Commands/DeleteWorkflowCommandValidatorTests.cs

### Implementation for User Story 4

- [ ] T038 [US4] Create DeleteWorkflowCommand record in Backend/CoreSRE.Application/Workflows/Commands/DeleteWorkflow/DeleteWorkflowCommand.cs
- [ ] T039 [US4] Create DeleteWorkflowCommandValidator in Backend/CoreSRE.Application/Workflows/Commands/DeleteWorkflow/DeleteWorkflowCommandValidator.cs
- [ ] T040 [US4] Create DeleteWorkflowCommandHandler (fetch → status guard → agent reference check → delete → save) in Backend/CoreSRE.Application/Workflows/Commands/DeleteWorkflow/DeleteWorkflowCommandHandler.cs
- [ ] T041 [US4] Add DELETE /api/workflows/{id} endpoint to WorkflowEndpoints in Backend/CoreSRE/Endpoints/WorkflowEndpoints.cs

**Checkpoint**: Delete works for unreferenced Draft workflows. Published guard and agent reference guard enforced. Tests pass.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final validation and cleanup across all user stories

- [ ] T042 [P] Run full test suite — all domain, handler, and validator tests must pass
- [ ] T043 Run quickstart.md verification steps end-to-end (all 6 HTTP scenarios + 13-item checklist)
- [ ] T044 Code review — validate all patterns match existing codebase conventions (naming, DI, error codes, Result\<T\> usage)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 — **BLOCKS all user stories**
- **US1 Create (Phase 3)**: Depends on Phase 2 — creates WorkflowEndpoints.cs and Program.cs registration
- **US2 List/Detail (Phase 4)**: Depends on Phase 2 + T023 (WorkflowEndpoints.cs must exist)
- **US3 Update (Phase 5)**: Depends on Phase 2 + T023 (WorkflowEndpoints.cs must exist)
- **US4 Delete (Phase 6)**: Depends on Phase 2 + T023 (WorkflowEndpoints.cs must exist)
- **Polish (Phase 7)**: Depends on all user stories complete

### User Story Dependencies

- **US1 (P1)**: Depends on Foundational only — **MVP story, implement first**
- **US2 (P1)**: Depends on Foundational + US1 (needs WorkflowEndpoints.cs file). Can be implemented immediately after US1.
- **US3 (P1)**: Depends on Foundational + US1 (needs WorkflowEndpoints.cs file). Can be implemented in parallel with US2.
- **US4 (P2)**: Depends on Foundational + US1 (needs WorkflowEndpoints.cs file). Can be implemented in parallel with US2/US3.

### Within Each User Story (TDD Order)

1. **Tests FIRST** — write handler + validator tests (they will fail/not compile initially)
2. **Command record** — creates the type (tests can now reference it)
3. **Validator** — implements validation rules (validator tests start passing)
4. **Handler** — implements business logic (handler tests start passing)
5. **Endpoint** — wires HTTP to MediatR (integration-ready)

> **C# TDD Note**: In compiled languages, the command record type (step 2) must exist before tests compile. The practical workflow is: create minimal command record → write tests → implement validator + handler → run tests. This satisfies TDD intent while respecting compiler constraints.

### Parallel Opportunities

- **Phase 2**: T002/T003/T004 (enums + VOs) can all run in parallel. T008/T009 (tests) can run in parallel. T010/T011 (DTOs + mapping) can run in parallel.
- **Phase 3-6**: Within each story, test files (handler + validator tests) can run in parallel.
- **Phase 4-6**: US2, US3, US4 can be implemented in parallel after US1 creates WorkflowEndpoints.cs (different methods, same file — coordinate endpoint additions).

---

## Parallel Example: Phase 2 Foundation

```
# Step 1 — All enum and VO files in parallel:
T002: Create enums (3 files)
T003: Create WorkflowNodeVO
T004: Create WorkflowEdgeVO

# Step 2 — After enums + VOs:
T005: Create WorkflowGraphVO (depends on T003, T004)

# Step 3 — After WorkflowGraphVO:
T006: Create WorkflowDefinition entity (depends on T005)
T007: Create IWorkflowDefinitionRepository (depends on T006)

# Step 4 — Tests + DTOs in parallel:
T008: Entity tests (depends on T006)
T009: DAG validation tests (depends on T005)
T010: DTOs (depends on T002-T004 for types)
T011: Mapping profile (depends on T006, T010)

# Step 5 — Infrastructure (sequential):
T012: EF Configuration (depends on T006)
T013: AppDbContext (depends on T006)
T014: Repository (depends on T007, T013)
T015: DI registration (depends on T014)
T016: Migration (depends on T012, T013)

# Step 6 — Verify:
T017: Run tests (depends on all above)
```

## Parallel Example: User Story 1

```
# Step 1 — Write tests in parallel:
T018: Handler tests
T019: Validator tests

# Step 2 — Create command type:
T020: CreateWorkflowCommand record

# Step 3 — Implementation (validator + handler can be parallel):
T021: Validator
T022: Handler

# Step 4 — API:
T023: WorkflowEndpoints POST
T024: Register in Program.cs
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup ✓
2. Complete Phase 2: Foundational (**CRITICAL** — blocks all stories)
3. Complete Phase 3: User Story 1 — Create Workflow
4. **STOP and VALIDATE**: POST /api/workflows works, DAG validation works, tests pass
5. This is a deployable MVP — admins can create validated workflow definitions

### Incremental Delivery

1. Setup + Foundational → Domain model ready, database migrated
2. **US1 Create** → Test independently → MVP (create + validate workflows)
3. **US2 List/Detail** → Test independently → Workflows browsable
4. **US3 Update** → Test independently → Workflows editable (Draft only)
5. **US4 Delete** → Test independently → Full CRUD complete
6. **Polish** → All tests green, quickstart validated, code reviewed

### Single Developer Strategy (Recommended)

Execute phases sequentially in priority order: Phase 1 → 2 → 3 → 4 → 5 → 6 → 7. Each phase builds on the previous. After each user story, run its tests to validate before proceeding.

---

## Task Summary

| Phase | Description | Task Range | Count |
|-------|-------------|------------|-------|
| 1 | Setup | T001 | 1 |
| 2 | Foundational | T002–T017 | 16 |
| 3 | US1: Create (P1) 🎯 MVP | T018–T024 | 7 |
| 4 | US2: List/Detail (P1) | T025–T029 | 5 |
| 5 | US3: Update (P1) | T030–T035 | 6 |
| 6 | US4: Delete (P2) | T036–T041 | 6 |
| 7 | Polish | T042–T044 | 3 |
| **Total** | | **T001–T044** | **44** |

---

## Notes

- [P] tasks = different files, no dependencies — safe to parallelize
- [US*] label maps task to specific user story for traceability
- Constitution: TDD (non-negotiable) — tests before implementation per story
- Constitution: Interface-Before-Implementation — IWorkflowDefinitionRepository before WorkflowDefinitionRepository
- Constitution: Test Immutability — existing tests in other features must not be modified
- DAG validation: Kahn's algorithm (BFS topological sort), pure domain logic in WorkflowGraphVO
- JSONB storage: OwnsOne + ToJson("graph") with OwnsMany for nested collections
- Domain tests in Infrastructure.Tests (no separate Domain.Tests project exists)
- All repositories in Infrastructure/Persistence/ (not a separate Repositories/ folder)
- AutoMapper profiles auto-discovered via assembly scanning in Application DI
- Commit after each phase or logical task group
