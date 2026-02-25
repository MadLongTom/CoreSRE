# Implementation Plan: Team Mode Agent Chat

**Branch**: `021-team-agent-chat` | **Date**: 2026-02-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/021-team-agent-chat/spec.md`

## Summary

Enable Team-type agents to be selected and used in the Chat UI, with full multi-agent orchestration across all 6 modes (Sequential, Concurrent, RoundRobin, Handoffs, Selector, MagneticOne). The backend leverages the already-referenced `Microsoft.Agents.AI.Workflows` package to build orchestration pipelines. The frontend extends the existing AG-UI SSE protocol with participant attribution metadata, adds Team agent support to the agent selector, and introduces mode-specific UI components (MagneticOne ledger side panel, handoff notifications, concurrent bubble streaming).

## Technical Context

**Language/Version**: C# / .NET 10 (backend), TypeScript ~5.9 (frontend)
**Primary Dependencies**: `Microsoft.Agents.AI.Workflows 1.0.0-preview.260209.1` (already referenced, unused), `Microsoft.Extensions.AI.Abstractions 10.2.0`, React 19, AG-UI Client `@ag-ui/client ^0.0.44`, shadcn/radix
**Storage**: PostgreSQL via Npgsql EF Core 10 + JSONB for session data, pgvector for semantic memory
**Testing**: xUnit 2.9.3, Moq 4.20.72, FluentAssertions 8.3.0
**Target Platform**: Web (Aspire AppHost + React SPA)
**Project Type**: Web (backend + frontend)
**Performance Goals**: Team conversations with up to 5 participants complete without UI errors or lost messages. No custom timeout — rely on `maxIterations` + LLM provider timeout.
**Constraints**: No team nesting (participant agents must be ChatClient or A2A type). All 6 modes mandatory from day one.
**Scale/Scope**: Extends existing Agent CRUD + Chat UI. ~15 FRs across backend orchestration engine + frontend UI changes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Spec-Driven Development | ✅ PASS | Spec exists at `specs/021-team-agent-chat/spec.md` with 15 FRs, 5 user stories, clarifications completed |
| II. TDD (Non-Negotiable) | ✅ PLAN | Tests will be written before implementation per Red-Green-Refactor. Domain tests for team resolution, Application tests for orchestration handlers, Infrastructure tests for session persistence. |
| III. DDD Layer Compliance | ✅ PLAN | Domain: entities/VOs (already exist). Application: orchestration interfaces + handlers. Infrastructure: `AgentResolverService` Team case, workflow builders. API: endpoint extension only. No layer violations planned. |
| IV. Test Immutability | ✅ PLAN | No existing tests will be modified. All new test cases added. |
| V. Interface-Before-Implementation | ✅ PLAN | `ITeamOrchestrator` interface defined in Application layer before Infrastructure implementation. |

**Pre-Phase-0 Gate: PASS** — No violations. Proceeding to research.

### Post-Phase-1 Re-evaluation

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Spec-Driven Development | ✅ PASS | Spec complete (15 FRs). All design artifacts (research.md, data-model.md, contracts/, quickstart.md) derived from spec. |
| II. TDD (Non-Negotiable) | ✅ PLAN | Test plan: `TeamOrchestratorTests.cs` (6 mode tests + error cases), frontend integration tests for SSE event parsing. Tests written before implementation per constitution. |
| III. DDD Layer Compliance | ✅ PASS | Domain: existing entities/VOs (no changes). Application: `ITeamOrchestrator` interface + `TeamChatEventDto` DTOs. Infrastructure: `TeamOrchestratorService` + `AgentResolverService` extension. API: `HandleTeamStreamAsync()` routing only. No layer violations. |
| IV. Test Immutability | ✅ PASS | No existing tests modified. All new test cases added to `TeamOrchestratorTests.cs`. |
| V. Interface-Before-Implementation | ✅ PASS | `ITeamOrchestrator` defined in Application/Interfaces before `TeamOrchestratorService` in Infrastructure. Custom GroupChatManagers implement framework abstract class. |

**Post-Phase-1 Gate: PASS** — No violations found. Design is constitution-compliant.

## Project Structure

### Documentation (this feature)

```text
specs/021-team-agent-chat/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── sse-events.md    # Extended AG-UI SSE protocol
│   └── team-chat-api.md # API contract changes
└── tasks.md             # Phase 2 output (via /speckit.tasks)
```

### Source Code (repository root)

```text
Backend/
├── CoreSRE.Application/
│   ├── Interfaces/
│   │   └── ITeamOrchestrator.cs          # NEW: Team orchestration interface
│   ├── Agents/
│   │   └── DTOs/                         # Existing (TeamConfigDto already exists)
│   └── Chat/
│       └── DTOs/
│           └── TeamChatEventDto.cs       # NEW: SSE event DTOs for team attribution
├── CoreSRE.Infrastructure/
│   ├── Services/
│   │   ├── AgentResolverService.cs       # MODIFIED: Add Team case
│   │   └── TeamOrchestratorService.cs    # NEW: Team orchestration engine (6 modes)
│   └── Migrations/                       # MODIFIED if schema changes needed
├── CoreSRE/
│   └── Endpoints/
│       └── AgentChatEndpoints.cs         # MODIFIED: Add HandleTeamStreamAsync path
└── CoreSRE.Application.Tests/
    └── Agents/
        └── TeamOrchestratorTests.cs      # NEW: Tests for all 6 orchestration modes

Frontend/
└── src/
    ├── components/
    │   └── chat/
    │       ├── AgentSelector.tsx          # MODIFIED: Include Team agents
    │       ├── MessageBubble.tsx          # MODIFIED: Show participant agent name
    │       ├── HandoffNotification.tsx    # NEW: "🔀 Agent A → Agent B" system msg
    │       ├── TeamProgressIndicator.tsx  # NEW: Active agent indicator
    │       └── MagneticOneLedger.tsx      # NEW: Collapsible side panel
    ├── hooks/
    │   └── use-agent-chat.ts             # MODIFIED: Handle team SSE events
    ├── pages/
    │   └── ChatPage.tsx                  # MODIFIED: Layout for ledger panel
    └── types/
        └── chat.ts                       # MODIFIED: Add participant attribution fields
```

**Structure Decision**: Extends existing web application structure. Backend changes span Application (interfaces, DTOs), Infrastructure (new TeamOrchestratorService, AgentResolver extension), and API (endpoint routing). Frontend changes span components (new + modified), hooks, pages, and types.

## Complexity Tracking

No Constitution Check violations found — this section is intentionally empty.
