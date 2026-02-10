# Tasks: Agent 注册与 CRUD 管理（多类型）

**Input**: Design documents from `/specs/002-agent-registry-crud/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/agents-api.yaml ✅, quickstart.md ✅

**Tests**: Not explicitly requested in the feature specification. Test tasks are omitted.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Backend**: `Backend/CoreSRE.Domain/`, `Backend/CoreSRE.Application/`, `Backend/CoreSRE.Infrastructure/`, `Backend/CoreSRE/`
- Based on existing DDD 4-layer structure in plan.md

---

## Phase 1: Setup

**Purpose**: Create folder structure and any new directories needed for this feature

- [X] T001 Create directory structure for Agent feature: `Backend/CoreSRE.Domain/Enums/`, `Backend/CoreSRE.Domain/ValueObjects/`, `Backend/CoreSRE.Application/Agents/DTOs/`, `Backend/CoreSRE.Application/Agents/Commands/RegisterAgent/`, `Backend/CoreSRE.Application/Agents/Commands/UpdateAgent/`, `Backend/CoreSRE.Application/Agents/Commands/DeleteAgent/`, `Backend/CoreSRE.Application/Agents/Queries/GetAgents/`, `Backend/CoreSRE.Application/Agents/Queries/GetAgentById/`, `Backend/CoreSRE/Endpoints/`, `Backend/CoreSRE/Middleware/`, `Backend/CoreSRE.Infrastructure/Persistence/Configurations/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain model, shared infrastructure, and cross-cutting concerns that ALL user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

### Domain Layer — Enums

- [X] T002 [P] Create `AgentType` enum (A2A, ChatClient, Workflow) in `Backend/CoreSRE.Domain/Enums/AgentType.cs`
- [X] T003 [P] Create `AgentStatus` enum (Registered, Active, Inactive, Error) in `Backend/CoreSRE.Domain/Enums/AgentStatus.cs`

### Domain Layer — Value Objects

- [X] T004 [P] Create `AgentSkillVO` record (Name, Description) in `Backend/CoreSRE.Domain/ValueObjects/AgentSkillVO.cs`
- [X] T005 [P] Create `AgentInterfaceVO` record (Protocol, Path) in `Backend/CoreSRE.Domain/ValueObjects/AgentInterfaceVO.cs`
- [X] T006 [P] Create `SecuritySchemeVO` record (Type, Parameters) in `Backend/CoreSRE.Domain/ValueObjects/SecuritySchemeVO.cs`
- [X] T007 [P] Create `AgentCardVO` record (Skills, Interfaces, SecuritySchemes lists) in `Backend/CoreSRE.Domain/ValueObjects/AgentCardVO.cs`
- [X] T008 [P] Create `LlmConfigVO` record (ModelId, Instructions, ToolRefs) in `Backend/CoreSRE.Domain/ValueObjects/LlmConfigVO.cs`
- [X] T009 [P] Create `HealthCheckVO` record (LastCheckTime, IsHealthy, FailureCount) with default values in `Backend/CoreSRE.Domain/ValueObjects/HealthCheckVO.cs`

### Domain Layer — Entity & Repository Interface

- [X] T010 Create `AgentRegistration` aggregate root entity inheriting `BaseEntity` with factory methods (`CreateA2A`, `CreateChatClient`, `CreateWorkflow`), `Update` method, and domain invariant guards in `Backend/CoreSRE.Domain/Entities/AgentRegistration.cs`
- [X] T011 Create `IAgentRegistrationRepository` interface extending `IRepository<AgentRegistration>` with `GetByTypeAsync(AgentType? type)` method in `Backend/CoreSRE.Domain/Interfaces/IAgentRegistrationRepository.cs`

### Application Layer — Result<T> Extension

- [X] T012 Extend `Result<T>` with `ErrorCode` (int?) property, add `NotFound()` and `Conflict()` factory methods in `Backend/CoreSRE.Application/Common/Models/Result.cs`

### Application Layer — DTOs & Mapping

- [X] T013 [P] Create `AgentRegistrationDto` (full detail DTO with all type-specific fields) in `Backend/CoreSRE.Application/Agents/DTOs/AgentRegistrationDto.cs`
- [X] T014 [P] Create `AgentSummaryDto` (list item DTO: Id, Name, AgentType, Status, CreatedAt) in `Backend/CoreSRE.Application/Agents/DTOs/AgentSummaryDto.cs`
- [X] T015 [P] Create `AgentCardDto` and `LlmConfigDto` (nested DTOs for type-specific data) in `Backend/CoreSRE.Application/Agents/DTOs/AgentCardDto.cs` and `Backend/CoreSRE.Application/Agents/DTOs/LlmConfigDto.cs`
- [X] T016 Create `AgentMappingProfile` AutoMapper profile mapping AgentRegistration ↔ AgentRegistrationDto/AgentSummaryDto and VOs ↔ nested DTOs in `Backend/CoreSRE.Application/Agents/DTOs/AgentMappingProfile.cs`

