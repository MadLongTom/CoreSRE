# Tasks: LLM Provider 配置与模型发现

**Input**: Design documents from `/specs/006-llm-provider-config/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅, quickstart.md ✅

**Tests**: Not requested — no test tasks generated.

**Organization**: Tasks grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: New LlmProvider domain entity, repository, EF configuration, DB migration, DI wiring

- [X] T001 Create LlmProvider aggregate root entity in Backend/CoreSRE.Domain/Entities/LlmProvider.cs
- [X] T002 [P] Extend LlmConfigVO with nullable ProviderId in Backend/CoreSRE.Domain/ValueObjects/LlmConfigVO.cs
- [X] T003 [P] Create ILlmProviderRepository interface in Backend/CoreSRE.Domain/Interfaces/ILlmProviderRepository.cs
- [X] T004 [P] Create IModelDiscoveryService interface in Backend/CoreSRE.Application/Common/Interfaces/IModelDiscoveryService.cs
- [X] T005 Create LlmProviderConfiguration EF mapping in Backend/CoreSRE.Infrastructure/Persistence/Configurations/LlmProviderConfiguration.cs
- [X] T006 Add DbSet\<LlmProvider\> to AppDbContext in Backend/CoreSRE.Infrastructure/Persistence/AppDbContext.cs
- [X] T007 Create LlmProviderRepository implementation in Backend/CoreSRE.Infrastructure/Persistence/LlmProviderRepository.cs
- [X] T008 Create EF Core migration for llm_providers table (handled by EnsureCreatedAsync in dev)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Application layer DTOs, AutoMapper profile, ModelDiscoveryService, DI registration — MUST complete before any user story

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T009 [P] Create LlmProviderDto in Backend/CoreSRE.Application/Providers/DTOs/LlmProviderDto.cs
- [X] T010 [P] Create LlmProviderSummaryDto in Backend/CoreSRE.Application/Providers/DTOs/LlmProviderSummaryDto.cs
- [X] T011 [P] Create DiscoveredModelDto in Backend/CoreSRE.Application/Providers/DTOs/DiscoveredModelDto.cs
- [X] T012 [P] Create ProviderMappingProfile (AutoMapper) in Backend/CoreSRE.Application/Providers/DTOs/ProviderMappingProfile.cs
- [X] T013 [P] Extend LlmConfigDto with ProviderId and ProviderName in Backend/CoreSRE.Application/Agents/DTOs/LlmConfigDto.cs
- [X] T014 [P] Update AgentMappingProfile to map ProviderId in Backend/CoreSRE.Application/Agents/DTOs/AgentMappingProfile.cs
- [X] T015 Create ModelDiscoveryService (HttpClient implementation) in Backend/CoreSRE.Infrastructure/Services/ModelDiscoveryService.cs
- [X] T016 Register ILlmProviderRepository, IModelDiscoveryService, and named HttpClient in Backend/CoreSRE.Infrastructure/DependencyInjection.cs
- [X] T017 [P] Create provider TypeScript types in Frontend/src/types/provider.ts
- [X] T018 [P] Extend LlmConfig type with providerId and providerName in Frontend/src/types/agent.ts
- [X] T019 Create provider API client functions in Frontend/src/lib/api/providers.ts

**Checkpoint**: Foundation ready — user story implementation can now begin

---

## Phase 3: User Story 1 — 注册 LLM Provider (Priority: P1) 🎯 MVP

**Goal**: Admin can register a new OpenAI-compatible Provider with name, base URL, and API key; provider appears in list with masked API key

**Independent Test**: POST /api/providers with valid payload → 201; GET /api/providers → list includes new provider; duplicate name → 409

### Implementation for User Story 1

- [X] T020 [P] [US1] Create RegisterProviderCommand and handler in Backend/CoreSRE.Application/Providers/Commands/RegisterProvider/RegisterProviderCommand.cs and RegisterProviderCommandHandler.cs
- [X] T021 [P] [US1] Create RegisterProviderCommandValidator in Backend/CoreSRE.Application/Providers/Commands/RegisterProvider/RegisterProviderCommandValidator.cs
- [X] T022 [P] [US1] Create GetProvidersQuery and handler in Backend/CoreSRE.Application/Providers/Queries/GetProviders/GetProvidersQuery.cs and GetProvidersQueryHandler.cs
- [X] T023 [P] [US1] Create GetProviderByIdQuery and handler in Backend/CoreSRE.Application/Providers/Queries/GetProviderById/GetProviderByIdQuery.cs and GetProviderByIdQueryHandler.cs
- [X] T024 [US1] Create ProviderEndpoints (POST /api/providers, GET /api/providers, GET /api/providers/{id}) in Backend/CoreSRE/Endpoints/ProviderEndpoints.cs
- [X] T025 [US1] Register ProviderEndpoints in Backend/CoreSRE/Program.cs (app.MapProviderEndpoints)
- [X] T026 [P] [US1] Create ProviderListPage in Frontend/src/pages/ProviderListPage.tsx
- [X] T027 [US1] Create ProviderDetailPage in Frontend/src/pages/ProviderDetailPage.tsx
- [X] T028 [US1] Add /providers and /providers/:id routes in Frontend/src/App.tsx
- [X] T029 [US1] Add Provider nav link in Frontend/src/components/layout/Sidebar.tsx

**Checkpoint**: Provider CRUD (create + list + detail) works end-to-end, API key masked in responses

---

## Phase 4: User Story 2 — 发现可用模型列表 (Priority: P1)

**Goal**: Admin triggers model discovery on a registered Provider; system calls GET {baseUrl}/models, stores model IDs, displays them with refresh timestamp

**Independent Test**: POST /api/providers/{id}/discover → 200 with discoveredModels populated; GET /api/providers/{id}/models → model list; unreachable endpoint → 502

### Implementation for User Story 2

- [X] T030 [P] [US2] Create DiscoverModelsCommand and handler in Backend/CoreSRE.Application/Providers/Commands/DiscoverModels/DiscoverModelsCommand.cs and DiscoverModelsCommandHandler.cs
- [X] T031 [P] [US2] Create GetProviderModelsQuery and handler in Backend/CoreSRE.Application/Providers/Queries/GetProviderModels/GetProviderModelsQuery.cs and GetProviderModelsQueryHandler.cs
- [X] T032 [US2] Add discover and models endpoints (POST /api/providers/{id}/discover, GET /api/providers/{id}/models) to Backend/CoreSRE/Endpoints/ProviderEndpoints.cs
- [X] T033 [US2] Add model discovery UI (Discover Models button, model list, refresh timestamp) to Frontend/src/pages/ProviderDetailPage.tsx

**Checkpoint**: Model discovery works end-to-end; discovered models persist and display on provider detail page

---

## Phase 5: User Story 3 — 创建 ChatClient 时选择 Provider 和模型 (Priority: P1)

**Goal**: When creating a ChatClient agent, user selects Provider from dropdown then selects model from that Provider's discovered models; LlmConfig stores providerId + modelId

**Independent Test**: Create ChatClient → select provider → model dropdown loads → submit → agent created with providerId and modelId stored

### Implementation for User Story 3

- [X] T034 [P] [US3] Create ProviderModelSelect cascading component in Frontend/src/components/agents/ProviderModelSelect.tsx
- [X] T035 [US3] Integrate ProviderModelSelect into ChatClient form in Frontend/src/pages/AgentCreatePage.tsx (replace free-text modelId input)
- [X] T036 [US3] Update LlmConfigSection to display Provider name and Model in read mode in Frontend/src/components/agents/LlmConfigSection.tsx

**Checkpoint**: ChatClient creation uses cascading Provider→Model select; created agent stores providerId; detail page shows provider name and model

---

## Phase 6: User Story 4 — 管理 Provider（编辑与删除）(Priority: P2)

**Goal**: Admin can edit Provider name/URL/key and delete unused Providers; deletion blocked if Agent references exist

**Independent Test**: PUT /api/providers/{id} → 200 with updated fields; DELETE unreferenced provider → 200; DELETE referenced provider → 409 with agent count

### Implementation for User Story 4

- [X] T037 [P] [US4] Create UpdateProviderCommand, handler, and validator in Backend/CoreSRE.Application/Providers/Commands/UpdateProvider/UpdateProviderCommand.cs, UpdateProviderCommandHandler.cs, UpdateProviderCommandValidator.cs
- [X] T038 [P] [US4] Create DeleteProviderCommand and handler (with agent reference check) in Backend/CoreSRE.Application/Providers/Commands/DeleteProvider/DeleteProviderCommand.cs and DeleteProviderCommandHandler.cs
- [X] T039 [US4] Add update and delete endpoints (PUT /api/providers/{id}, DELETE /api/providers/{id}) to Backend/CoreSRE/Endpoints/ProviderEndpoints.cs
- [X] T040 [P] [US4] Create DeleteProviderDialog confirmation component in Frontend/src/components/providers/DeleteProviderDialog.tsx
- [X] T041 [US4] Add edit form and delete button to Frontend/src/pages/ProviderDetailPage.tsx

**Checkpoint**: Provider edit/delete works; referenced provider deletion is blocked with clear error message

---

## Phase 7: User Story 5 — 编辑 ChatClient 时切换 Provider 或模型 (Priority: P2)

**Goal**: When editing an existing ChatClient agent, user can change Provider and/or model; switching Provider clears model selection and reloads model list

**Independent Test**: Edit ChatClient → current provider/model pre-selected → switch provider → model list updates → save → agent updated with new providerId/modelId

### Implementation for User Story 5

- [X] T042 [US5] Integrate ProviderModelSelect into edit mode of AgentDetailPage in Frontend/src/pages/AgentDetailPage.tsx
- [X] T043 [US5] Ensure UpdateAgentCommand handler persists updated ProviderId in Backend/CoreSRE.Application/Agents/Commands/UpdateAgent/UpdateAgentCommandHandler.cs

**Checkpoint**: ChatClient agent edit mode supports provider/model switching with cascading dropdown behavior

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [X] T044 [P] Update AgentRegistrationConfiguration to map LlmConfigVO.ProviderId in JSONB in Backend/CoreSRE.Infrastructure/Persistence/Configurations/AgentRegistrationConfiguration.cs
- [X] T045 [P] Add empty-state messaging for no-providers scenario in Frontend/src/pages/AgentCreatePage.tsx
- [X] T046 Run quickstart.md validation checklist for all 5 user stories
- [X] T047 Verify EF migration applies cleanly and existing agents with null ProviderId are unaffected

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 completion — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Phase 2 — Provider CRUD backend + frontend
- **US2 (Phase 4)**: Depends on Phase 2 + ProviderEndpoints from US1 (T024) — extends existing endpoints
- **US3 (Phase 5)**: Depends on Phase 2 + US2 (needs discovered models to populate dropdown)
- **US4 (Phase 6)**: Depends on Phase 2 + US1 (needs existing provider CRUD endpoints to extend)
- **US5 (Phase 7)**: Depends on US3 (ProviderModelSelect component) + US4 (edit support)
- **Polish (Phase 8)**: Depends on all user stories being complete

### User Story Dependencies

- **US1 (P1)**: Can start after Phase 2 — No dependencies on other stories
- **US2 (P1)**: Depends on US1 T024 (ProviderEndpoints.cs exists) — extends the same endpoint file
- **US3 (P1)**: Depends on US2 (needs GET /api/providers/{id}/models for dropdown population)
- **US4 (P2)**: Depends on US1 (needs ProviderEndpoints.cs and ProviderDetailPage.tsx to extend)
- **US5 (P2)**: Depends on US3 (ProviderModelSelect component) and US4 (edit form in ProviderDetailPage)

### Within Each User Story

- Commands/Queries before Endpoints
- Endpoints before Frontend pages
- Backend before Frontend (API must exist for frontend to call)

### Parallel Opportunities

**Phase 1 parallel group** (T002, T003, T004 can run simultaneously — different files):
```
T002: LlmConfigVO.cs  |  T003: ILlmProviderRepository.cs  |  T004: IModelDiscoveryService.cs
```

**Phase 2 parallel group** (T009–T014, T017, T018 can run simultaneously — different files):
```
T009: LlmProviderDto  |  T010: LlmProviderSummaryDto  |  T011: DiscoveredModelDto
T012: ProviderMappingProfile  |  T013: LlmConfigDto  |  T014: AgentMappingProfile
T017: provider.ts  |  T018: agent.ts
```

**US1 backend parallel** (T020, T021, T022, T023 — different CQRS slices):
```
T020: RegisterProvider  |  T022: GetProviders  |  T023: GetProviderById
```

**US4 backend parallel** (T037, T038 — different command slices):
```
T037: UpdateProvider  |  T038: DeleteProvider
```

---

## Implementation Strategy

### MVP First (User Stories 1 + 2 + 3)

1. Complete Phase 1: Setup → LlmProvider entity + interfaces + EF + migration
2. Complete Phase 2: Foundational → DTOs + ModelDiscoveryService + DI + frontend types
3. Complete Phase 3: US1 → Provider register + list + detail (backend + frontend)
4. Complete Phase 4: US2 → Model discovery endpoint + UI
5. Complete Phase 5: US3 → Cascading Provider→Model select in ChatClient creation
6. **STOP and VALIDATE**: Test all P1 stories independently via quickstart.md

### Incremental Delivery

1. Setup + Foundational → Infrastructure ready
2. Add US1 → Provider CRUD works → Demo-ready (partial MVP)
3. Add US2 → Model discovery works → Demo-ready (core MVP)
4. Add US3 → ChatClient creation improved → **Full MVP! 🎯**
5. Add US4 → Provider edit/delete lifecycle complete
6. Add US5 → ChatClient edit supports provider switching → **Feature complete**
7. Polish → Migration validation + empty-state UX

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- US2 extends US1's ProviderEndpoints.cs — must run sequentially
- US3 requires US2's model discovery data — cannot parallelize with US2
- API Key is NEVER returned in plain text — always use MaskApiKey()
- ProviderId is Guid? (nullable) for backward compatibility with existing agents
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
