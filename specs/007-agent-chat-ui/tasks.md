# Tasks: 单 Agent 对话界面

**Input**: Design documents from `/specs/007-agent-chat-ui/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/chat-api.yaml, quickstart.md

**Tests**: Not explicitly requested in the feature specification — test tasks are NOT included.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story. Chat history is stored in existing `AgentSessionRecord.SessionData` (SPEC-004) — no custom `ChatMessage` entity.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Install dependencies and configure project for AG-UI protocol + chat feature

- [X] T001 Install backend NuGet packages: `Microsoft.Agents.AI.OpenAI`, `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`, `System.Net.ServerSentEvents` in Backend/CoreSRE/CoreSRE.csproj
- [X] T002 Install frontend npm packages: `@ag-ui/client`, `@ag-ui/core`, `rxjs` in Frontend/package.json
- [X] T003 Register `AddAGUI()` service in Backend/CoreSRE/Program.cs (AG-UI middleware)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain entity, repository interface, EF configuration, migration, and IAgentResolver interface — MUST be complete before ANY user story

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T004 Create `Conversation` aggregate root entity with `Create(Guid agentId)` factory method, `SetTitle(string)`, `Touch()` domain methods in Backend/CoreSRE.Domain/Entities/Conversation.cs
- [X] T005 [P] Create `IConversationRepository` interface extending `IRepository<Conversation>` with `GetAllOrderedByUpdatedAtAsync()` in Backend/CoreSRE.Domain/Interfaces/IConversationRepository.cs
- [X] T006 [P] Create `IAgentResolver` interface with `ResolveAsync(Guid agentRegistrationId)` returning `AIAgent` in Backend/CoreSRE.Application/Interfaces/IAgentResolver.cs
- [X] T007 Create `ConversationConfiguration` EF Fluent API mapping (snake_case columns, FK to `agent_registrations`, indexes on `agent_id` and `updated_at DESC`) in Backend/CoreSRE.Infrastructure/Persistence/Configurations/ConversationConfiguration.cs
- [X] T008 Register `DbSet<Conversation>` in `AppDbContext` and add EF migration for `conversations` table in Backend/CoreSRE.Infrastructure/Persistence/AppDbContext.cs
- [X] T009 Implement `ConversationRepository` extending `Repository<Conversation>` with `GetAllOrderedByUpdatedAtAsync()` in Backend/CoreSRE.Infrastructure/Repositories/ConversationRepository.cs
- [X] T010 Register `IConversationRepository` → `ConversationRepository` in DI in Backend/CoreSRE.Infrastructure/DependencyInjection.cs
- [X] T011 Create chat DTOs: `ConversationDto`, `ConversationSummaryDto`, `ChatMessageDto` (projected from SessionData JSONB) in Backend/CoreSRE.Application/Chat/Dtos/
- [X] T012 [P] Create AutoMapper profile mapping `Conversation` → `ConversationDto` / `ConversationSummaryDto` in Backend/CoreSRE.Application/Chat/Mappings/ChatMappingProfile.cs
- [X] T013 [P] Create chat TypeScript interfaces: `Conversation`, `ConversationSummary`, `ChatMessage`, `CreateConversationRequest` in Frontend/src/types/chat.ts

**Checkpoint**: Foundation ready — domain model, persistence, DTOs, and interfaces all in place

---

## Phase 3: User Story 1 — 发起新对话 (Priority: P1) 🎯 MVP

**Goal**: User selects an Agent, sends first message, receives streamed AG-UI response. Agent selector locks after first message.

**Independent Test**: Select an Agent from dropdown → type message → send → see streaming response appear → verify Agent selector is locked.

### Backend Implementation for User Story 1

- [X] T014 [US1] Implement `CreateConversationCommand` + `CreateConversationCommandHandler` (validates agentId exists, calls `Conversation.Create()`, saves) in Backend/CoreSRE.Application/Chat/Commands/CreateConversation/
- [X] T015 [US1] Implement `CreateConversationCommandValidator` (FluentValidation: agentId required, non-empty) in Backend/CoreSRE.Application/Chat/Commands/CreateConversation/CreateConversationCommandValidator.cs
- [X] T016 [US1] Implement `TouchConversationCommand` + `TouchConversationCommandHandler` (updates `UpdatedAt`, sets `Title` from first message if not set) in Backend/CoreSRE.Application/Chat/Commands/TouchConversation/
- [X] T017 [US1] Implement `AgentResolverService` — resolves `AgentRegistration` by ID, loads `LlmProvider`, constructs `OpenAIClient` → `.GetChatClient(model).AsIChatClient().CreateAIAgent(name, description)`, configures `ChatHistoryProvider` backed by `PostgresAgentSessionStore` in Backend/CoreSRE.Infrastructure/Services/AgentResolverService.cs
- [X] T018 [US1] Register `IAgentResolver` → `AgentResolverService` in DI in Backend/CoreSRE.Infrastructure/DependencyInjection.cs
- [X] T019 [US1] Create `ChatEndpoints.cs` with `MapChatEndpoints()` — POST `/api/chat/conversations` (create), routes through MediatR in Backend/CoreSRE/Endpoints/ChatEndpoints.cs
- [X] T020 [US1] Create `AgentChatEndpoints.cs` with `MapAgentChatEndpoints()` — calls `MapAGUI("/api/chat/stream", ...)` to register AG-UI SSE streaming endpoint, resolves agent via `IAgentResolver` using `RunAgentInput.context` in Backend/CoreSRE/Endpoints/AgentChatEndpoints.cs
- [X] T021 [US1] Register `MapChatEndpoints()` and `MapAgentChatEndpoints()` in Backend/CoreSRE/Program.cs

### Frontend Implementation for User Story 1

- [X] T022 [P] [US1] Create chat REST API client: `createConversation()`, `touchConversation()` in Frontend/src/lib/api/chat.ts
- [X] T023 [US1] Create `useAgentChat` hook wrapping `@ag-ui/client` `HttpAgent` + subscriber pattern — manages messages state, streaming flag, error, `sendMessage()`, `abortRun()` in Frontend/src/hooks/use-agent-chat.ts
- [X] T024 [P] [US1] Create `AgentSelector` component — dropdown of registered agents, lockable via `disabled` prop after first message in Frontend/src/components/chat/AgentSelector.tsx
- [X] T025 [P] [US1] Create `MessageBubble` component — displays single message with role-based styling (user right-aligned, assistant left-aligned) in Frontend/src/components/chat/MessageBubble.tsx
- [X] T026 [US1] Create `MessageArea` component — scrollable message list, auto-scroll on new content, loading indicator during streaming in Frontend/src/components/chat/MessageArea.tsx
- [X] T027 [US1] Create `MessageInput` component — text input + send button, disabled when no agent selected or during streaming, Enter to send in Frontend/src/components/chat/MessageInput.tsx
- [X] T028 [US1] Create `ChatPage` — assembles AgentSelector + MessageArea + MessageInput, manages conversation creation on first send, calls `touchConversation` on run finish in Frontend/src/pages/ChatPage.tsx
- [X] T029 [US1] Add `/chat` route to router and "对话" sidebar nav entry in Frontend/src/App.tsx and Frontend/src/components/layout/Sidebar.tsx

**Checkpoint**: User can select Agent → send message → see streaming AG-UI response → Agent selector locks. MVP complete.

---

## Phase 4: User Story 2 — 继续历史对话 (Priority: P1)

**Goal**: User sees conversation list sorted by recent activity, clicks a conversation to load full history (extracted from `AgentSessionRecord.SessionData`), and continues chatting.

**Independent Test**: With existing conversations, click one from the list → verify all historical messages display → send new message → verify it appends correctly.

### Backend Implementation for User Story 2

- [X] T030 [US2] Implement `GetConversationsQuery` + `GetConversationsQueryHandler` — loads all conversations ordered by `UpdatedAt DESC`, joins `AgentRegistration` for agent name/type, extracts last message preview from `AgentSessionRecord.SessionData` JSONB in Backend/CoreSRE.Application/Chat/Queries/GetConversations/
- [X] T031 [US2] Implement `GetConversationByIdQuery` + `GetConversationByIdQueryHandler` — loads `Conversation`, resolves `AgentRegistration.Name`, loads `AgentSessionRecord` by `(agentName, conversationId)`, deserializes `SessionData.ChatHistoryProviderState.Messages[]` into `ChatMessageDto[]` in Backend/CoreSRE.Application/Chat/Queries/GetConversationById/
- [X] T032 [US2] Add GET `/api/chat/conversations` (list) and GET `/api/chat/conversations/{id}` (detail with messages) routes to `ChatEndpoints.cs` in Backend/CoreSRE/Endpoints/ChatEndpoints.cs

### Frontend Implementation for User Story 2

- [X] T033 [P] [US2] Add `getConversations()` and `getConversationById()` API functions in Frontend/src/lib/api/chat.ts
- [X] T034 [US2] Create `ConversationList` component — sidebar list of conversations showing agent name, title/last message preview, timestamp, sorted by recent activity, click to select in Frontend/src/components/chat/ConversationList.tsx
- [X] T035 [US2] Integrate `ConversationList` into `ChatPage` — load conversations on mount, clicking a conversation loads its history via `getConversationById()`, populates `useAgentChat` with historical messages, locks Agent selector in Frontend/src/pages/ChatPage.tsx

**Checkpoint**: User can browse conversation list → click to load history → continue chatting in existing conversation.

---

## Phase 5: User Story 3 — 创建全新对话 (Priority: P2)

**Goal**: User clicks "新建对话" button to reset the chat page — clears messages, unlocks Agent selector, ready to start fresh.

**Independent Test**: While in an active conversation, click "新建对话" → verify messages cleared, Agent selector unlocked, can select new Agent.

### Frontend Implementation for User Story 3

- [X] T036 [US3] Add "新建对话" button to `ChatPage` header — resets `useAgentChat` state (clear messages, reset conversationId), unlocks Agent selector, refreshes conversation list in Frontend/src/pages/ChatPage.tsx
- [X] T037 [US3] Update `ConversationList` to highlight "新建对话" state (no conversation selected) vs active conversation selected in Frontend/src/components/chat/ConversationList.tsx

**Checkpoint**: User can switch between creating new conversations and resuming existing ones.

---

## Phase 6: User Story 4 — 对话消息的流式展示 (Priority: P2)

**Goal**: Agent responses stream token-by-token via AG-UI `TEXT_MESSAGE_CONTENT` events with visual loading indicator.

**Independent Test**: Send a message → observe response appearing character-by-character → see loading indicator during generation → indicator disappears when complete.

### Frontend Implementation for User Story 4

- [X] T038 [US4] Enhance `useAgentChat` hook — implement `onTextMessageContentEvent` subscriber to progressively update in-flight assistant message with streaming deltas, manage `isStreaming` state via `onRunStarted` / `onRunFinished` in Frontend/src/hooks/use-agent-chat.ts
- [X] T039 [US4] Enhance `MessageArea` — show typing/loading indicator (animated dots or spinner) when `isStreaming` is true, auto-scroll to bottom on each content delta in Frontend/src/components/chat/MessageArea.tsx
- [X] T040 [US4] Enhance `MessageInput` — disable send button and show visual feedback while `isStreaming` is true in Frontend/src/components/chat/MessageInput.tsx

**Checkpoint**: Streaming UI fully functional — tokens appear progressively, loading indicator visible during generation.

---

## Phase 7: User Story 5 — 删除对话记录 (Priority: P3)

**Goal**: User can delete a conversation with confirmation dialog. Deletes both `Conversation` record and associated `AgentSessionRecord`.

**Independent Test**: Click delete on a conversation → see confirmation dialog → confirm → conversation disappears from list → if it was active, page resets to new conversation state.

### Backend Implementation for User Story 5

- [X] T041 [US5] Implement `DeleteConversationCommand` + `DeleteConversationCommandHandler` — deletes `Conversation` entity AND associated `AgentSessionRecord` (by composite key `agentName + conversationId`) in Backend/CoreSRE.Application/Chat/Commands/DeleteConversation/
- [X] T042 [US5] Add DELETE `/api/chat/conversations/{id}` route to `ChatEndpoints.cs` in Backend/CoreSRE/Endpoints/ChatEndpoints.cs

### Frontend Implementation for User Story 5

- [X] T043 [P] [US5] Add `deleteConversation(id)` API function in Frontend/src/lib/api/chat.ts
- [X] T044 [P] [US5] Create `DeleteConversationDialog` component — shadcn/ui AlertDialog with "确认删除此对话？" prompt, confirm/cancel buttons in Frontend/src/components/chat/DeleteConversationDialog.tsx
- [X] T045 [US5] Add delete button to each conversation item in `ConversationList`, wire up `DeleteConversationDialog`, on confirm delete + refresh list, if deleted conversation was active reset to new conversation state in Frontend/src/components/chat/ConversationList.tsx and Frontend/src/pages/ChatPage.tsx

**Checkpoint**: Full conversation lifecycle — create, chat, resume, delete — all working.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Edge cases, error handling, and UX improvements across all user stories

- [X] T046 Handle Agent offline/unavailable error — show error toast when AG-UI stream returns `RUN_ERROR` event in Frontend/src/hooks/use-agent-chat.ts
- [X] T047 [P] Handle empty Agent list — show "请先注册 Agent" placeholder in `AgentSelector` when no agents available in Frontend/src/components/chat/AgentSelector.tsx
- [X] T048 [P] Handle empty message validation — keep send button disabled for empty/whitespace-only input in Frontend/src/components/chat/MessageInput.tsx
- [X] T049 Add `HttpAgent.abortRun()` cancel button visible during streaming in Frontend/src/components/chat/MessageInput.tsx
- [X] T050 Run quickstart.md validation — verify all curl commands work end-to-end per specs/007-agent-chat-ui/quickstart.md

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Phase 2 — MVP, should be completed first
- **US2 (Phase 4)**: Depends on Phase 2 + Phase 3 (needs conversation creation to have data to list)
- **US3 (Phase 5)**: Depends on Phase 3 (needs ChatPage to exist to add reset button)
- **US4 (Phase 6)**: Depends on Phase 3 (enhances existing streaming hook + components)
- **US5 (Phase 7)**: Depends on Phase 2 + Phase 4 (needs list + detail views to add delete)
- **Polish (Phase 8)**: Depends on all user story phases

### User Story Dependencies

- **US1 (P1)**: Foundation only — no other story dependencies → 🎯 MVP
- **US2 (P1)**: Depends on US1 (conversations must exist to list them)
- **US3 (P2)**: Depends on US1 (ChatPage must exist)
- **US4 (P2)**: Depends on US1 (streaming hook must exist to enhance)
- **US5 (P3)**: Depends on US2 (conversation list must exist to add delete)

### Within Each User Story

- Backend before frontend (endpoints must exist for frontend to call)
- Domain/interfaces before implementations
- Services before endpoints
- API client before components that use it
- Core components before page integration

### Parallel Opportunities

- T005 + T006: Repository interface and resolver interface (different files)
- T011 + T012 + T013: DTOs, AutoMapper, TypeScript types (all independent)
- T022 + T024 + T025: API client, AgentSelector, MessageBubble (independent files)
- T033 + T043 + T044: API functions + delete dialog (independent files)
- T046 + T047 + T048: Polish tasks (different components)

---

## Parallel Example: User Story 1

```bash
# After Phase 2 foundation is complete:

