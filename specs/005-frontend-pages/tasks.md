# Tasks: 前端管理页面（Agent Registry + 搜索）

**Input**: Design documents from `/specs/005-frontend-pages/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅, quickstart.md ✅

**Tests**: Not requested — spec.md 未要求前端测试。Constitution TDD 原则针对后端 DDD 四层，不适用前端 SPA。

**Organization**: Tasks grouped by user story. US6 (路由/布局) is foundational — all other stories depend on it.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1–US6 from spec.md
- Paths relative to repository root (`Frontend/src/...`)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Install dependencies, create project scaffolding, define shared types and API client

- [X] T001 Install react-router dependency via `npm install react-router` in `Frontend/`
- [X] T002 Install shadcn/ui form components via `npx shadcn@latest add form input select textarea label badge dialog separator table` in `Frontend/`
- [X] T003 [P] Create TypeScript type definitions (AgentType, AgentStatus, ApiResult, AgentSummary, AgentRegistration, AgentCard, AgentSkill, AgentInterface, SecurityScheme, LlmConfig, AgentSearchResponse, AgentSearchResult, MatchedSkill, CreateAgentRequest, UpdateAgentRequest) in `Frontend/src/types/agent.ts` per data-model.md
- [X] T004 [P] Create API client with typed fetch wrapper functions (getAgents, getAgentById, createAgent, updateAgent, deleteAgent, searchAgents, ApiError class) in `Frontend/src/lib/api/agents.ts` per contracts/api-contract.md

**Checkpoint**: Dependencies installed, types defined, API client ready — foundation for all UI work

---

## Phase 2: Foundational — User Story 6: 前端路由与布局框架 (Priority: P1)

**Purpose**: Core routing and layout that MUST be complete before ANY page can be rendered

**⚠️ CRITICAL**: No page work (US1–US5) can begin until routing and layout are in place

**Goal**: React Router routes + sidebar navigation + unified layout shell

**Independent Test**: Start dev server, verify `/` redirects to `/agents`, sidebar navigation works, `/nonexistent` shows 404, browser refresh preserves route.

- [X] T005 [P] Create NotFoundPage component (404 message + link back to `/agents`) in `Frontend/src/pages/NotFoundPage.tsx`
- [X] T006 [P] Create Sidebar navigation component (links: Agent 列表 `/agents`, Agent 搜索 `/agents/search`; active route highlighting via React Router NavLink) in `Frontend/src/components/layout/Sidebar.tsx`
- [X] T007 Create AppLayout component (sidebar + main content area with `<Outlet />`) in `Frontend/src/components/layout/AppLayout.tsx`
- [X] T008 Rewrite App.tsx with React Router BrowserRouter, route config (`/` → redirect to `/agents`, `/agents` → AgentListPage, `/agents/new` → AgentCreatePage, `/agents/:id` → AgentDetailPage, `/agents/search` → AgentSearchPage, `*` → NotFoundPage), wrap routes in AppLayout in `Frontend/src/App.tsx`

**Checkpoint**: Routing + layout shell functional. Pages are placeholder stubs. Navigation between routes works. 404 page works. Sidebar highlights active route.

---

## Phase 3: User Story 1 — Agent 列表页面 (Priority: P1) 🎯 MVP

**Goal**: Table of all registered Agents with type filtering, loading/error/empty states, navigation to detail and delete actions

**Independent Test**: With backend running and agents registered, open `/agents` → table shows all agents with Name/Type/Status/CreatedAt. Select type filter → table updates. Empty state shows when no agents. Error state shows when API fails.

### Shared Components for US1

- [X] T009 [P] [US1] Create AgentTypeBadge component (color-coded badge: A2A=blue, ChatClient=green, Workflow=purple) in `Frontend/src/components/agents/AgentTypeBadge.tsx`
- [X] T010 [P] [US1] Create AgentStatusBadge component (status badge: Registered=default, Active=success, Inactive=secondary, Error=destructive) in `Frontend/src/components/agents/AgentStatusBadge.tsx`

### Page Implementation

- [X] T011 [US1] Implement AgentListPage with: table (shadcn Table) showing agents (Name clickable → `/agents/:id`, Type badge, Status badge, CreatedAt formatted), type filter Select (All/A2A/ChatClient/Workflow) calling `getAgents(type?)`, loading spinner, error message with retry button, empty state with "新建 Agent" link, delete button per row triggering DeleteAgentDialog in `Frontend/src/pages/AgentListPage.tsx`

**Checkpoint**: Agent list page fully functional — browse, filter, navigate to detail, trigger delete. MVP core complete.

---

## Phase 4: User Story 2 — Agent 注册页面 (Priority: P1)

**Goal**: Multi-step registration form with dynamic fields per AgentType, Zod validation, backend error handling

**Independent Test**: Navigate to `/agents/new`, select type, fill required fields, submit → agent created → redirect to detail page. Submit with missing fields → validation errors shown. Duplicate name → 409 error displayed.

- [X] T012 [US2] Implement AgentCreatePage with: React Hook Form + Zod schema (discriminatedUnion on agentType), step 1: type selection cards (A2A/ChatClient/Workflow with descriptions), step 2: dynamic form fields per type (A2A → name/description/endpoint + AgentCard section with dynamic skills/interfaces/securitySchemes arrays; ChatClient → name/description + LlmConfig section modelId/instructions/toolRefs; Workflow → name/description + workflowRef), submit calls `createAgent()`, success → navigate to `/agents/:id`, handle 400 validation errors (field-level display), handle 409 conflict error, loading state on submit button in `Frontend/src/pages/AgentCreatePage.tsx`

**Checkpoint**: Agent registration form works end-to-end. Dynamic field rendering per type. Validation and error handling complete.

---

## Phase 5: User Story 3 — Agent 详情与编辑页面 (Priority: P1)

**Goal**: Read-only detail view with toggle to edit mode, update via PUT, AgentType immutable in edit mode

**Independent Test**: Click agent from list → detail page shows all fields (including nested AgentCard/LlmConfig). Click Edit → fields become editable (except type). Modify and save → updates persist. Cancel → reverts changes. Non-existent ID → 404 message.

### Shared Components for US3

- [X] T013 [P] [US3] Create AgentCardSection component (read-only mode: display skills/interfaces/securitySchemes lists; edit mode: dynamic add/remove rows with form fields) in `Frontend/src/components/agents/AgentCardSection.tsx`
- [X] T014 [P] [US3] Create LlmConfigSection component (read-only mode: display modelId/instructions/toolRefs; edit mode: form fields) in `Frontend/src/components/agents/LlmConfigSection.tsx`

### Page Implementation

- [X] T015 [US3] Implement AgentDetailPage with: fetch agent via `getAgentById(id)` using useParams, read-only view (all fields, AgentCardSection, LlmConfigSection), edit button toggles to edit mode (React Hook Form + Zod, pre-populated via defaultValues), agentType field disabled in edit mode, save calls `updateAgent(id, data)`, cancel reverts to read-only (reset form), handle 404 (agent not found message + link to list), handle 400 validation errors, loading state, unsaved changes warning on navigation (beforeunload + React Router blocker) in `Frontend/src/pages/AgentDetailPage.tsx`

**Checkpoint**: Detail view + inline edit complete. Read/edit toggle works. Unsaved changes protection works.

---

## Phase 6: User Story 4 — Agent 删除确认 (Priority: P1)

**Goal**: Confirmation dialog for delete, triggered from list or detail page, prevents accidental deletion

**Independent Test**: Click delete on an agent → dialog shows agent name + warning. Confirm → agent removed from list. Cancel → no change. Double-click prevention works.

- [X] T016 [US4] Create DeleteAgentDialog component (shadcn Dialog: title "确认删除 Agent", message showing agent name + "此操作不可恢复" warning, Cancel button, Confirm Delete button with loading state + disabled during request, calls `deleteAgent(id)`, on success triggers onDeleted callback) in `Frontend/src/components/agents/DeleteAgentDialog.tsx`
- [X] T017 [US4] Integrate DeleteAgentDialog into AgentListPage (delete button per row opens dialog, on delete success remove row from state or refetch list) and AgentDetailPage (delete button in header, on delete success navigate to `/agents`) in `Frontend/src/pages/AgentListPage.tsx` and `Frontend/src/pages/AgentDetailPage.tsx`

**Checkpoint**: Delete flow complete from both list and detail. Confirmation prevents accidents. List updates after delete.

---

## Phase 7: User Story 5 — Agent 技能搜索页面 (Priority: P2)

**Goal**: Search page with debounced input, results showing matched skills with keyword highlighting, similarity scores

**Independent Test**: Navigate to `/agents/search`, type keyword, wait 300ms → results appear showing matching agents with highlighted skills. Empty query shows validation message. No results shows empty state. Click result → navigate to detail.

- [X] T018 [P] [US5] Create SkillHighlight component (highlights matching keywords in skill name/description text, accepts query string and text, wraps matches in `<mark>`) in `Frontend/src/components/agents/SkillHighlight.tsx`
- [X] T019 [US5] Implement AgentSearchPage with: search input with 300ms debounce (useEffect + setTimeout), client-side validation (non-empty query), calls `searchAgents(query)`, results display: cards/list with agent name (link to `/agents/:id`), AgentTypeBadge, matched skills with SkillHighlight, similarityScore as percentage, searchMode label, totalCount display, empty results state ("未找到匹配的 Agent 技能"), loading state during search, error handling in `Frontend/src/pages/AgentSearchPage.tsx`

**Checkpoint**: Search page functional. Debounce works. Results display with highlighting. Navigation to detail works.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Final refinements affecting multiple pages

- [X] T020 [P] Add loading skeleton/spinner component for consistent loading states across all pages in `Frontend/src/components/ui/` (or use shadcn Skeleton if available)
- [X] T021 [P] Verify all pages handle network timeout (>10s) gracefully with retry option
- [X] T022 Run `npm run build` and `npx tsc --noEmit` to verify zero TypeScript errors
- [X] T023 Run quickstart.md validation checklist (28 items) against running frontend + backend

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (US6 Routing/Layout)**: Depends on T001 (react-router installed) — BLOCKS all pages
- **Phase 3 (US1 List)**: Depends on Phase 1 + Phase 2 complete
- **Phase 4 (US2 Create)**: Depends on Phase 1 + Phase 2 complete (independent from US1)
- **Phase 5 (US3 Detail/Edit)**: Depends on Phase 1 + Phase 2 complete (independent from US1/US2)
- **Phase 6 (US4 Delete)**: Depends on T011 (AgentListPage exists) and T015 (AgentDetailPage exists)
- **Phase 7 (US5 Search)**: Depends on Phase 1 + Phase 2 complete (independent from US1–US4)
- **Phase 8 (Polish)**: Depends on all user story phases complete

### User Story Dependencies

```text
Phase 1 (Setup)
    │
    ▼
