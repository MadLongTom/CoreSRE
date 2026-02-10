# Tasks: AgentSession PostgreSQL 持久化

**Input**: Design documents from `/specs/004-agent-session-persistence/`  
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅, quickstart.md ✅

**Tests**: Not requested in feature specification. Test tasks are excluded.

**Organization**: Tasks grouped by user story. US1 and US2 are both P1 and tightly coupled (save/restore loop), so they share a single phase. US3 is a separate P2 phase.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Exact file paths included in descriptions

---

## Phase 1: Setup

**Purpose**: Add Agent Framework NuGet dependency and ensure solution compiles

- [X] T001 Add `Microsoft.Agents.AI.Hosting` NuGet package to Backend/CoreSRE.Infrastructure/CoreSRE.Infrastructure.csproj
- [X] T002 Register `IDbContextFactory<AppDbContext>` in Backend/CoreSRE.Infrastructure/DependencyInjection.cs (alongside existing AddDbContext)

**Checkpoint**: Solution compiles with new dependency, IDbContextFactory available in DI

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain entity and EF Core configuration that ALL user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T003 [P] Create `AgentSessionRecord` entity in Backend/CoreSRE.Domain/Entities/AgentSessionRecord.cs — Properties: AgentId (string, 255), ConversationId (string, 255), SessionData (JsonElement), SessionType (string, 100), CreatedAt (DateTime), UpdatedAt (DateTime). Static factory method `Create()` and instance method `Update()`. No BaseEntity inheritance (composite string PK).
- [X] T004 [P] Create `AgentSessionRecordConfiguration` EF entity config in Backend/CoreSRE.Infrastructure/Persistence/Configurations/AgentSessionRecordConfiguration.cs — Table `agent_sessions`, composite PK (agent_id, conversation_id), snake_case columns, SessionData as `jsonb`, index on agent_id per data-model.md
- [X] T005 Add `DbSet<AgentSessionRecord>` to Backend/CoreSRE.Infrastructure/Persistence/AppDbContext.cs
- [X] T006 Create EF Core migration for `agent_sessions` table via `dotnet ef migrations add AddAgentSessions`

**Checkpoint**: Migration exists, `agent_sessions` table schema matches data-model.md

---

## Phase 3: User Story 1 + 2 — 会话持久化存储 & 会话恢复 (Priority: P1) 🎯 MVP

**Goal**: Implement `PostgresAgentSessionStore` with both `SaveSessionAsync` (US1: UPSERT to PostgreSQL) and `GetSessionAsync` (US2: read + deserialize or create new session). These two stories form an inseparable save/restore pair.

**Independent Test**: Call SaveSessionAsync to persist a session, then call GetSessionAsync to retrieve it. Verify the deserialized session matches the original. Also verify that GetSessionAsync returns a new session (via CreateSessionAsync) when no record exists.

### Implementation

- [X] T007 [US1] [US2] Create `PostgresAgentSessionStore` class in Backend/CoreSRE.Infrastructure/Persistence/Sessions/PostgresAgentSessionStore.cs — Inherit `AgentSessionStore`, constructor takes `IDbContextFactory<AppDbContext>`. Implement `SaveSessionAsync`: serialize via `agent.SerializeSession(session)`, extract `GetRawText()`, execute UPSERT SQL via `context.Database.ExecuteSqlAsync` with `FormattableString` (INSERT...ON CONFLICT DO UPDATE per R4). Implement `GetSessionAsync`: query `agent_sessions` by `(agent.Id, conversationId)`, if found call `agent.DeserializeSessionAsync(sessionData)`, if not found call `agent.CreateSessionAsync()` per R1 contract.

**Checkpoint**: PostgresAgentSessionStore fully implements AgentSessionStore contract. Solution compiles.

---

## Phase 4: User Story 3 — DI 注册与零配置集成 (Priority: P2)

**Goal**: Register `PostgresAgentSessionStore` into DI via `WithSessionStore()` factory, using `IDbContextFactory<AppDbContext>` from the container

**Independent Test**: DI container resolves `AgentSessionStore` keyed service and returns a `PostgresAgentSessionStore` instance with valid database connectivity

### Implementation

- [X] T008 [US3] Add DI registration helper for PostgresAgentSessionStore in Backend/CoreSRE.Infrastructure/DependencyInjection.cs — Create extension method (e.g., `AddPostgresSessionStore` on `IServiceCollection`) or document the `WithSessionStore((sp, name) => new PostgresAgentSessionStore(sp.GetRequiredService<IDbContextFactory<AppDbContext>>()))` pattern for use in Program.cs when Agent hosting is configured

**Checkpoint**: PostgresAgentSessionStore is accessible via DI. Existing Agent Registry functionality unaffected.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Build validation and verification

- [X] T009 Run `dotnet build Backend/CoreSRE/CoreSRE.slnx` and verify 0 errors
- [X] T010 Run quickstart.md validation steps (build, migration list, DI registration check)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 — T003, T004 can run in parallel; T005 depends on T003; T006 depends on T004, T005
- **Phase 3 (US1+US2)**: Depends on Phase 2 complete (entity + config + DbSet + migration)
- **Phase 4 (US3)**: Depends on Phase 3 (PostgresAgentSessionStore must exist)
- **Phase 5 (Polish)**: Depends on Phase 4

### User Story Dependencies

- **US1 + US2 (P1)**: Merged into single phase — save and restore are inseparable
- **US3 (P2)**: Depends on US1+US2 (the class must exist before DI registration)

### Within Each Phase

```
Phase 2 parallel opportunities:
  T003 (entity) ──────────┐
                           ├──→ T005 (DbSet) ──→ T006 (migration)
  T004 (EF config) ───────┘

Phase 3:
  T007 (PostgresAgentSessionStore) — single task, sequential

Phase 4:
  T008 (DI registration) — single task, depends on T007
```

---

## Implementation Strategy

### MVP First (US1 + US2)

1. Complete Phase 1: Setup (NuGet + IDbContextFactory)
2. Complete Phase 2: Foundational (entity, config, DbSet, migration)
3. Complete Phase 3: PostgresAgentSessionStore implementation
4. **STOP and VALIDATE**: Build succeeds, migration exists
5. This is the MVP — session save/restore works

### Incremental Delivery

1. Setup + Foundational → Database schema ready
2. US1+US2 → Core save/restore works → MVP!
3. US3 → DI integration ready → Production-ready
4. Polish → Verified against quickstart.md

---

## Notes

- Total tasks: 10
- Tasks per story: US1+US2 = 1 core task (T007), US3 = 1 task (T008), shared = 8 tasks
- Parallel opportunities: T003 ∥ T004 in Phase 2
- No external API endpoints — all integration is internal (Agent Framework calls)
- No test tasks generated (tests not requested in spec)
- `AgentSessionRecord` entity exists for EF Migration generation; `PostgresAgentSessionStore` uses raw SQL for UPSERT and EF Core query for SELECT
