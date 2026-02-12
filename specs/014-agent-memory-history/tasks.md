# Tasks: Agent Memory & History Management

**Input**: Design documents from `/specs/014-agent-memory-history/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅, quickstart.md ✅

**Tests**: Included per Constitution Principle II (TDD — NON-NEGOTIABLE). Tests follow Red-Green-Refactor: write first, ensure they fail, then implement.

**Organization**: Tasks are grouped by user story (US1–US4) to enable independent implementation and testing.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4)
- Exact file paths included in all descriptions

## Path Conventions

- **Backend**: `Backend/CoreSRE.Domain/`, `Backend/CoreSRE.Application/`, `Backend/CoreSRE.Infrastructure/`, `Backend/CoreSRE/`
- **Backend Tests**: `Backend/CoreSRE.Infrastructure.Tests/`, `Backend/CoreSRE.Application.Tests/`
- **Frontend**: `Frontend/src/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Domain model extension and DI wiring that all user stories depend on

- [X] T001 Add memory/history fields to `LlmConfigVO` value object in `Backend/CoreSRE.Domain/ValueObjects/LlmConfigVO.cs` — add `EnableChatHistory` (bool?), `MaxHistoryMessages` (int?), `EnableSemanticMemory` (bool?), `MemorySearchMode` (string?), `MemoryMaxResults` (int?)
- [X] T002 Register `PostgresAgentSessionStore` as `AgentSessionStore` singleton in DI in `Backend/CoreSRE.Infrastructure/DependencyInjection.cs` — wire the existing `CreatePostgresSessionStore` factory method

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core wiring that MUST be complete before any user story implementation

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T003 Add `AgentSessionStore` to `IAgentResolver.ResolveAsync` or expose it alongside the resolved agent — ensure `AgentChatEndpoints` can access both the `AIAgent` and the `AgentSessionStore` for session lifecycle. Evaluate whether to inject `AgentSessionStore` directly into the endpoint or return it from the resolver. Update `Backend/CoreSRE.Application/Interfaces/IAgentResolver.cs` and `Backend/CoreSRE.Infrastructure/Services/AgentResolverService.cs` as needed
- [X] T004 Add `LlmConfig` to the resolved agent metadata so the chat endpoint can read `EnableChatHistory` at runtime — extend resolver return type or add a method to retrieve agent config in `Backend/CoreSRE.Infrastructure/Services/AgentResolverService.cs`

**Checkpoint**: Foundation ready — `LlmConfigVO` extended, session store registered, resolver provides config access

---

## Phase 3: User Story 1 — Framework-Managed Chat History (Priority: P1) 🎯 MVP

**Goal**: Replace manual SQL persistence with framework `ChatHistoryProvider` + `PostgresAgentSessionStore`. Users can resume conversations across browser refreshes.

**Independent Test**: Start a conversation, close browser, reopen — full history restored, Agent has context.

### Tests for User Story 1 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation (Red phase)**

- [X] T005 [P] [US1] Test `ResolveChatClientAgent_EnableChatHistoryTrue_ConfiguresChatHistoryProviderFactory` — verify that when `LlmConfig.EnableChatHistory` is `true` (or `null`, defaulting to true), the resolved `ChatClientAgentOptions` has a non-null `ChatHistoryProviderFactory` delegate. In `Backend/CoreSRE.Infrastructure.Tests/Services/AgentResolverServiceTests.cs`
- [X] T006 [P] [US1] Test `ResolveChatClientAgent_EnableChatHistoryFalse_NoChatHistoryProviderFactory` — verify that when `LlmConfig.EnableChatHistory` is explicitly `false`, the `ChatHistoryProviderFactory` is null (stateless mode). In `Backend/CoreSRE.Infrastructure.Tests/Services/AgentResolverServiceTests.cs`
- [X] T007 [P] [US1] Test `ChatHistoryProviderFactory_WithSerializedState_RestoresHistory` — invoke the factory delegate with a valid `SerializedState` `JsonElement` and verify it returns an `InMemoryChatHistoryProvider` containing the restored messages. In `Backend/CoreSRE.Infrastructure.Tests/Services/AgentResolverServiceTests.cs`
- [X] T008 [P] [US1] Test `ChatHistoryProviderFactory_WithoutSerializedState_CreatesEmptyProvider` — invoke the factory delegate with `default(JsonElement)` and verify it returns an empty `InMemoryChatHistoryProvider`. In `Backend/CoreSRE.Infrastructure.Tests/Services/AgentResolverServiceTests.cs`