Phase 2 (US6: Routing + Layout)  ← BLOCKS ALL
    │
    ├──► Phase 3 (US1: List)  ──────────────┐
    ├──► Phase 4 (US2: Create)              ├──► Phase 6 (US4: Delete)
    ├──► Phase 5 (US3: Detail/Edit) ────────┘
    └──► Phase 7 (US5: Search)  [independent]
                                              │
                                              ▼
                                    Phase 8 (Polish)
```

### Within Each User Story

- Shared components (badges, sections) before page implementation
- Components marked [P] within a story can be built in parallel
- Page implementation integrates all components

### Parallel Opportunities

- **Phase 1**: T003 and T004 can run in parallel (different files)
- **Phase 2**: T005 and T006 can run in parallel (different files)
- **Phase 3**: T009 and T010 can run in parallel (badge components)
- **Phase 5**: T013 and T014 can run in parallel (section components)
- **After Phase 2**: US1, US2, US3, US5 can all start in parallel (different page files)

---

## Parallel Example: After Phase 2 Completes

```text
# All four stories can start simultaneously (different files):
Developer A: T009 → T010 → T011 (US1: List page)
Developer B: T012 (US2: Create page)
Developer C: T013 → T014 → T015 (US3: Detail page)
Developer D: T018 → T019 (US5: Search page)

