# Implementation Plan: Agent Memory & History Management

**Branch**: `014-agent-memory-history` | **Date**: 2026-02-11 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/014-agent-memory-history/spec.md`

## Summary

Replace the current stateless AG-UI chat pattern (frontend sends full message history, backend persists via raw SQL) with the Agent Framework's `ChatHistoryProvider` + `PostgresAgentSessionStore` pipeline. Add `IChatReducer`-based token window management, extend `LlmConfigVO` with per-agent memory/history configuration, and implement cross-session semantic memory via `ChatHistoryMemoryProvider` + pgvector.

## Technical Context

**Language/Version**: C# / .NET 10.0 (Backend), TypeScript (Frontend — Vite + React)
**Primary Dependencies**: Microsoft.Agents.AI.Hosting 1.0.0-preview.260209.1, Microsoft.Extensions.AI 10.2.0, Entity Framework Core 10.0.2, Npgsql 10.0.0
**Storage**: PostgreSQL (via Aspire + Npgsql EF Core), pgvector extension (new for semantic memory)
**Testing**: xUnit + Moq (Backend), Vite/Vitest (Frontend)
**Target Platform**: Linux/Windows server (Aspire orchestrated), Web browser (SPA)
**Project Type**: Web application (Backend .NET + Frontend React SPA)
**Performance Goals**: <2s conversation restore for 200 messages; no token-limit errors for 100+ exchanges
**Constraints**: Backward-compatible (stateless mode preserved); best-effort persistence (don't block chat on DB errors)
**Scale/Scope**: Single-tenant dev/lab platform; ~10 concurrent users; <1000 conversations

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Pre-Research | Post-Design | Notes |
|-----------|-------------|-------------|-------|
| **I. Spec-Driven Development** | ✅ PASS | ✅ PASS | Spec 014 written before any code; plan derived from spec |
| **II. TDD (NON-NEGOTIABLE)** | ✅ PASS | ✅ PASS | Quickstart details test-first approach; Red-Green-Refactor enforced |
| **III. Domain-Driven Design** | ✅ PASS | ✅ PASS | `LlmConfigVO` (Domain), `AgentResolverService` (Infrastructure), endpoints (API). No layer violations. Vector collection managed by SDK — not a domain entity. |
| **IV. Test Immutability** | ✅ PASS | ✅ PASS | No existing tests modified; only new tests added |
| **V. Interface-Before-Implementation** | ✅ PASS | ✅ PASS | `IAgentResolver` already defined; framework types (`ChatHistoryProvider`, `IChatReducer`, `AIContextProvider`) serve as interfaces. No new project interfaces needed. |
| **DDD Layer Compliance** | ✅ PASS | ✅ PASS | Value object in Domain (zero external deps), services in Infrastructure, DI in Infrastructure, endpoints in API. Dependencies flow inward only. |
| **Five-Step Workflow** | ✅ PASS | ✅ PASS | Spec → Test → Interface → Implement → Verify |

## Project Structure

### Documentation (this feature)

```text
specs/014-agent-memory-history/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── llm-config-api.yaml
└── tasks.md             # Phase 2 output (NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
Backend/
├── CoreSRE.Domain/
│   └── ValueObjects/
│       └── LlmConfigVO.cs              # MODIFY — add memory/history fields
├── CoreSRE.Application/
│   └── Interfaces/
│       └── IAgentResolver.cs           # NO CHANGE — existing interface sufficient
├── CoreSRE.Infrastructure/
│   ├── Services/
│   │   └── AgentResolverService.cs     # MODIFY — wire ChatHistoryProvider + IChatReducer + AIContextProvider
│   ├── Persistence/
│   │   ├── Sessions/
│   │   │   └── PostgresAgentSessionStore.cs  # NO CHANGE — already implemented
│   │   ├── AppDbContext.cs             # MODIFY — add AgentMemoryEmbedding DbSet (Phase 3)
│   │   └── Configurations/            # ADD — AgentMemoryEmbeddingConfiguration (Phase 3)
│   ├── Memory/
│   │   └── PostgresVectorStoreProvider.cs  # ADD (Phase 3) — Microsoft.Extensions.VectorData adapter
│   ├── Migrations/                     # ADD — new migration for pgvector + vector table
│   └── DependencyInjection.cs          # MODIFY — wire session store + vector store
├── CoreSRE/
│   ├── Endpoints/
│   │   └── AgentChatEndpoints.cs       # MODIFY — replace manual SQL with agent.RunStreamingAsync + session lifecycle
│   └── Program.cs                      # MODIFY — register session store in pipeline
├── CoreSRE.Application.Tests/
│   └── (no changes — application layer has no new logic)
└── CoreSRE.Infrastructure.Tests/
    └── Services/
        └── AgentResolverServiceTests.cs  # MODIFY — add tests for history/reducer/memory configuration

Frontend/
└── src/
    ├── types/
    │   └── agent.ts                    # MODIFY — add memory fields to LlmConfig interface
    └── components/
        └── agents/
            └── LlmConfigSection.tsx    # MODIFY — add "History & Memory" collapsible section
```

**Structure Decision**: Existing web application structure (Backend .NET + Frontend React). No new projects added. Changes are modifications to existing files + a few new infrastructure files for Phase 3 (vector store).

## Complexity Tracking

> No constitution violations requiring justification. All changes follow existing patterns.