### Implementation for User Story 1

- [X] T009 [US1] Configure `ChatHistoryProviderFactory` on `ChatClientAgentOptions` in `Backend/CoreSRE.Infrastructure/Services/AgentResolverService.cs` — read `EnableChatHistory` and `MaxHistoryMessages` from `LlmConfig`, create `InMemoryChatHistoryProvider` with optional `MessageCountingChatReducer`, handle `SerializedState` restoration
- [X] T010 [US1] Refactor `HandleChatClientStreamAsync` in `Backend/CoreSRE/Endpoints/AgentChatEndpoints.cs` — wrap resolved `AIAgent` in `AIHostAgent(agent, sessionStore)`, call `GetOrCreateSessionAsync(conversationId)` to load stored history, extract only new user message from frontend payload when `EnableChatHistory=true`, stream via `agent.RunStreamingAsync(newMessage, session)`, call `SaveSessionAsync` after streaming completes
- [X] T011 [US1] Delete `PersistChatHistoryAsync` method and all raw SQL from `Backend/CoreSRE/Endpoints/AgentChatEndpoints.cs` — remove the manual JSON construction and `INSERT ... ON CONFLICT DO UPDATE` SQL statement
- [X] T012 [US1] Add backward-compatible stateless fallback in `Backend/CoreSRE/Endpoints/AgentChatEndpoints.cs` — when `EnableChatHistory=false`, use all frontend-provided messages directly without session store (current behavior preserved)
- [X] T013 [US1] Add best-effort error handling around session load/save in `Backend/CoreSRE/Endpoints/AgentChatEndpoints.cs` — wrap `GetOrCreateSessionAsync` and `SaveSessionAsync` in try-catch, log errors, continue serving the chat response even if persistence fails
- [X] T014 [US1] Adapt SSE event emission to use `AgentResponseUpdate` stream from `RunStreamingAsync` instead of raw `IChatClient` streaming in `Backend/CoreSRE/Endpoints/AgentChatEndpoints.cs` — map `AgentResponseUpdate` to AG-UI SSE events (RUN_STARTED, TEXT_MESSAGE_START/CONTENT/END, RUN_FINISHED)

**Checkpoint**: User Story 1 complete — conversations persist via framework, no raw SQL, browser refresh restores history

---

## Phase 4: User Story 2 — Token Window Management (Priority: P2)

**Goal**: Add configurable message-count truncation via `IChatReducer` so long conversations never exceed the LLM context window.

**Independent Test**: Send 100+ messages, verify no token-limit errors, recent messages always in context, all messages visible in UI.

### Tests for User Story 2 ⚠️

- [X] T015 [P] [US2] Test `ResolveChatClientAgent_MaxHistoryMessages20_ConfiguresReducerWith20` — verify that when `LlmConfig.MaxHistoryMessages = 20`, the `ChatHistoryProviderFactory` creates an `InMemoryChatHistoryProvider` whose `ChatReducer` is a `MessageCountingChatReducer` with max=20. In `Backend/CoreSRE.Infrastructure.Tests/Services/AgentResolverServiceTests.cs`
- [X] T016 [P] [US2] Test `ResolveChatClientAgent_MaxHistoryMessagesNull_ConfiguresDefaultReducer` — verify that when `MaxHistoryMessages` is null, the reducer uses the platform default (50). In `Backend/CoreSRE.Infrastructure.Tests/Services/AgentResolverServiceTests.cs`
- [X] T017 [P] [US2] Test `ResolveChatClientAgent_MaxHistoryMessagesZeroOrNegative_TreatedAsDefault` — verify that 0 or negative values are treated as null (platform default applies). In `Backend/CoreSRE.Infrastructure.Tests/Services/AgentResolverServiceTests.cs`