# Then converge for US4 (Delete) which needs List + Detail pages:
Together: T016 → T017 (US4: Delete dialog integration)
```

---

## Implementation Strategy

### MVP First (User Stories 1 + 6 Only)

1. Complete Phase 1: Setup (T001–T004)
2. Complete Phase 2: US6 Routing/Layout (T005–T008)
3. Complete Phase 3: US1 Agent List (T009–T011)
4. **STOP and VALIDATE**: Browse agents, filter by type, navigate to detail (stub)
5. This delivers: working list page with routing framework

### Incremental Delivery

1. Setup + US6 (Routing) → Navigation skeleton ready
2. + US1 (List) → Browse all agents → **MVP!**
3. + US2 (Create) → Register new agents
4. + US3 (Detail/Edit) → View and modify agents
5. + US4 (Delete) → Complete CRUD lifecycle
6. + US5 (Search) → Advanced skill-based discovery
7. + Polish → Production-ready

### Single Developer Strategy (Recommended)

Execute phases sequentially in priority order:

1. Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5 → Phase 6 → Phase 7 → Phase 8

---

## Notes

- All UI uses shadcn/ui components + Tailwind CSS v4 — no custom CSS needed
- API calls use plain `fetch` wrapper — no axios/react-query
- Forms use React Hook Form + Zod (installed via `shadcn add form`)
- State is page-local (`useState` + `useEffect`) — no global store
- C# `Guid` → TypeScript `string`, C# `DateTime` → TypeScript `string` (ISO 8601)
- Search endpoint returns unwrapped `AgentSearchResponse` (not wrapped in `ApiResult`)
- `UpdateAgentRequest` has no `agentType` field — type is immutable after creation
- Commit after each task or logical group for clean git history
