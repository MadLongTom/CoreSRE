# Implementation Plan: 工作流引擎基础修复 (Workflow Engine Base Fix)

**Branch**: `015-workflow-engine-fix` | **Date**: 2026-02-12 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/015-workflow-engine-fix/spec.md`

## Summary

Fix three foundational defects in the existing workflow execution engine that prevent it from being usable: (1) `NodeExecutionVO.Input` is never written during execution — add input recording to `WorkflowExecution.StartNode` and all 5 call sites in `WorkflowEngine`, (2) `WorkflowExecutionDto` omits `GraphSnapshot` — add the property and let AutoMapper use the existing `WorkflowGraphVO → WorkflowGraphDto` mapping, (3) no mock agent mode exists — introduce a `MockChatClient` implementing `IChatClient` and a configuration flag in `AgentResolverService` to short-circuit with mock responses when no LLM is configured or mock mode is requested.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)
**Primary Dependencies**: MediatR 12.4.1, AutoMapper 13.0.1, FluentValidation 11.11.0, Microsoft.Extensions.AI.Abstractions 10.2.0, Microsoft.Agents.AI.* 1.0.0-preview, EF Core 10.0.2
**Storage**: PostgreSQL via Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0
**Testing**: xUnit 2.9.3, Moq 4.20.72, FluentAssertions 8.3.0
**Target Platform**: Linux/Windows server (ASP.NET Core, Aspire host)
**Project Type**: Web application (Backend: Clean Architecture DDD, Frontend: React/Vite)
**Performance Goals**: Workflow execution under 30 seconds for 3-node mock workflow (test suite constraint)
**Constraints**: No breaking changes to existing API contracts; backward-compatible DTO additions only
**Scale/Scope**: 5 files modified, 2 new files, ~200 lines of changes

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Spec-Driven Development | ✅ PASS | Specification written at `specs/015-workflow-engine-fix/spec.md` before any code |
| II. TDD — NON-NEGOTIABLE | ✅ PASS | Plan orders tests before implementation; smoke test is a deliverable |
| III. Domain-Driven Design | ✅ PASS | Domain changes in Domain layer (`WorkflowExecution.StartNode`), DTO changes in Application layer, engine changes in Infrastructure layer. Dependencies flow inward only. |
| IV. Test Immutability — NON-NEGOTIABLE | ✅ PASS | No existing tests modified; only new tests added |
| V. Interface-Before-Implementation | ✅ PASS | Mock mode uses existing `IChatClient` interface; no new interfaces needed since `MockChatClient` implements an existing interface |
| Development Workflow (5-step) | ✅ PASS | Spec (done) → Tests → Interfaces (existing) → Implementation → Verify |
| DDD Layer Rules | ✅ PASS | `NodeExecutionVO`/`WorkflowExecution` in Domain; DTOs in Application; `WorkflowEngine`/`MockChatClient` in Infrastructure |

**Gate Result**: ALL PASS — proceed to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/015-workflow-engine-fix/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── execution-detail-response.json
└── checklists/
    └── requirements.md  # Quality checklist
```

### Source Code (repository root)

```text
Backend/
├── CoreSRE.Domain/
│   └── Entities/
│       └── WorkflowExecution.cs          # MODIFY: Add input param to StartNode
├── CoreSRE.Application/
│   └── Workflows/DTOs/
│       └── WorkflowExecutionDto.cs       # MODIFY: Add GraphSnapshot property
├── CoreSRE.Infrastructure/
│   └── Services/
│       ├── WorkflowEngine.cs             # MODIFY: Pass input to StartNode at all 5 call sites
│       ├── AgentResolverService.cs        # MODIFY: Add mock mode fallback
│       └── MockChatClient.cs             # NEW: IChatClient mock implementation
├── CoreSRE.Infrastructure.Tests/
│   └── Workflows/
│       ├── WorkflowEngineTests.cs        # EXISTING: Verify existing tests still pass
│       ├── NodeInputRecordingTests.cs    # NEW: Tests for input recording fix
│       └── MockAgentTests.cs             # NEW: Tests for mock agent mode
└── CoreSRE.Application.Tests/
    └── Workflows/
        └── WorkflowExecutionDtoTests.cs  # NEW: Tests for GraphSnapshot mapping
```

**Structure Decision**: Standard DDD layered architecture already established. All changes fit within existing project structure. No new projects needed.

## Complexity Tracking

> No constitution violations to justify. All changes fit within existing architecture.
