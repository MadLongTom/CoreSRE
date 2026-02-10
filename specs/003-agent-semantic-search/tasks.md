# Tasks: Agent 能力语义搜索

**Input**: Design documents from `/specs/003-agent-semantic-search/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅, quickstart.md ✅

**Tests**: Not explicitly requested in specification. Test tasks are NOT included.

**Organization**: Tasks grouped by user story. Only P1 (keyword search) is in scope per plan.md. P2 (semantic search) is documented as future work.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1)
- Exact file paths included in all descriptions

## Path Conventions

- **Backend layers**: `Backend/CoreSRE.Domain/`, `Backend/CoreSRE.Application/`, `Backend/CoreSRE.Infrastructure/`, `Backend/CoreSRE/`
- Follows existing DDD 4-layer architecture from SPEC-001

---

## Phase 1: Setup

**Purpose**: No new project initialization needed — this feature extends the existing SPEC-001 codebase. Setup phase ensures folder structure exists for new files.

- [X] T001 Create directory structure for SearchAgents query in `Backend/CoreSRE.Application/Agents/Queries/SearchAgents/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: DTOs and domain interface that ALL user story tasks depend on. These are shared types referenced by both query handler and endpoint.

**⚠️ CRITICAL**: No US1 implementation tasks can begin until this phase is complete.

- [X] T002 [P] Create `MatchedSkillDto` record in `Backend/CoreSRE.Application/Agents/DTOs/MatchedSkillDto.cs` — properties: `Name` (string), `Description` (string?) per data-model.md
- [X] T003 [P] Create `AgentSearchResultDto` class in `Backend/CoreSRE.Application/Agents/DTOs/AgentSearchResultDto.cs` — properties: `Id`, `Name`, `AgentType`, `Status`, `CreatedAt`, `MatchedSkills` (List\<MatchedSkillDto\>), `SimilarityScore` (double?) per data-model.md
- [X] T004 [P] Create `AgentSearchResponse` class in `Backend/CoreSRE.Application/Agents/DTOs/AgentSearchResponse.cs` — properties: `Results` (List\<AgentSearchResultDto\>), `SearchMode` (string), `Query` (string), `TotalCount` (int) per data-model.md and contracts/agents-search-api.yaml
- [X] T005 Add `SearchBySkillAsync(string searchTerm, CancellationToken)` method signature to `IAgentRegistrationRepository` in `Backend/CoreSRE.Domain/Interfaces/IAgentRegistrationRepository.cs` — returns `Task<IReadOnlyList<AgentRegistration>>` per data-model.md

**Checkpoint**: All shared types defined. Domain interface extended. Implementation can proceed.

---

## Phase 3: User Story 1 — 按关键词搜索 Agent 技能 (Priority: P1) 🎯 MVP

**Goal**: Implement keyword-based skill search via `GET /api/agents/search?q={query}`. Case-insensitive ILIKE matching against JSONB skill name/description. Results sorted by matched skill count descending.

**Independent Test**: Register A2A Agents with various skills, send `GET /api/agents/search?q=customer`, verify response contains only Agents whose skill name/description contains "customer", sorted by match count, with `searchMode: "keyword"`.

### Implementation for User Story 1

- [X] T006 [P] [US1] Create `SearchAgentsQuery` MediatR request record in `Backend/CoreSRE.Application/Agents/Queries/SearchAgents/SearchAgentsQuery.cs` — `record SearchAgentsQuery(string Query) : IRequest<Result<AgentSearchResponse>>` per data-model.md CQRS section
- [X] T007 [P] [US1] Create `SearchAgentsQueryValidator` in `Backend/CoreSRE.Application/Agents/Queries/SearchAgents/SearchAgentsQueryValidator.cs` — FluentValidation rules: `Query` NotEmpty, MaximumLength(500), Must not be whitespace-only per spec.md FR-006/FR-007 and edge cases
- [X] T008 [US1] Implement `SearchBySkillAsync` in `Backend/CoreSRE.Infrastructure/Persistence/AgentRegistrationRepository.cs` — hybrid approach: raw SQL with `EXISTS(SELECT 1 FROM jsonb_array_elements(agent_card->'Skills') WHERE value->>'Name' ILIKE @term OR value->>'Description' ILIKE @term)` to get matching Agent IDs, then EF Core query by ID list to load full entities with owned JSON navigation properties per research.md R1/R2 decisions
- [X] T009 [US1] Implement `SearchAgentsQueryHandler` in `Backend/CoreSRE.Application/Agents/Queries/SearchAgents/SearchAgentsQueryHandler.cs` — call `SearchBySkillAsync`, extract matched skills in C# (case-insensitive Contains on each skill's Name/Description), build `AgentSearchResultDto` list sorted by `MatchedSkills.Count` descending, wrap in `AgentSearchResponse` with `SearchMode = "keyword"` per data-model.md handler logic and spec.md FR-004/FR-005
- [X] T010 [US1] Add `GET /search` route to `AgentEndpoints` in `Backend/CoreSRE/Endpoints/AgentEndpoints.cs` — `group.MapGet("/search", SearchAgents)` placed BEFORE `/{id:guid}` routes, handler accepts `[FromQuery] string? q`, sends `SearchAgentsQuery` via MediatR, returns `Ok(response)` on success or `BadRequest` on validation failure per contracts/agents-search-api.yaml and research.md R6 decision
- [X] T011 [US1] Build and verify compilation with `dotnet build Backend/CoreSRE/CoreSRE.slnx` — ensure zero errors and zero warnings