# Backend — sequential (command → service → endpoints):
T014 → T015 → T016 → T017 → T018 → T019 → T020 → T021

# Frontend — parallel models then sequential integration:
T022 (API client)  ─┐
T024 (AgentSelector) ─┤── can run in parallel
T025 (MessageBubble) ─┘
          │
T023 (useAgentChat hook) ← depends on API client
T026 (MessageArea) ← depends on MessageBubble
T027 (MessageInput)
T028 (ChatPage) ← depends on all components + hook
T029 (Route + Sidebar)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001–T003)
2. Complete Phase 2: Foundational (T004–T013)
3. Complete Phase 3: User Story 1 (T014–T029)
4. **STOP and VALIDATE**: Select Agent → send message → see streaming response → Agent locks
5. Deploy/demo if ready

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. Add US1 (发起新对话) → Test independently → **MVP!**
3. Add US2 (继续历史对话) → Test independently → Core chat complete
4. Add US3 (创建全新对话) + US4 (流式展示) → Can run in parallel → Full UX
5. Add US5 (删除对话) → Conversation lifecycle complete
6. Polish → Production ready

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks in same phase
- [Story] label maps task to specific user story for traceability
- No `ChatMessage` entity — messages are read from `AgentSessionRecord.SessionData` JSONB
- `Conversation` is a thin metadata entity (title, agentId, timestamps only)
- AG-UI protocol handles all SSE formatting — no manual SSE code needed
- `ChatHistoryProvider` + `PostgresAgentSessionStore` auto-persist chat history — no manual message saving
- Frontend only calls REST to touch `Conversation.UpdatedAt` + `Title` after each AG-UI run
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
