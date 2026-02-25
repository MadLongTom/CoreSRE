# Tasks: Team Mode Agent Chat

**Input**: Design documents from `/specs/021-team-agent-chat/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included — Constitution mandates TDD (Red-Green-Refactor). Tests are written first and must fail before implementation.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization — no new project scaffolding needed (existing repo), but foundational types and interfaces must be created.

- [X] T001 Add team chat TypeScript types (OuterLedger, InnerLedgerEntry, TeamProgress, participantAgent fields on ChatMessage) in Frontend/src/types/chat.ts
- [X] T002 [P] Create TeamChatEventDto hierarchy (TeamHandoffEventDto, TeamLedgerUpdateEventDto, TeamProgressEventDto) in Backend/CoreSRE.Application/Chat/DTOs/TeamChatEventDto.cs
- [X] T003 [P] Create ITeamOrchestrator interface in Backend/CoreSRE.Application/Interfaces/ITeamOrchestrator.cs
- [X] T004 Register ITeamOrchestrator in DI container in Backend/CoreSRE.Application/DependencyInjection.cs or Backend/CoreSRE.Infrastructure/DependencyInjection.cs

**Checkpoint**: All shared types, interfaces, and DI wiring in place. No behavior yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core backend orchestration engine that MUST be complete before any user story can deliver end-to-end functionality.

**CRITICAL**: No user story work can begin until this phase is complete — the orchestration engine is the backbone for all 6 modes.

### Tests

- [X] T005 Write TeamOrchestratorService unit tests for Sequential mode (BuildTeamAgent returns valid AIAgent, agents called in order) in Backend/CoreSRE.Application.Tests/Agents/TeamOrchestratorTests.cs
- [X] T006 [P] Write TeamOrchestratorService unit tests for Concurrent mode (agents fan-out, aggregator called) in Backend/CoreSRE.Application.Tests/Agents/TeamOrchestratorTests.cs
- [X] T007 [P] Write TeamOrchestratorService unit tests for RoundRobin mode (RoundRobinGroupChatManager used) in Backend/CoreSRE.Application.Tests/Agents/TeamOrchestratorTests.cs
- [X] T008 [P] Write TeamOrchestratorService unit tests for Handoffs mode (handoff routes wired, initial agent correct) in Backend/CoreSRE.Application.Tests/Agents/TeamOrchestratorTests.cs
- [X] T009 [P] Write TeamOrchestratorService unit tests for Selector mode (LlmSelectorGroupChatManager constructed with provider) in Backend/CoreSRE.Application.Tests/Agents/TeamOrchestratorTests.cs
- [X] T010 [P] Write TeamOrchestratorService unit tests for MagneticOne mode (MagneticOneGroupChatManager constructed with orchestrator LLM) in Backend/CoreSRE.Application.Tests/Agents/TeamOrchestratorTests.cs
- [X] T011 [P] Write TeamOrchestratorService unit tests for error cases (empty participants, inactive participant, deleted participant, maxIterations enforcement) in Backend/CoreSRE.Application.Tests/Agents/TeamOrchestratorTests.cs

### Implementation

- [X] T012 Implement TeamOrchestratorService with Sequential and Concurrent mode support using AgentWorkflowBuilder in Backend/CoreSRE.Infrastructure/Services/TeamOrchestratorService.cs
- [X] T013 Add RoundRobin mode to TeamOrchestratorService using BuildGroupChat with RoundRobinGroupChatManager in Backend/CoreSRE.Infrastructure/Services/TeamOrchestratorService.cs
- [X] T014 Add Handoffs mode to TeamOrchestratorService using BuildHandoffs with WithHandoff from TeamConfigVO.HandoffRoutes in Backend/CoreSRE.Infrastructure/Services/TeamOrchestratorService.cs
- [X] T015 Implement LlmSelectorGroupChatManager (custom GroupChatManager using LLM to select next speaker) in Backend/CoreSRE.Infrastructure/Services/LlmSelectorGroupChatManager.cs
- [X] T016 Add Selector mode to TeamOrchestratorService using BuildGroupChat with LlmSelectorGroupChatManager in Backend/CoreSRE.Infrastructure/Services/TeamOrchestratorService.cs
- [X] T017 Implement MagneticOneGroupChatManager (dual-loop ledger: outer plan + inner task log, stall detection) in Backend/CoreSRE.Infrastructure/Services/MagneticOneGroupChatManager.cs
- [X] T018 Add MagneticOne mode to TeamOrchestratorService using BuildGroupChat with MagneticOneGroupChatManager in Backend/CoreSRE.Infrastructure/Services/TeamOrchestratorService.cs
- [X] T019 Add participant validation logic (all exist, all Active, no Team nesting, name uniqueness for Handoffs) to TeamOrchestratorService in Backend/CoreSRE.Infrastructure/Services/TeamOrchestratorService.cs

**Checkpoint**: All 6 orchestration modes buildable. TeamOrchestratorService tests pass green.

---

## Phase 3: User Story 1 — Select a Team Agent and Start Conversation (Priority: P1) MVP

**Goal**: Users can select a Team agent from the Chat page agent selector and send a message that flows through the orchestration pipeline.

**Independent Test**: Create a Team agent via CRUD, open Chat, select Team agent, send message, receive orchestrated response.

### Tests

- [X] T020 Write AgentResolverService test for Team type resolution (resolves participants, calls ITeamOrchestrator, returns ResolvedAgent) in Backend/CoreSRE.Application.Tests/Agents/TeamOrchestratorTests.cs
- [X] T021 [P] Write AgentResolverService test for Team type error cases (participant not found throws, inactive participant throws) in Backend/CoreSRE.Application.Tests/Agents/TeamOrchestratorTests.cs

### Backend Implementation

- [X] T022 [US1] Add Team case to AgentResolverService.ResolveAsync switch — resolve each participant, call ITeamOrchestrator.BuildTeamAgent, return ResolvedAgent in Backend/CoreSRE.Infrastructure/Services/AgentResolverService.cs
- [X] T023 [US1] Add HandleTeamStreamAsync method to AgentChatEndpoints — SSE streaming via aiAgent.RunStreamingAsync with RUN_STARTED, TEXT_MESSAGE_*, RUN_FINISHED events in Backend/CoreSRE/Endpoints/AgentChatEndpoints.cs
- [X] T024 [US1] Wire Team agent routing in HandleAgentChat — detect Team type from ResolvedAgent metadata and route to HandleTeamStreamAsync in Backend/CoreSRE/Endpoints/AgentChatEndpoints.cs

### Frontend Implementation

- [X] T025 [US1] Update AgentSelector to include Team-type agents in the filter (remove ChatClient/A2A-only restriction on line 36) in Frontend/src/components/chat/AgentSelector.tsx
- [X] T026 [US1] Update use-agent-chat.ts hook to handle basic Team SSE events (TEXT_MESSAGE_START/CONTENT/END with participantAgentId) in Frontend/src/hooks/use-agent-chat.ts

**Checkpoint**: Team agent selectable, message sent, orchestrated response streamed back. Core E2E path works.

---

## Phase 4: User Story 2 — See Which Participant Agent Is Speaking (Priority: P1)

**Goal**: Each assistant message bubble in a Team conversation shows the originating participant agent's name. Handoff notifications and concurrent separate bubbles work.

**Independent Test**: Start Sequential team chat, verify each response bubble labeled with participant name. Start Handoffs chat, verify handoff notification appears.

### Backend Implementation

- [X] T027 [US2] Emit participantAgentId and participantAgentName in TEXT_MESSAGE_START and TOOL_CALL_START events from HandleTeamStreamAsync by reading AgentResponseUpdate.AgentId/AgentName in Backend/CoreSRE/Endpoints/AgentChatEndpoints.cs
- [X] T028 [US2] Emit TEAM_HANDOFF SSE event in HandleTeamStreamAsync when handoff tool call detected (tool name matches handoff_to_* pattern) in Backend/CoreSRE/Endpoints/AgentChatEndpoints.cs

### Frontend Implementation

- [X] T029 [US2] Update MessageBubble to display participantAgentName label on assistant messages when present in Frontend/src/components/chat/MessageBubble.tsx
- [X] T030 [US2] Update use-agent-chat.ts to track multiple in-flight messages by participantAgentId for concurrent mode (Map<string, ChatMessage> for parallel bubbles) in Frontend/src/hooks/use-agent-chat.ts
- [X] T031 [US2] Create HandoffNotification component showing "🔀 Agent A handed off to Agent B" system message in Frontend/src/components/chat/HandoffNotification.tsx
- [X] T032 [US2] Handle TEAM_HANDOFF SSE event in use-agent-chat.ts — insert HandoffNotification into message stream in Frontend/src/hooks/use-agent-chat.ts

**Checkpoint**: All assistant bubbles attributed. Handoff notifications visible. Concurrent bubbles appear separately. US1+US2 = core Team chat experience complete.

---

## Phase 5: User Story 3 — Team Agent Visual Differentiation (Priority: P2)

**Goal**: Team agents are visually distinct in the agent selector and conversation list with a team icon/badge.

**Independent Test**: Open agent selector with mixed agent types, verify Team agents have distinctive icon. Check conversation sidebar for team indicator.

### Frontend Implementation

- [X] T033 [P] [US3] Add team icon/badge to AgentSelector for Team-type agents (e.g., Users icon from lucide-react) in Frontend/src/components/chat/AgentSelector.tsx
- [X] T034 [P] [US3] Add team mode indicator to conversation list sidebar for Team conversations (show mode label from TEAM_MODE_LABELS) in Frontend/src/components/chat/AgentSelector.tsx or conversation list component

**Checkpoint**: Team agents visually identifiable at a glance. No functional changes.

---

## Phase 6: User Story 4 — Team Orchestration Progress Indicator (Priority: P2)

**Goal**: Users see real-time progress of which participant agent is active. MagneticOne mode shows collapsible ledger side panel.

**Independent Test**: Start Sequential team, observe step indicator. Start MagneticOne team, observe ledger panel with outer/inner updates.

### Backend Implementation

- [X] T035 [US4] Emit TEAM_PROGRESS SSE event in HandleTeamStreamAsync on agent transitions (track current vs previous AgentName, emit step/totalSteps for Sequential) in Backend/CoreSRE/Endpoints/AgentChatEndpoints.cs
- [X] T036 [P] [US4] Emit TEAM_LEDGER_UPDATE SSE events from MagneticOneGroupChatManager via callback mechanism (outer ledger on plan change, inner ledger on agent completion) in Backend/CoreSRE.Infrastructure/Services/MagneticOneGroupChatManager.cs

### Frontend Implementation

- [X] T037 [US4] Create TeamProgressIndicator component showing current agent step (e.g., "Agent 2/3: LogAnalyzer is thinking...") in Frontend/src/components/chat/TeamProgressIndicator.tsx
- [X] T038 [US4] Handle TEAM_PROGRESS SSE event in use-agent-chat.ts — update teamProgress state in Frontend/src/hooks/use-agent-chat.ts
- [X] T039 [US4] Create MagneticOneLedger collapsible side panel component (outer ledger at top, inner ledger entries below) in Frontend/src/components/chat/MagneticOneLedger.tsx
- [X] T040 [US4] Handle TEAM_LEDGER_UPDATE SSE event in use-agent-chat.ts — update outerLedger and innerLedgerEntries state in Frontend/src/hooks/use-agent-chat.ts
- [X] T041 [US4] Update ChatPage layout to conditionally render MagneticOneLedger side panel when team mode is MagneticOne in Frontend/src/pages/ChatPage.tsx
- [X] T042 [US4] Render TeamProgressIndicator in ChatPage below message input area for all Team conversations in Frontend/src/pages/ChatPage.tsx

**Checkpoint**: Progress indicator live for all modes. MagneticOne ledger panel functional with real-time updates.

---

## Phase 7: User Story 5 — Reload and Continue Team Conversation (Priority: P3)

**Goal**: Team conversation history persists across page reloads with full participant attribution intact.

**Independent Test**: Complete a Team conversation, refresh page, reopen conversation, verify all messages retain participant labels.

### Backend Implementation

- [X] T043 [US5] Persist team session via sessionStore.SaveSessionAsync in HandleTeamStreamAsync after RUN_FINISHED (participant attribution preserved in SessionData JSONB) in Backend/CoreSRE/Endpoints/AgentChatEndpoints.cs
- [X] T044 [US5] Extend ChatMessage DTO projection to read participantAgentId and participantAgentName from SessionData JSONB when present in Backend/CoreSRE/Endpoints/AgentChatEndpoints.cs or conversation detail endpoint

### Frontend Implementation

- [X] T045 [US5] Ensure ChatMessage rendering displays participantAgentName on reloaded messages (same MessageBubble attribution from US2 applied to loaded history) in Frontend/src/components/chat/MessageBubble.tsx

**Checkpoint**: Full conversation history with attribution survives page reload. All 5 user stories functional.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Error handling, edge cases, and final validation across all stories.

- [X] T046 Add error handling for participant agent failures in HandleTeamStreamAsync — emit RUN_ERROR with participantAgentName attribution (FR-011) in Backend/CoreSRE/Endpoints/AgentChatEndpoints.cs
- [X] T047 [P] Add maxIterations reached notification — detect iteration limit in orchestration and emit user-visible error event (FR-009) in Backend/CoreSRE/Endpoints/AgentChatEndpoints.cs
- [X] T048 [P] Handle single-participant Team edge case — verify pass-through behavior works correctly in Backend/CoreSRE.Infrastructure/Services/TeamOrchestratorService.cs
- [X] T049 [P] Add frontend error display for team-specific errors (participant failure, maxIterations reached) in Frontend/src/hooks/use-agent-chat.ts
- [X] T050 Run quickstart.md validation — manually test all 6 modes per quickstart.md scenarios

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup (T003 interface needed for T005+ tests) — **BLOCKS all user stories**
- **User Story 1 (Phase 3)**: Depends on Foundational completion — first E2E path
- **User Story 2 (Phase 4)**: Depends on US1 (needs HandleTeamStreamAsync to exist for attribution extension)
- **User Story 3 (Phase 5)**: Depends on Setup only (frontend-only, needs Team type in types) — can run in parallel with US1/US2
- **User Story 4 (Phase 6)**: Depends on US1 (needs HandleTeamStreamAsync for TEAM_PROGRESS/LEDGER events)
- **User Story 5 (Phase 7)**: Depends on US2 (needs participant attribution to persist)
- **Polish (Phase 8)**: Depends on all user stories

### User Story Dependencies

```
Phase 1 (Setup)
    ↓