### Implementation for User Story 2

- [X] T018 [US2] Update `ChatHistoryProviderFactory` in `Backend/CoreSRE.Infrastructure/Services/AgentResolverService.cs` to always configure a `MessageCountingChatReducer` — use `MaxHistoryMessages` if positive, otherwise platform default (50). Validate that `ChatReducerTriggerEvent.BeforeMessagesRetrieval` is used so full history is stored but only a window is sent to LLM
- [X] T019 [US2] Add `ChatHistory:DefaultMaxMessages` setting to `Backend/CoreSRE/appsettings.json` with default value 50 — inject `IConfiguration` into resolver to read the platform default

**Checkpoint**: User Story 2 complete — long conversations auto-truncated, all messages stored, only window sent to LLM

---

## Phase 5: User Story 3 — Per-Agent Memory Configuration UI (Priority: P2)

**Goal**: Expose history and memory configuration controls in the Agent detail page so administrators can tune context strategy per Agent.

**Independent Test**: Open Agent detail, toggle Enable Chat History, set Max History Messages to 30, save, verify Agent respects settings.

### Implementation for User Story 3

- [X] T020 [P] [US3] Add memory/history fields to `LlmConfig` TypeScript interface in `Frontend/src/types/agent.ts` — add `enableChatHistory?: boolean | null`, `maxHistoryMessages?: number | null`, `enableSemanticMemory?: boolean | null`, `memorySearchMode?: string | null`, `memoryMaxResults?: number | null`
- [X] T021 [US3] Add "History & Memory" collapsible section to `Frontend/src/components/agents/LlmConfigSection.tsx` — add a new `<Collapsible>` block with: Switch for `enableChatHistory` (default true), NumberInput for `maxHistoryMessages` (shown when history enabled), Switch for `enableSemanticMemory` (default false), Select for `memorySearchMode` with options "BeforeAIInvoke" and "OnDemandFunctionCalling" (shown when memory enabled), NumberInput for `memoryMaxResults` (shown when memory enabled). Follow existing pattern of the "Advanced" collapsible section
- [X] T022 [US3] Ensure the `UpdateAgentCommand` handler passes new `LlmConfigVO` fields through the update pipeline — verify in `Backend/CoreSRE.Application/` that the AutoMapper profile or manual mapping includes the 5 new fields when mapping from DTO to value object. Check `Backend/CoreSRE.Application/Agents/Commands/` for the update command handler

**Checkpoint**: User Story 3 complete — administrators can configure history/memory settings per Agent via UI

---

## Phase 6: User Story 4 — Cross-Session Semantic Memory (Priority: P3)

**Goal**: Enable Agents to recall information from prior conversations using vector embeddings and semantic retrieval via `ChatHistoryMemoryProvider`.

**Independent Test**: Tell Agent a fact in Conversation A, start Conversation B, ask about it — Agent recalls correctly.

### Tests for User Story 4 ⚠️

- [X] T023 [P] [US4] Test `ResolveChatClientAgent_EnableSemanticMemoryTrue_ConfiguresAIContextProviderFactory` — verify that when `LlmConfig.EnableSemanticMemory` is `true`, the resolved `ChatClientAgentOptions` has a non-null `AIContextProviderFactory` delegate. In `Backend/CoreSRE.Infrastructure.Tests/Services/AgentResolverServiceTests.cs`
- [X] T024 [P] [US4] Test `ResolveChatClientAgent_EnableSemanticMemoryFalse_NoAIContextProviderFactory` — verify that when `EnableSemanticMemory` is false (default), `AIContextProviderFactory` is null. In `Backend/CoreSRE.Infrastructure.Tests/Services/AgentResolverServiceTests.cs`
- [X] T025 [P] [US4] Test `ResolveChatClientAgent_MemorySearchModeBeforeAIInvoke_SetsCorrectSearchBehavior` — verify the factory configures `SearchBehavior.BeforeAIInvoke`. In `Backend/CoreSRE.Infrastructure.Tests/Services/AgentResolverServiceTests.cs`
- [X] T026 [P] [US4] Test `ResolveChatClientAgent_MemoryMaxResults5_SetsMaxResults5` — verify the factory configures `MaxResults = 5`. In `Backend/CoreSRE.Infrastructure.Tests/Services/AgentResolverServiceTests.cs`