### Infrastructure Layer — Persistence

- [X] T017 Create `AgentRegistrationConfiguration` EF Core entity configuration with `ToJson()` for AgentCardVO/LlmConfigVO/HealthCheckVO JSONB columns, unique index on Name, enum-to-string conversion for AgentType/AgentStatus in `Backend/CoreSRE.Infrastructure/Persistence/Configurations/AgentRegistrationConfiguration.cs`
- [X] T018 Create `AgentRegistrationRepository` implementing `IAgentRegistrationRepository` with `GetByTypeAsync` filter logic in `Backend/CoreSRE.Infrastructure/Persistence/AgentRegistrationRepository.cs`
- [X] T019 Add `DbSet<AgentRegistration>` to `AppDbContext` in `Backend/CoreSRE.Infrastructure/Persistence/AppDbContext.cs`
- [X] T020 Register `IAgentRegistrationRepository` → `AgentRegistrationRepository` in DI container in `Backend/CoreSRE.Infrastructure/DependencyInjection.cs`

### API Layer — Middleware

- [X] T021 Create `ExceptionHandlingMiddleware` that catches `ValidationException` → 400 (structured Result errors) and `DbUpdateException` with PostgresException SqlState 23505 → 409 in `Backend/CoreSRE/Middleware/ExceptionHandlingMiddleware.cs`
- [X] T022 Register `ExceptionHandlingMiddleware` in the HTTP pipeline in `Backend/CoreSRE/Program.cs`

### API Layer — Endpoint Scaffold

- [X] T023 Create `AgentEndpoints` static class with `MapAgentEndpoints()` extension method using `MapGroup("/api/agents")`, `.WithTags("Agents")`, `.WithOpenApi()` in `Backend/CoreSRE/Endpoints/AgentEndpoints.cs` (register in Program.cs)

**Checkpoint**: Foundation ready — domain model, persistence, middleware, and endpoint scaffold in place. User story implementation can now begin.

---

## Phase 3: User Story 1 — 注册 A2A 类型 Agent (Priority: P1) 🎯 MVP

**Goal**: Register an A2A Agent with full AgentCard (skills, interfaces, securitySchemes) via `POST /api/agents`

**Independent Test**: Send `POST /api/agents` with `agentType: "A2A"`, verify 201 + ID returned. Then `GET /api/agents/{id}` confirms persistence. Also verify 400 for missing endpoint/agentCard, and 409 for duplicate name.

### Implementation for User Story 1

- [X] T024 [P] [US1] Create `RegisterAgentCommand` record (Name, Description, AgentType, Endpoint, AgentCard, LlmConfig, WorkflowRef) in `Backend/CoreSRE.Application/Agents/Commands/RegisterAgent/RegisterAgentCommand.cs`
- [X] T025 [P] [US1] Create `RegisterAgentCommandValidator` with FluentValidation rules: name required/max 200, agentType valid enum, type-conditional rules (A2A → endpoint + agentCard required, agentCard.skills etc.) in `Backend/CoreSRE.Application/Agents/Commands/RegisterAgent/RegisterAgentCommandValidator.cs`
- [X] T026 [US1] Create `RegisterAgentCommandHandler` that maps command → domain factory method, saves via repository, catches DbUpdateException for unique name conflict → Result.Conflict(), returns Result<AgentRegistrationDto> in `Backend/CoreSRE.Application/Agents/Commands/RegisterAgent/RegisterAgentCommandHandler.cs`
- [X] T027 [US1] Add `POST /api/agents` endpoint handler in `AgentEndpoints` that sends `RegisterAgentCommand` via MediatR, maps Result to 201/400/409 HTTP responses with Location header in `Backend/CoreSRE/Endpoints/AgentEndpoints.cs`

**Checkpoint**: A2A Agent registration works end-to-end — POST creates, returns 201 with ID. Validation errors return 400. Duplicate names return 409.

---

## Phase 4: User Story 2 — 查询 Agent 列表与详情 (Priority: P1)

**Goal**: List all agents (with optional `?type=` filter) and get individual agent details by ID

**Independent Test**: After registering agents, `GET /api/agents` returns full list, `GET /api/agents?type=A2A` returns filtered list, `GET /api/agents/{id}` returns full detail. Non-existent ID returns 404.

### Implementation for User Story 2