Phase 2 (Foundational — 6-mode orchestration engine)
    ↓
Phase 3 (US1 — Select Team + Chat)  ←──── Phase 5 (US3 — Visual Diff) [parallel]
    ↓
Phase 4 (US2 — Participant Attribution)  ←──── Phase 6 (US4 — Progress + Ledger) [parallel after US1]
    ↓
Phase 7 (US5 — History Persistence)
    ↓
Phase 8 (Polish)
```

### Within Each User Story

- Tests MUST be written and FAIL before implementation (Red-Green-Refactor)
- Backend before frontend (SSE events must exist for frontend to consume)
- Core implementation before integration
- Story complete before moving to next priority

### Parallel Opportunities

- T002 + T003: TeamChatEventDto and ITeamOrchestrator (different files)
- T005–T011: All orchestration mode tests (same file but independent test methods)
- T006–T010: Concurrent through MagneticOne mode tests (parallel within test class)
- T020 + T021: Team resolver tests (success + error cases)
- T033 + T034: Visual differentiation tasks (different components)
- T035 + T036: Backend progress + ledger emission (different files)
- US3 (Phase 5) can run entirely in parallel with US1/US2

---

## Parallel Example: Foundational Phase

```
# Launch all mode tests in parallel (T005–T011):
T005: Sequential mode test
T006: Concurrent mode test      [P]
T007: RoundRobin mode test      [P]
T008: Handoffs mode test        [P]
T009: Selector mode test        [P]
T010: MagneticOne mode test     [P]
T011: Error cases test          [P]

