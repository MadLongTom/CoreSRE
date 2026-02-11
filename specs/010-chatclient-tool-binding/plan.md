# Implementation Plan: ChatClient 工具绑定与对话调用

**Branch**: `010-chatclient-tool-binding` | **Date**: 2026-02-11 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/010-chatclient-tool-binding/spec.md`

## Summary

Enable ChatClient agents to bind registered tools (`ToolRegistration` / `McpToolItem`) via a visual picker in the agent configuration UI, convert those bindings into `AIFunction` definitions at resolution time, wrap the `IChatClient` pipeline with `FunctionInvocationChatClient` for automatic function-calling loops, emit AG-UI `TOOL_CALL_*` SSE events during streaming, and render tool call cards in the React frontend chat flow.

## Technical Context

**Language/Version**: .NET 10.0 (backend), TypeScript 5.9 / React 19.2 (frontend)
**Primary Dependencies**:
- Backend: `Microsoft.Extensions.AI` (via `Microsoft.Agents.AI.OpenAI`), `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`, EF Core 10.0, MediatR 12.4, AutoMapper 13.0, `ModelContextProtocol` 0.8.0-preview.1, FluentValidation 11.11
- Frontend: `@ag-ui/client` 0.0.44, `@ag-ui/core` 0.0.44, React 19.2, react-hook-form 7.71, zod 4.3, shadcn/Radix UI, RxJS 7.8, Tailwind CSS 4.1
**Storage**: PostgreSQL (Aspire-hosted, Npgsql), EF Core Code-First, JSONB columns for value objects
**Testing**: xUnit 2.9.3, Moq 4.20, FluentAssertions 8.3; projects: `CoreSRE.Application.Tests`, `CoreSRE.Infrastructure.Tests`
**Target Platform**: Linux server (Aspire container) + browser SPA
**Project Type**: Web (backend API + frontend SPA)
**Performance Goals**: Tool call card renders in <500ms after SSE event; tool picker handles 50+ tools without UI jank
**Constraints**: Tool execution timeout 30s; max 10 function-calling loop iterations; SSE streaming backpressure
**Scale/Scope**: ~50 registered tools, ~5 concurrent agent conversations, ~20 tools per agent binding max

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Spec-Driven Development | ✅ PASS | spec.md written and committed before plan/code |
| II | TDD (NON-NEGOTIABLE) | ✅ PASS | Plan will define tests first in tasks; Red-Green-Refactor enforced |
| III | Domain-Driven Design | ✅ PASS | No new domain entities needed; `LlmConfigVO.ToolRefs` already exists. New service (`IToolFunctionFactory`) interface in Application, implementation in Infrastructure. No business logic in API layer. |
| IV | Test Immutability | ✅ PASS | No existing tests modified; only new tests added |
| V | Interface-Before-Implementation | ✅ PASS | `IToolFunctionFactory` interface defined before concrete impl. Existing `IToolInvoker` / `IAgentResolver` interfaces unchanged. |
| — | 5-Step Workflow | ✅ PASS | Plan enforces Spec → Test → Interface → Implement → Verify per task |
| — | Layer Dependencies | ✅ PASS | Domain (no deps) ← Application (interfaces) ← Infrastructure (impls) ← API (endpoints). No reversals. |
| — | Naming Conventions | ✅ PASS | Backend: `IToolFunctionFactory`, `ToolFunctionFactory`, `ToolCallCard.tsx`, `useAgentChat` |

**Gate Result**: ✅ ALL CLEAR — proceed to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/010-chatclient-tool-binding/
├── spec.md              # Feature specification (committed)
├── plan.md              # This file (/speckit.plan output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (AG-UI event schemas, API additions)
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
Backend/
├── CoreSRE.Domain/
│   └── ValueObjects/
│       └── LlmConfigVO.cs                    # (existing — ToolRefs already defined)
├── CoreSRE.Application/
│   └── Interfaces/
│       └── IToolFunctionFactory.cs            # NEW — converts tool refs to AIFunction[]
├── CoreSRE.Infrastructure/
│   └── Services/
│       ├── AgentResolverService.cs            # MODIFY — wire ToolRefs → AIFunction pipeline
│       └── ToolFunctionFactory.cs             # NEW — IToolFunctionFactory implementation
├── CoreSRE/
│   └── Endpoints/
│       └── AgentChatEndpoints.cs              # MODIFY — emit TOOL_CALL_* SSE events
├── CoreSRE.Application.Tests/                 # NEW tests for IToolFunctionFactory behavior
└── CoreSRE.Infrastructure.Tests/              # NEW tests for ToolFunctionFactory + AgentResolver

Frontend/
├── src/
│   ├── components/
│   │   ├── agents/
│   │   │   ├── LlmConfigSection.tsx           # MODIFY — replace GUID input with ToolRefsPicker
│   │   │   └── ToolRefsPicker.tsx             # NEW — searchable multi-select tool picker
│   │   └── chat/
│   │       ├── MessageBubble.tsx               # MODIFY — delegate tool call rendering
│   │       └── ToolCallCard.tsx               # NEW — collapsible tool call visualization
│   ├── hooks/
│   │   └── use-agent-chat.ts                  # MODIFY — parse TOOL_CALL_* events
│   └── types/
│       └── chat.ts                            # MODIFY — add ToolCall fields to ChatMessage
```

**Structure Decision**: Web application (backend + frontend), following existing DDD layer layout. No new projects. New interfaces in Application layer, implementations in Infrastructure layer, UI components follow existing shadcn/Radix patterns.

## Constitution Re-Check (Post-Design)

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Spec-Driven Development | ✅ PASS | spec.md + plan.md + research.md + data-model.md + contracts/ complete |
| II | TDD | ✅ PASS | Test cases defined in quickstart.md; tasks will enforce Red-Green-Refactor |
| III | DDD | ✅ PASS | No new domain entities. `IToolFunctionFactory` in Application (interfaces). `ToolFunctionFactory`, `ToolRegistrationAIFunction`, `McpToolAIFunction` in Infrastructure. No business logic in API layer. Repository extensions follow existing `IRepository<T>` pattern. |
| IV | Test Immutability | ✅ PASS | Only new tests added. No existing tests modified. |
| V | Interface-Before-Implementation | ✅ PASS | `IToolFunctionFactory` defined as interface. `GetByIdsAsync` added to existing repository interfaces. All new services consumed via DI interfaces. |
| — | Layer Dependencies | ✅ PASS | Domain (no deps) ← Application (`IToolFunctionFactory`) ← Infrastructure (`ToolFunctionFactory`, AIFunction subclasses) ← API (endpoints). No reversals. |
| — | Naming Conventions | ✅ PASS | `IToolFunctionFactory` / `ToolFunctionFactory` / `ToolRegistrationAIFunction` / `McpToolAIFunction` / `ToolCallCard.tsx` / `ToolRefsPicker.tsx` |

**Post-Design Gate Result**: ✅ ALL CLEAR — ready for Phase 2 (tasks).