### Implementation for User Story 4

- [X] T027 [US4] Add pgvector NuGet packages to `Backend/CoreSRE.Infrastructure/CoreSRE.Infrastructure.csproj` — add `Microsoft.Extensions.VectorData` and the PostgreSQL vector store adapter package (e.g., `Npgsql.EntityFrameworkCore.PostgreSQL.Pgvector` or equiv)
- [X] T028 [US4] Create EF Core migration to enable pgvector extension in `Backend/CoreSRE.Infrastructure/Migrations/` — add migration with `CREATE EXTENSION IF NOT EXISTS vector` via raw SQL in `Up` method
- [X] T029 [US4] Register `VectorStore` (pgvector adapter) and `IEmbeddingGenerator` in DI in `Backend/CoreSRE.Infrastructure/DependencyInjection.cs` — configure the pgvector `VectorStore` with connection string from existing PostgreSQL config, configure `IEmbeddingGenerator` using an OpenAI-compatible embedding client
- [X] T030 [US4] Add embedding model configuration to `Backend/CoreSRE/appsettings.json` — add `SemanticMemory:EmbeddingModel`, `SemanticMemory:EmbeddingDimensions`, `SemanticMemory:CollectionName` settings
- [X] T031 [US4] Configure `AIContextProviderFactory` on `ChatClientAgentOptions` in `Backend/CoreSRE.Infrastructure/Services/AgentResolverService.cs` — when `EnableSemanticMemory=true`, create `ChatHistoryMemoryProvider` with `VectorStore`, `collectionName`, `vectorDimensions`, scoped `ChatHistoryMemoryProviderScope` (ApplicationId="CoreSRE", AgentId=registration.Id), and `ChatHistoryMemoryProviderOptions` (SearchTime from `MemorySearchMode`, MaxResults from `MemoryMaxResults`)
- [X] T032 [US4] Add graceful degradation for vector store unavailability in `Backend/CoreSRE.Infrastructure/Services/AgentResolverService.cs` — if `VectorStore` or `IEmbeddingGenerator` is not registered or throws on resolution, log a warning and skip `AIContextProviderFactory` configuration, allowing the agent to operate without semantic memory

**Checkpoint**: User Story 4 complete — Agent recalls information across conversations via vector semantic search

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Error handling, cleanup, and validation across all stories

- [X] T033 [P] Add graceful degradation for corrupted session data in `Backend/CoreSRE/Endpoints/AgentChatEndpoints.cs` — if `DeserializeSessionAsync` fails, catch exception, log warning, create fresh session, continue chat
- [X] T034 [P] Validate `MaxHistoryMessages` in domain — if value is ≤ 0, treat as null (platform default) in `Backend/CoreSRE.Infrastructure/Services/AgentResolverService.cs`
- [X] T035 Remove dead code — delete the unused `CreatePostgresSessionStore` method body if it has been superseded by the new DI registration in `Backend/CoreSRE.Infrastructure/DependencyInjection.cs`, or keep if reused
- [X] T036 Run quickstart.md validation — execute the manual verification checklist from `specs/014-agent-memory-history/quickstart.md` to confirm all acceptance scenarios pass

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 completion — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Phase 2 — core MVP
- **US2 (Phase 4)**: Depends on Phase 3 (T009 specifically — reducer wiring builds on history provider)
- **US3 (Phase 5)**: Depends on Phase 1 (T001 — domain fields must exist); frontend work (T020, T021) is parallelizable with US1/US2 backend work
- **US4 (Phase 6)**: Depends on Phase 3 (history pipeline must work); independent of US2 and US3
- **Polish (Phase 7)**: Depends on all desired user stories being complete

