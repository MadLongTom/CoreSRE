# Tasks: A2A AgentCard 自动解析

**Input**: Design documents from `/specs/008-a2a-card-resolve/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅, quickstart.md ✅

**Tests**: Included per Constitution Principle II (TDD — NON-NEGOTIABLE). Test infrastructure must be established first (historical tech debt).

**Organization**: Tasks grouped by user story (US1 P1, US2 P1, US3 P2) for independent implementation and testing.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[US?]**: Which user story (US1, US2, US3) — Setup/Foundational/Polish phases have no story label
- Exact file paths included in descriptions

---

## Phase 1: Setup

**Purpose**: Add `A2A` NuGet dependency, create test project infrastructure

- [x] T001 Add `A2A` NuGet package reference to Backend/CoreSRE.Infrastructure/CoreSRE.Infrastructure.csproj
- [x] T002 Create test project Backend/CoreSRE.Application.Tests/CoreSRE.Application.Tests.csproj with xUnit, Moq, FluentAssertions, and project reference to CoreSRE.Application
- [x] T003 Create test project Backend/CoreSRE.Infrastructure.Tests/CoreSRE.Infrastructure.Tests.csproj with xUnit, Moq, FluentAssertions, and project reference to CoreSRE.Infrastructure
- [x] T004 Add both test projects to Backend/CoreSRE/CoreSRE.slnx solution file

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Interface definition and core service implementation that ALL user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T005 Define `IAgentCardResolver` interface in Backend/CoreSRE.Application/Interfaces/IAgentCardResolver.cs with method `Task<ResolvedAgentCardDto> ResolveAsync(string url, CancellationToken cancellationToken)`
- [x] T006 Create `ResolvedAgentCardDto` class in Backend/CoreSRE.Application/Agents/DTOs/ResolvedAgentCardDto.cs with properties: Name, Description, Url, Version, Skills (List\<AgentSkillDto\>), Interfaces (List\<AgentInterfaceDto\>), SecuritySchemes (List\<SecuritySchemeDto\>)
- [x] T007 Create `ResolveAgentCardQuery` record in Backend/CoreSRE.Application/Agents/Queries/ResolveAgentCard/ResolveAgentCardQuery.cs as MediatR IRequest\<Result\<ResolvedAgentCardDto\>\> with Url property
- [x] T008 Create `ResolveAgentCardQueryValidator` in Backend/CoreSRE.Application/Agents/Queries/ResolveAgentCard/ResolveAgentCardQueryValidator.cs with FluentValidation rules: Url NotEmpty, MaxLength(2048), must start with http:// or https://
- [x] T009 Write unit tests for `ResolveAgentCardQueryValidator` in Backend/CoreSRE.Application.Tests/Agents/Queries/ResolveAgentCard/ResolveAgentCardQueryValidatorTests.cs covering: empty URL, invalid scheme, valid URL, URL exceeding max length
- [x] T010 Create `A2ACardResolverService` implementing `IAgentCardResolver` in Backend/CoreSRE.Infrastructure/Services/A2ACardResolverService.cs — use A2A.A2ACardResolver SDK with 10s HttpClient timeout, map SDK AgentCard to ResolvedAgentCardDto per data-model.md field mapping rules
- [x] T011 Write unit tests for `A2ACardResolverService` in Backend/CoreSRE.Infrastructure.Tests/Services/A2ACardResolverServiceTests.cs — mock HttpClient to test: successful resolution, HTTP error → HttpRequestException, timeout → TaskCanceledException, invalid JSON → A2AException, field mapping correctness
- [x] T012 Create `ResolveAgentCardQueryHandler` in Backend/CoreSRE.Application/Agents/Queries/ResolveAgentCard/ResolveAgentCardQueryHandler.cs — inject IAgentCardResolver, call ResolveAsync, catch and map exceptions to appropriate Result error codes (502/504/422)
- [x] T013 Write unit tests for `ResolveAgentCardQueryHandler` in Backend/CoreSRE.Application.Tests/Agents/Queries/ResolveAgentCard/ResolveAgentCardQueryHandlerTests.cs — mock IAgentCardResolver to test: success path returns Result.Ok, HttpRequestException → 502, TaskCanceledException → 504, A2AException → 422
- [x] T014 Register `IAgentCardResolver` → `A2ACardResolverService` in DI container in Backend/CoreSRE/Program.cs
- [x] T015 Add `POST /api/agents/resolve-card` endpoint in Backend/CoreSRE/Endpoints/AgentEndpoints.cs — accept ResolveAgentCardQuery body, send via MediatR, return appropriate HTTP status per contract (200/400/422/502/504)

**Checkpoint**: Backend resolve-card API fully functional. Verify with `POST /api/agents/resolve-card` per quickstart.md section 1.

---

## Phase 3: User Story 1 — 通过 Endpoint 自动解析 AgentCard (Priority: P1) 🎯 MVP

**Goal**: 用户在创建 A2A Agent 时输入 Endpoint URL，点击解析按钮，表单自动填充 skills/interfaces/securitySchemes

**Independent Test**: 输入有效 A2A endpoint URL → 点击解析 → AgentCard 字段自动填充 → 用户审阅后提交创建

- [x] T016 [P] [US1] Add `ResolvedAgentCard` TypeScript type in Frontend/src/types/agent.ts with fields: name, description, url, version, skills, interfaces, securitySchemes
- [x] T017 [P] [US1] Add `resolveAgentCard(url: string, signal?: AbortSignal)` API function in Frontend/src/lib/api/agents.ts — POST to /api/agents/resolve-card, return ResolvedAgentCard, propagate typed errors
- [x] T018 [US1] Modify A2A form section in Frontend/src/pages/AgentCreatePage.tsx — add "解析" button next to Endpoint URL input, with loading spinner state and disabled-while-resolving behavior
- [x] T019 [US1] Implement resolve logic in Frontend/src/pages/AgentCreatePage.tsx — on resolve click: validate URL (http/https), call resolveAgentCard(), on success auto-fill skills/interfaces/securitySchemes form fields, on error show toast/alert with error message
- [x] T020 [US1] Implement request cancellation in Frontend/src/pages/AgentCreatePage.tsx — use AbortController to cancel in-flight resolve when URL changes or component unmounts; cancel previous request before starting new one on re-resolve

**Checkpoint**: A2A Agent creation with auto-resolve works end-to-end. Skills/interfaces/securitySchemes auto-fill from remote AgentCard.

---

## Phase 4: User Story 2 — Endpoint URL 覆写选项 (Priority: P1)

**Goal**: 当解析的 AgentCard URL 与用户输入 URL 不同时，提供覆写开关控制最终保存的 Endpoint

**Independent Test**: 解析后看到覆写开关 → 开启时用用户 URL → 关闭时用 AgentCard URL → URL 相同时开关不显示

- [x] T021 [US2] Add URL override switch UI in Frontend/src/pages/AgentCreatePage.tsx — after successful resolve, compare resolvedCard.url with user-entered URL; if different, show Switch/Checkbox labeled "使用我输入的 URL 覆盖 AgentCard 中的 URL" (default: checked); if same, hide switch
- [x] T022 [US2] Wire URL override to form submission in Frontend/src/pages/AgentCreatePage.tsx — when override is on (default), submit user-entered URL as endpoint; when off, submit resolvedCard.url as endpoint; ensure the override state resets when re-resolving

**Checkpoint**: URL override works correctly — different URLs show switch, same URLs hide it, submitted endpoint respects switch state.

---

## Phase 5: User Story 3 — AgentCard 名称与描述预填 (Priority: P2)

**Goal**: 解析成功后，自动将 AgentCard 的 name/description 预填到基本信息区域（仅当字段为空时）

**Independent Test**: 解析后检查名称和描述是否被预填；已填内容不被覆盖

- [x] T023 [US3] Implement name/description pre-fill in Frontend/src/pages/AgentCreatePage.tsx — after successful resolve, if name field is empty set it to resolvedCard.name; if description field is empty set it to resolvedCard.description; do NOT overwrite non-empty fields

**Checkpoint**: Name/description pre-fill works. Empty fields get auto-filled, non-empty fields preserved.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, cleanup, edge case hardening

- [x] T024 [P] Add resolve-card request example to Backend/CoreSRE/CoreSRE.http for quick manual API testing
- [x] T025 Run quickstart.md full validation — verify all 4 test scenarios (backend API, frontend flow, URL override, error scenarios) per quickstart.md
- [x] T026 Verify `dotnet build` succeeds and all unit tests pass across CoreSRE.Application.Tests and CoreSRE.Infrastructure.Tests

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 completion — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Phase 2 — backend API must be working
- **US2 (Phase 4)**: Depends on Phase 3 (T019) — needs resolve result to compare URLs
- **US3 (Phase 5)**: Depends on Phase 3 (T019) — needs resolve result for name/description
- **Polish (Phase 6)**: Depends on all desired user stories being complete

### Within Each Phase

```
Phase 2 (Foundational):
  T005 (interface) + T006 (DTO) → can run in parallel
  T007 (query) + T008 (validator) → depend on T006
  T009 (validator tests) → depends on T008
  T010 (service impl) → depends on T005, T006
  T011 (service tests) → depends on T010
  T012 (handler) → depends on T005, T007
  T013 (handler tests) → depends on T012
  T014 (DI registration) → depends on T010
  T015 (endpoint) → depends on T007, T014