- [X] T028 [P] [US2] Create `GetAgentsQuery` record (AgentType? Type) and `GetAgentsQueryHandler` that calls `IAgentRegistrationRepository.GetByTypeAsync()`, maps to `List<AgentSummaryDto>` in `Backend/CoreSRE.Application/Agents/Queries/GetAgents/GetAgentsQuery.cs` and `Backend/CoreSRE.Application/Agents/Queries/GetAgents/GetAgentsQueryHandler.cs`
- [X] T029 [P] [US2] Create `GetAgentByIdQuery` record (Guid Id) and `GetAgentByIdQueryHandler` that calls repository.GetByIdAsync(), returns Result<AgentRegistrationDto> or Result.NotFound() in `Backend/CoreSRE.Application/Agents/Queries/GetAgentById/GetAgentByIdQuery.cs` and `Backend/CoreSRE.Application/Agents/Queries/GetAgentById/GetAgentByIdQueryHandler.cs`
- [X] T030 [US2] Add `GET /api/agents` (with optional `?type=` query param) and `GET /api/agents/{id}` endpoint handlers in `AgentEndpoints` that send queries via MediatR, map Result to 200/404 HTTP responses in `Backend/CoreSRE/Endpoints/AgentEndpoints.cs`

**Checkpoint**: Query endpoints functional — list with filter works, detail by ID works, 404 for missing agents.

---

## Phase 5: User Story 3 — 注册 ChatClient 和 Workflow 类型 Agent (Priority: P1)

**Goal**: Extend registration to support ChatClient (with LlmConfig) and Workflow (with workflowRef) types

**Independent Test**: Send `POST /api/agents` with `agentType: "ChatClient"` (modelId required), verify 201. Send with `agentType: "Workflow"` (workflowRef required), verify 201. Verify 400 for missing type-specific fields.

### Implementation for User Story 3

- [X] T031 [US3] Add ChatClient-specific validation rules (llmConfig required, modelId non-empty) and Workflow-specific rules (workflowRef required, not Guid.Empty) to `RegisterAgentCommandValidator` in `Backend/CoreSRE.Application/Agents/Commands/RegisterAgent/RegisterAgentCommandValidator.cs`

**Checkpoint**: All three agent types can be registered with proper type-specific validation. ChatClient requires modelId, Workflow requires workflowRef, A2A requires endpoint+agentCard.

---

## Phase 6: User Story 4 — 更新 Agent 注册信息 (Priority: P1)

**Goal**: Update an existing agent's configuration (name, description, type-specific data) via `PUT /api/agents/{id}`. Agent type is immutable.

**Independent Test**: Register an A2A Agent, then `PUT /api/agents/{id}` to update description and skills. Verify 200 with updated data and new updatedAt. Verify 400 when attempting to change agentType. Verify 404 for non-existent ID.

### Implementation for User Story 4

- [X] T032 [P] [US4] Create `UpdateAgentCommand` record (Guid Id, Name, Description, Endpoint, AgentCard, LlmConfig, WorkflowRef) in `Backend/CoreSRE.Application/Agents/Commands/UpdateAgent/UpdateAgentCommand.cs`
- [X] T033 [P] [US4] Create `UpdateAgentCommandValidator` with same field rules as RegisterAgentCommandValidator (name required/max 200) but without agentType in request (type comes from existing entity) in `Backend/CoreSRE.Application/Agents/Commands/UpdateAgent/UpdateAgentCommandValidator.cs`
- [X] T034 [US4] Create `UpdateAgentCommandHandler` that loads entity by ID (→ NotFound if missing), calls entity.Update() with new values, saves, catches unique name conflict → Conflict(), returns Result<AgentRegistrationDto> in `Backend/CoreSRE.Application/Agents/Commands/UpdateAgent/UpdateAgentCommandHandler.cs`
- [X] T035 [US4] Add `PUT /api/agents/{id}` endpoint handler in `AgentEndpoints` that sends `UpdateAgentCommand` via MediatR, maps Result to 200/400/404/409 HTTP responses in `Backend/CoreSRE/Endpoints/AgentEndpoints.cs`

**Checkpoint**: Update flow works — config changes persist, agentType immutable, updatedAt reflects change time, validation same as registration.

---

## Phase 7: User Story 5 — 注销 Agent (Priority: P1)

**Goal**: Permanently delete an agent via `DELETE /api/agents/{id}`

**Independent Test**: Register an agent, `DELETE /api/agents/{id}` returns 204. Subsequent `GET /api/agents/{id}` returns 404. DELETE on non-existent ID returns 404.

### Implementation for User Story 5

- [X] T036 [P] [US5] Create `DeleteAgentCommand` record (Guid Id) and `DeleteAgentCommandHandler` that loads entity by ID (→ NotFound if missing), calls repository.DeleteAsync(), saves via UnitOfWork, returns Result in `Backend/CoreSRE.Application/Agents/Commands/DeleteAgent/DeleteAgentCommand.cs` and `Backend/CoreSRE.Application/Agents/Commands/DeleteAgent/DeleteAgentCommandHandler.cs`
- [X] T037 [US5] Add `DELETE /api/agents/{id}` endpoint handler in `AgentEndpoints` that sends `DeleteAgentCommand` via MediatR, maps Result to 204/404 HTTP responses in `Backend/CoreSRE/Endpoints/AgentEndpoints.cs`

