# Tasks: Team Agent 领域模型与 CRUD

**Input**: Design documents from `/specs/018-team-agent-model/`
**Prerequisites**: plan.md ✅, spec.md ✅, data-model.md ✅

**Tests**: Included — Constitution mandates TDD (NON-NEGOTIABLE).

**Organization**: Tasks grouped by phase. Each phase is independently verifiable.

## Format: `[ID] [P?] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- Include exact file paths in descriptions

## Path Conventions

- **Backend**: `Backend/CoreSRE.Domain/`, `Backend/CoreSRE.Application/`, `Backend/CoreSRE.Infrastructure/`
- **Tests**: `Backend/CoreSRE.Application.Tests/`
- **Frontend**: `Frontend/src/`

---

## Phase 1: Domain Model — Enums & Value Objects

**Purpose**: Define the core domain types that all subsequent work depends on.

- [X] T001 [P] Create `TeamMode` enum (Sequential, Concurrent, RoundRobin, Handoffs, Selector, MagneticOne) in `Backend/CoreSRE.Domain/Enums/TeamMode.cs`
- [X] T002 [P] Create `HandoffTargetVO` record (TargetAgentId, Reason) in `Backend/CoreSRE.Domain/ValueObjects/HandoffTargetVO.cs`
- [X] T003 Create `TeamConfigVO` record with all mode-specific fields in `Backend/CoreSRE.Domain/ValueObjects/TeamConfigVO.cs`
- [X] T004 Add `Team` value to `AgentType` enum in `Backend/CoreSRE.Domain/Enums/AgentType.cs`

---

## Phase 2: Domain Model — AgentRegistration Extension

**Purpose**: Extend the aggregate root with Team support.

### Tests ⚠️ (Red Phase)

- [X] T005 Write unit tests for `AgentRegistration.CreateTeam()` — happy path + all 6 TeamMode validation scenarios in `Backend/CoreSRE.Application.Tests/Agents/CreateTeamAgentTests.cs`

### Implementation

- [X] T006 Add `TeamConfig` property + `CreateTeam()` factory method + mode-specific validation to `AgentRegistration` entity in `Backend/CoreSRE.Domain/Entities/AgentRegistration.cs`
- [X] T007 Update `AgentRegistration.Update()` to handle `AgentType.Team` with `TeamConfig` validation in `Backend/CoreSRE.Domain/Entities/AgentRegistration.cs`

**Checkpoint**: `CreateTeamAgentTests` green. Existing agent tests unchanged.

---

## Phase 3: Database Migration

**Purpose**: Add the JSONB column for TeamConfig to PostgreSQL.

- [X] T008 Configure `TeamConfigVO` JSONB mapping in `AppDbContext` / EF Core entity configuration in `Backend/CoreSRE.Infrastructure/Persistence/`
- [X] T009 Generate EF Core migration `AddTeamConfig` — adds nullable `TeamConfig` JSONB column to `AgentRegistrations` table

**Checkpoint**: `dotnet ef database update` succeeds. Existing data unaffected.

---

## Phase 4: Application Layer — CQRS + DTOs

**Purpose**: Update the commands, queries, DTOs, validators, and mapping for Team Agent CRUD.

### Tests ⚠️ (Red Phase)

- [X] T010 [P] Write validator tests for `RegisterAgentCommand` with `AgentType.Team` — all validation rules in `Backend/CoreSRE.Application.Tests/Agents/RegisterTeamAgentValidatorTests.cs`

### Implementation

- [X] T011 [P] Create `TeamConfigDto` and `HandoffTargetDto` records in `Backend/CoreSRE.Application/Agents/DTOs/TeamConfigDto.cs`
- [X] T012 [P] Add `TeamConfig` property to `AgentRegistrationDto` in `Backend/CoreSRE.Application/Agents/DTOs/AgentRegistrationDto.cs`
- [X] T013 Update `AgentMappingProfile` with `TeamConfigVO ↔ TeamConfigDto` mapping in `Backend/CoreSRE.Application/Agents/DTOs/AgentMappingProfile.cs`
- [X] T014 Add `TeamConfig` field to `RegisterAgentCommand` and update handler to call `CreateTeam()` for `AgentType.Team` in `Backend/CoreSRE.Application/Agents/Commands/RegisterAgent/`
- [X] T015 Add Team-specific validation rules to `RegisterAgentCommandValidator` — validate ParticipantIds non-empty, mode-specific fields, no Team-in-Team nesting in `Backend/CoreSRE.Application/Agents/Commands/RegisterAgent/RegisterAgentCommandValidator.cs`
- [X] T016 Add `TeamConfig` field to `UpdateAgentCommand` and update handler for `AgentType.Team` in `Backend/CoreSRE.Application/Agents/Commands/UpdateAgent/`
- [X] T017 Add Team-specific validation rules to `UpdateAgentCommandValidator` in `Backend/CoreSRE.Application/Agents/Commands/UpdateAgent/UpdateAgentCommandValidator.cs`

**Checkpoint**: `RegisterTeamAgentValidatorTests` green. All existing CQRS tests pass.

---

## Phase 5: Frontend — Team Configuration UI

**Purpose**: Add Team type support to the Agent registration form.

- [X] T018 Add Team-related TypeScript types (TeamMode, TeamConfig, HandoffTarget) to `Frontend/src/types/agent.ts`
- [X] T019 Create `ParticipantSelector` component — multi-select Agent list with async search, excludes self and Team-type agents in `Frontend/src/components/agents/ParticipantSelector.tsx`
- [X] T020 Create `HandoffRouteEditor` component — visual editor for HandoffRoutes (source → target with reason) in `Frontend/src/components/agents/HandoffRouteEditor.tsx`
- [X] T021 Create `TeamConfigForm` component — conditionally renders mode-specific fields (base config + Handoffs/Selector/MagneticOne sections) in `Frontend/src/components/agents/TeamConfigForm.tsx`
- [X] T022 Integrate `TeamConfigForm` into existing Agent register/edit page — show when AgentType=Team selected in `Frontend/src/pages/AgentCreatePage.tsx`

---

## Phase 6: Verification

**Purpose**: End-to-end validation.

- [X] T023 Manual E2E test: Create RoundRobin Team Agent (3 participants) via API → Query → Verify JSONB → Update MaxIterations → Query → Delete → Verify 404
- [X] T024 Manual E2E test: Create Handoffs Team Agent with routes via API → Verify all validation rules → Query DTO completeness
- [X] T025 Verify `dotnet build` and all test suites pass across all projects