**Checkpoint**: User Story 1 fully functional. `GET /api/agents/search?q=customer` returns matching A2A Agents with matched skills, sorted by relevance. All edge cases handled (empty query → 400, no match → empty 200, case-insensitive, A2A-only).

---

## Phase 4: Polish & Cross-Cutting Concerns

**Purpose**: End-to-end validation and documentation

- [X] T012 Run quickstart.md validation — skipped (build verification sufficient per user) — start Aspire AppHost, register test Agents per quickstart.md Step 1, execute all search queries from Steps 2–4, verify all expected responses match

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — directory creation only
- **Foundational (Phase 2)**: Depends on Phase 1 — defines all shared types
- **User Story 1 (Phase 3)**: Depends on Phase 2 — uses DTOs, response types, and repository interface
- **Polish (Phase 4)**: Depends on Phase 3 — validates the complete feature

### Within User Story 1

```
T006 (Query record) ──┐
T007 (Validator)    ──┤── can run in parallel (different files)
                      │
T008 (Repository)  ───┘── can run in parallel with T006/T007 (different layer)
                      │
T009 (Handler)     ───── depends on T006 (uses SearchAgentsQuery), T008 (calls SearchBySkillAsync)
                      │
T010 (Endpoint)    ───── depends on T006 (sends SearchAgentsQuery), T009 (handler must exist)
                      │
T011 (Build)       ───── depends on all above
```

### User Story Dependencies

- **User Story 1 (P1)**: Only depends on Foundational phase. No other story dependencies.
- **User Story 2 (P2)**: Future scope — not included in this task list per plan.md decision.

### Parallel Opportunities

- **Phase 2**: T002, T003, T004, T005 all create different files — run in parallel
- **Phase 3**: T006, T007, T008 create different files — run in parallel; T009 depends on T006+T008; T010 depends on T009

---

## Parallel Example: User Story 1

```bash
# After Phase 2 completes, launch these in parallel:
Task T006: "Create SearchAgentsQuery in Backend/CoreSRE.Application/Agents/Queries/SearchAgents/SearchAgentsQuery.cs"
Task T007: "Create SearchAgentsQueryValidator in Backend/CoreSRE.Application/Agents/Queries/SearchAgents/SearchAgentsQueryValidator.cs"
Task T008: "Implement SearchBySkillAsync in Backend/CoreSRE.Infrastructure/Persistence/AgentRegistrationRepository.cs"

# Then sequentially:
Task T009: "Implement SearchAgentsQueryHandler" (needs T006 + T008)
Task T010: "Add GET /search route to AgentEndpoints" (needs T009)
Task T011: "Build verification"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001)
2. Complete Phase 2: Foundational (T002–T005, all parallel)
3. Complete Phase 3: User Story 1 (T006–T011)
4. **STOP and VALIDATE**: Run quickstart.md validation (T012)
5. Feature complete for P1 scope

### Incremental Delivery

1. Setup + Foundational → Shared types ready
2. User Story 1 → Keyword search operational → **Deploy/Demo (MVP!)**
3. User Story 2 (future) → Semantic search with IEmbeddingGenerator → Deploy/Demo
4. Each story adds search capability without breaking previous functionality

---

## Notes

- P2 (semantic search) is explicitly excluded from this task list per plan.md "P2 不在本 plan 的实现范围内"
- No test tasks generated — tests not explicitly requested in spec.md or by user
- Total: 12 tasks (1 setup + 4 foundational + 6 US1 implementation + 1 polish)
- All new files follow existing naming conventions (PascalCase, one class per file)
- Repository implementation uses parameterized SQL to prevent injection (spec.md edge case)
- `SearchMode` field in response is forward-compatible with P2 values ("semantic", "keyword-fallback")