Phase 3 (US1):
  T016 (type) + T017 (API fn) → can run in parallel
  T018 (resolve button) → depends on T017
  T019 (resolve logic) → depends on T018
  T020 (cancellation) → depends on T019
```

### Parallel Opportunities

```
# Phase 1: All setup tasks run sequentially (NuGet → test projects → solution)

# Phase 2: Interface + DTO in parallel
T005 + T006  ← parallel (different files)

# Phase 3 (US1): Type + API function in parallel
T016 + T017  ← parallel (different files)

# Phase 4 + Phase 5: Could overlap if US1 is done
# US2 (T021-T022) and US3 (T023) both modify AgentCreatePage.tsx
# → CANNOT run in parallel (same file conflicts)
# → Execute US2 first, then US3
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001–T004)
2. Complete Phase 2: Foundational (T005–T015) — backend API fully working
3. Complete Phase 3: User Story 1 (T016–T020) — end-to-end resolve + auto-fill
4. **STOP and VALIDATE**: Test with a real A2A Agent endpoint
5. Deploy/demo if ready — users can already resolve AgentCards

### Incremental Delivery

1. Setup + Foundational → Backend API ready
2. Add US1 → Test independently → Deploy (MVP! Core resolve works)
3. Add US2 → Test independently → Deploy (URL override available)
4. Add US3 → Test independently → Deploy (Name/description pre-fill)
5. Each story adds value without breaking previous stories

---

## Notes

- `A2A` NuGet package (from `a2aproject/a2a-dotnet`) provides `A2ACardResolver` — NOT `Microsoft.Agents.Client` (see research.md §1)
- SDK well-known path: `/.well-known/agent-card.json` (A2A v0.3.0 standard)
- Field mapping follows data-model.md § "Field Mapping: SDK AgentCard → ResolvedAgentCardDto"
- Error status codes follow contracts/resolve-agent-card.yaml: 400/422/502/504
- Existing `RegisterAgentCommand` and `AgentCardDto` are NOT modified — resolve is a separate query flow
- Constitution requires TDD: write tests (T009, T011, T013) BEFORE or alongside implementation, ensure they FAIL first (Red), then pass (Green)