**Checkpoint**: Full CRUD lifecycle complete — Register → Query → Update → Delete all functional.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Build verification, EF Core migration, and quickstart validation

- [X] T038 [P] Generate EF Core migration for `agent_registrations` table by running `dotnet ef migrations add AddAgentRegistration` from `Backend/CoreSRE.Infrastructure/`
- [X] T039 Verify solution builds cleanly with `dotnet build Backend/CoreSRE/CoreSRE.slnx`
- [ ] T040 Run quickstart.md validation: start Aspire AppHost, execute all curl commands from `specs/002-agent-registry-crud/quickstart.md`, verify expected HTTP status codes

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 — BLOCKS all user stories
- **User Story 1 (Phase 3)**: Depends on Phase 2 — delivers MVP registration
- **User Story 2 (Phase 4)**: Depends on Phase 2 — can run in parallel with US1
- **User Story 3 (Phase 5)**: Depends on US1 (extends its validator) — must follow Phase 3
- **User Story 4 (Phase 6)**: Depends on Phase 2 — can run in parallel with US1/US2
- **User Story 5 (Phase 7)**: Depends on Phase 2 — can run in parallel with US1/US2/US4
- **Polish (Phase 8)**: Depends on ALL user stories being complete

### User Story Dependencies

- **US1 (Register A2A)**: Phase 2 only — MVP, no other story dependencies
- **US2 (Query list/detail)**: Phase 2 only — independent of US1 (reads same entity)
- **US3 (Register ChatClient/Workflow)**: Depends on US1 (extends RegisterAgentCommandValidator)
- **US4 (Update)**: Phase 2 only — independent of US1/US2
- **US5 (Delete)**: Phase 2 only — independent of other stories

### Within Each User Story

- Command/Query definitions before handlers
- Validators in parallel with commands
- Handlers before endpoint wiring
- Endpoint wiring as final step

### Parallel Opportunities

- **Phase 2**: T002-T003 (enums) in parallel; T004-T009 (VOs) in parallel; T013-T015 (DTOs) in parallel; T017-T020 can partially parallelize
- **Phase 3-7**: US1, US2, US4, US5 can start in parallel after Phase 2 (US3 depends on US1)
- **Within stories**: Command + Validator marked [P] can be written in parallel

---

## Parallel Example: User Story 1

```bash
# Launch command + validator in parallel (different files):
Task T024: "Create RegisterAgentCommand in .../RegisterAgent/RegisterAgentCommand.cs"
Task T025: "Create RegisterAgentCommandValidator in .../RegisterAgent/RegisterAgentCommandValidator.cs"

# Then sequentially:
Task T026: "Create RegisterAgentCommandHandler" (depends on T024)
Task T027: "Add POST endpoint handler" (depends on T026)
```

## Parallel Example: Foundational Phase

```bash
# All enums in parallel:
Task T002: "AgentType enum"
Task T003: "AgentStatus enum"

# All VOs in parallel (after enums not needed for VOs):
Task T004-T009: All 6 value objects

# Then entity (depends on enums + VOs):
Task T010: "AgentRegistration entity"

# Then repository interface (depends on entity):
Task T011: "IAgentRegistrationRepository"

# DTOs in parallel (after entity exists for reference):
Task T013-T015: All DTOs in parallel

# Mapping profile (depends on DTOs + entity):
Task T016: "AgentMappingProfile"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001)
2. Complete Phase 2: Foundational (T002–T023)
3. Complete Phase 3: User Story 1 — Register A2A (T024–T027)
4. **STOP and VALIDATE**: POST A2A agent, verify 201 + persistence
5. Ready for demo after just 27 tasks

### Incremental Delivery

1. Phase 1 + 2 → Foundation ready
2. Add US1 (Phase 3) → A2A registration works → **MVP!**
3. Add US2 (Phase 4) → Can query/list/filter agents
4. Add US3 (Phase 5) → All three types registerable
5. Add US4 (Phase 6) → Can update agent configs
6. Add US5 (Phase 7) → Full CRUD lifecycle
7. Phase 8 → Migration, build, quickstart validation
8. Each story adds value without breaking previous stories

### Parallel Team Strategy

With multiple developers after Phase 2 completes:

- Developer A: User Story 1 (Register A2A) → then User Story 3 (other types, extends US1)
- Developer B: User Story 2 (Query) + User Story 5 (Delete)
- Developer C: User Story 4 (Update)

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story for traceability
- Each user story is independently testable after Phase 2 foundation
- No test tasks generated (tests not explicitly requested in spec)
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Total: 40 tasks across 8 phases
