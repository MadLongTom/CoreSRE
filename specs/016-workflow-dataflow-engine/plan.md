# Implementation Plan: 工作流数据流模型与执行栈引擎

**Branch**: `016-workflow-dataflow-engine` | **Date**: 2026-02-13 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/016-workflow-dataflow-engine/spec.md`
**Depends On**: Feature 015 (SPEC-080 基础修复)

## Summary

Replace the workflow engine's single-string linear data pipeline (`string? lastOutput`) and pre-computed topological sort execution with a structured items-based data model and stack-based execution engine. New domain value objects (`WorkflowItemVO`, `PortDataVO`, `NodeInputData`, `NodeOutputData`, `ExecutionContext`) enable multi-port I/O. The execution stack + waiting queue model enables dynamic data-driven routing, replacing rigid FanOut/FanIn handling. Backward compatibility is guaranteed via default field values (InputCount=1, OutputCount=1, SourcePortIndex=0, TargetPortIndex=0).

## Technical Context

**Language/Version**: C# / .NET 10 (net10.0)
**Primary Dependencies**: Microsoft.Extensions.AI (MEAI), AutoMapper, System.Text.Json, EF Core 10
**Storage**: PostgreSQL with JSONB columns (graph_snapshot, node_executions) — no SQL migration needed; new fields use C# defaults that serialize transparently
**Testing**: xUnit 2.9.3, Moq 4.20.72, FluentAssertions 8.3.0
**Target Platform**: Linux server (containerized via .NET Aspire)
**Project Type**: Web application (Clean Architecture DDD — 4 backend layers + React frontend)
**Performance Goals**: Workflow execution with 100 nodes completes within topological-sort-equivalent time (no performance regression)
**Constraints**: All 79+ existing workflow tests must pass without modification (Test Immutability); zero SQL migration
**Scale/Scope**: Currently ~665-line WorkflowEngine.cs, will be rewritten to ~500-700 lines with new architecture; 7 new domain value objects; 2 modified VOs; updated DTOs and mapping profile

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Gate | Status |
|---|-----------|------|--------|
| I | Spec-Driven Development | Spec exists at `specs/016-workflow-dataflow-engine/spec.md` with 22 FRs, 7 SCs | ✅ PASS |
| II | TDD (NON-NEGOTIABLE) | All new code must be preceded by failing tests; Red→Green→Refactor | ✅ PLAN: tests written per-phase before implementation |
| III | DDD Layer Rules | New VOs in Domain, engine rewrite in Infrastructure, DTOs in Application, no cross-layer violations | ✅ PASS |
| IV | Test Immutability (NON-NEGOTIABLE) | 79 existing workflow tests must not be modified — engine must produce identical results for existing workflow shapes | ✅ PLAN: backward compatibility is User Story 1 (P1) |
| V | Interface-Before-Implementation | New interfaces/contracts defined before concrete types | ✅ PLAN: Phase 1 contracts precede implementation |

**Pre-Phase 0 Verdict**: All gates PASS. Proceeding to research.

## Project Structure

### Documentation (this feature)

```text
specs/016-workflow-dataflow-engine/
├── plan.md              # This file
├── research.md          # Phase 0: design decisions and rationale
├── data-model.md        # Phase 1: entity/VO definitions
├── quickstart.md        # Phase 1: developer onboarding guide
├── contracts/           # Phase 1: API contracts
│   └── workflow-api.md  # Updated endpoint schemas
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
Backend/
├── CoreSRE.Domain/
│   ├── ValueObjects/
│   │   ├── WorkflowNodeVO.cs          # MODIFY: +InputCount, +OutputCount
│   │   ├── WorkflowEdgeVO.cs          # MODIFY: +SourcePortIndex, +TargetPortIndex
│   │   ├── NodeExecutionVO.cs         # No change (Input/Output remain string? for JSONB compat)
│   │   ├── WorkflowGraphVO.cs         # MODIFY: +Validate() port index rules
│   │   ├── WorkflowItemVO.cs          # NEW: data item with JSON payload + source
│   │   ├── ItemSourceVO.cs            # NEW: lineage tracking
│   │   ├── PortDataVO.cs              # NEW: items on a single port
│   │   ├── NodeInputData.cs           # NEW: port-organized input
│   │   ├── NodeOutputData.cs          # NEW: port-organized output
│   │   ├── NodeExecutionTask.cs       # NEW: execution stack entry
│   │   ├── WaitingNodeData.cs         # NEW: waiting queue entry
│   │   └── NodeRunResult.cs           # NEW: per-run result
│   ├── Entities/
│   │   └── WorkflowExecution.cs       # MODIFY: StartNode signature may change
│   └── Interfaces/
│       └── IWorkflowEngine.cs         # No change (ExecuteAsync signature unchanged)
├── CoreSRE.Application/
│   ├── Workflows/DTOs/
│   │   ├── WorkflowNodeDto.cs         # MODIFY: +InputCount, +OutputCount
│   │   ├── WorkflowEdgeDto.cs         # MODIFY: +SourcePortIndex, +TargetPortIndex
│   │   └── WorkflowMappingProfile.cs  # MODIFY: map new fields
│   └── Interfaces/
│       └── IExpressionEvaluator.cs    # No change (ExpressionContext stays string-based for now)
├── CoreSRE.Infrastructure/
│   └── Services/
│       └── WorkflowEngine.cs          # REWRITE: execution stack model
├── CoreSRE.Infrastructure.Tests/
│   └── Workflows/
│       ├── WorkflowEngineTests.cs     # NO MODIFY (Test Immutability) — all 13 tests must pass as-is
│       ├── DataFlow/                  # NEW: structured data flow tests
│       ├── ExecutionStack/            # NEW: stack engine tests
│       └── PortRouting/               # NEW: multi-port routing tests
└── CoreSRE.Application.Tests/
    └── Workflows/
        └── (existing tests — NO MODIFY)

Frontend/  # NO CHANGES in this feature (frontend is Phase 3 / separate spec)
```

**Structure Decision**: Existing Clean Architecture structure with Domain/Application/Infrastructure layers. All new value objects go in `CoreSRE.Domain/ValueObjects/`. Engine rewrite stays in `CoreSRE.Infrastructure/Services/WorkflowEngine.cs`. New test files created alongside existing ones — existing tests are never modified.

## Complexity Tracking

No constitution violations requiring justification. The feature adds 7 new domain value objects but each is a small immutable record serving a distinct purpose in the data flow model. The engine rewrite replaces rather than extends the existing code, keeping complexity bounded.