# Then implement modes (some parallel, some sequential):
T012: Sequential + Concurrent (foundational)
T013: RoundRobin               (depends on T012 for base service)
T014: Handoffs                 (depends on T012 for base service)
T015 + T017: LlmSelector + MagneticOne managers [P] (different files)
T016: Selector mode in service (depends on T015)
T018: MagneticOne mode in service (depends on T017)
T019: Validation logic         (after all modes exist)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (types + interfaces)
2. Complete Phase 2: Foundational (6-mode orchestration engine with tests)
3. Complete Phase 3: User Story 1 (select Team agent, send message, get response)
4. **STOP and VALIDATE**: Select a Team agent, send a message, verify response streams back
5. This is the minimum viable Team chat

### Incremental Delivery

1. Setup + Foundational → Orchestration engine ready
2. Add US1 → Team agent selectable, basic chat works → **MVP!**
3. Add US2 → Messages attributed to participant agents → Core experience complete
4. Add US3 → Visual differentiation → Better UX
5. Add US4 → Progress indicator + MagneticOne ledger → Full observability
6. Add US5 → History persistence → Production-ready
7. Polish → Error handling, edge cases → Hardened

### Task Count Summary

| Phase | Tasks | Parallelizable |
|---|---|---|
| Setup | 4 | 2 |
| Foundational | 15 | 7 |
| US1 (P1) | 7 | 1 |
| US2 (P1) | 6 | 0 |
| US3 (P2) | 2 | 2 |
| US4 (P2) | 8 | 1 |
| US5 (P3) | 3 | 0 |
| Polish | 5 | 3 |
| **Total** | **50** | **16** |

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [USn] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Tests MUST fail before implementing (Red-Green-Refactor per Constitution)
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- No existing tests will be modified (Constitution Principle IV)