### User Story Dependencies

```
Phase 1 (Setup) ──► Phase 2 (Foundation) ──► Phase 3 (US1: Chat History) ──► Phase 4 (US2: Token Window)
                                          │                                    │
                                          ├──► Phase 5 (US3: Config UI)  ◄─────┘ (T020-T021 parallelizable)
                                          │
                                          └──► Phase 6 (US4: Semantic Memory)
                                                                               └──► Phase 7 (Polish)
```

### Within Each User Story

- Tests MUST be written and FAIL before implementation (Constitution TDD)
- Domain changes before infrastructure services
- Infrastructure services before API endpoints
- Core implementation before integration/polish
- Story complete before moving to next priority

### Parallel Opportunities

**Phase 1**: T001 and T002 are sequential (T002 depends on T001 for type availability)
**Phase 3 (US1)**: T005–T008 (all tests) can run in parallel
**Phase 4 (US2)**: T015–T017 (all tests) can run in parallel
**Phase 5 (US3)**: T020 (frontend types) can run in parallel with any backend US1/US2 work
**Phase 6 (US4)**: T023–T026 (all tests) can run in parallel; T027–T028 can run in parallel with each other
**Phase 7**: T033–T035 can all run in parallel

---

## Parallel Example: User Story 1

```bash
# 1. Write all US1 tests in parallel (Red phase):
T005: ResolveChatClientAgent_EnableChatHistoryTrue_ConfiguresChatHistoryProviderFactory
T006: ResolveChatClientAgent_EnableChatHistoryFalse_NoChatHistoryProviderFactory
T007: ChatHistoryProviderFactory_WithSerializedState_RestoresHistory
T008: ChatHistoryProviderFactory_WithoutSerializedState_CreatesEmptyProvider

# 2. Implement sequentially (Green phase):
T009: Configure ChatHistoryProviderFactory in resolver
T010: Refactor chat endpoint to use AIHostAgent + session lifecycle
T011: Delete PersistChatHistoryAsync raw SQL
T012: Add backward-compatible stateless fallback
T013: Add best-effort error handling
T014: Adapt SSE emission to AgentResponseUpdate
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001–T002)
2. Complete Phase 2: Foundation (T003–T004)
3. Complete Phase 3: User Story 1 (T005–T014)
4. **STOP and VALIDATE**: Conversation persists across browser refresh, no raw SQL, Agent responds with history context
5. Deploy/demo if ready — this is the minimum viable delivery

### Incremental Delivery

1. Phase 1 + 2 → Foundation ready
2. Add US1 (Phase 3) → Test: conversation restore works → **MVP deployed**
3. Add US2 (Phase 4) → Test: 100+ message conversation doesn't fail → Deploy
4. Add US3 (Phase 5) → Test: admin can configure memory settings in UI → Deploy
5. Add US4 (Phase 6) → Test: Agent recalls info from prior conversation → Deploy
6. Each story adds value without breaking previous stories

### Summary

| Metric | Count |
|--------|-------|
| **Total tasks** | 36 |
| **Setup tasks** | 2 |
| **Foundational tasks** | 2 |
| **US1 tasks** (P1 — MVP) | 10 (4 tests + 6 impl) |
| **US2 tasks** (P2) | 5 (3 tests + 2 impl) |
| **US3 tasks** (P2) | 3 (0 tests + 3 impl) |
| **US4 tasks** (P3) | 10 (4 tests + 6 impl) |
| **Polish tasks** | 4 |
| **Parallelizable tasks** | 18 |

---

## Notes

- All tests follow Constitution naming: `{Method}_{Scenario}_{ExpectedResult}`
- No existing tests are modified (Constitution Principle IV)
- JSONB schema changes require no migration — PostgreSQL handles nullable fields transparently
- `PostgresAgentSessionStore` is already implemented and tested — only DI wiring is new
- The `PersistChatHistoryAsync` raw SQL deletion (T011) is the key debt elimination
- Frontend US3 tasks (T020–T021) can start at any time after T001 since they only need type definitions
