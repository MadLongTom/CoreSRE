# Implementation Plan: 工作流定义 CRUD

**Branch**: `011-workflow-crud` | **Date**: 2026-02-11 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/011-workflow-crud/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

实现工作流定义（WorkflowDefinition）的完整 CRUD 管理。工作流以 DAG（有向无环图）JSON 格式描述节点（Agent/Tool/Condition/FanOut/FanIn）和边（Normal/Conditional）的编排关系。核心挑战在于 DAG 有效性校验（环检测、孤立节点、引用校验）和与现有领域模型（AgentRegistration.WorkflowRef）的关联约束。此 Spec 仅覆盖定义层 CRUD，不涉及工作流执行（SPEC-021）或发布为 Agent（SPEC-026）。

## Technical Context

**Language/Version**: C# / .NET 10.0  
**Primary Dependencies**: MediatR (CQRS), FluentValidation, EF Core 10.0, Microsoft.Agents.AI.Workflows 1.0.0-preview (runtime mapping, SPEC-021 scope)  
**Storage**: PostgreSQL (via Npgsql.EntityFrameworkCore.PostgreSQL), JSONB for value objects  
**Testing**: xUnit 2.9.3, Moq 4.20, FluentAssertions 8.3  
**Target Platform**: Linux server (container via .NET Aspire)  
**Project Type**: Web application (backend API + React SPA frontend)  
**Performance Goals**: DAG validation <2s for 50 nodes; list query <1s for 100 workflows  
**Constraints**: DAG must be acyclic (enforced at write time); Published workflows immutable  
**Scale/Scope**: ~100 workflow definitions, DAGs up to 100 nodes  

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Principle I: Spec-Driven Development (SDD)
- **Status**: ✅ PASS
- Specification exists at `specs/011-workflow-crud/spec.md` with 4 user stories, 17 FRs, 8 edge cases.

### Principle II: Test-Driven Development (TDD)
- **Status**: ✅ PASS (planning — tests will be written first per workflow Step 2)
- Test structure: `CoreSRE.Domain.Tests` (entity/VO invariants, DAG validation), `CoreSRE.Application.Tests` (command/query handlers), `CoreSRE.Infrastructure.Tests` (repository CRUD).
- Target coverage: Domain 95%, Application 90%, Infrastructure 80%.

### Principle III: Domain-Driven Design (DDD)
- **Status**: ✅ PASS
- Layers: Domain (WorkflowDefinition entity, VOs, enums, IWorkflowDefinitionRepository) → Application (Commands/Queries/Handlers/DTOs/Validators) → Infrastructure (EF Core repository, DbContext, Migration) → API (endpoints, DI).
- WorkflowDefinition is aggregate root. WorkflowGraphVO/NodeVO/EdgeVO are value objects (JSONB). No cross-aggregate direct references.

### Principle IV: Test Immutability
- **Status**: ✅ PASS (no existing tests to modify)

### Principle V: Interface-Before-Implementation
- **Status**: ✅ PASS
- `IWorkflowDefinitionRepository` defined in Domain/Interfaces/ before implementation in Infrastructure.

### Development Workflow Compliance
- Step 1 (Spec): ✅ Done
- Steps 2-5: Will be enforced in tasks.md task ordering (test → interface → implement → verify)

## Project Structure

### Documentation (this feature)

```text
specs/011-workflow-crud/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── api-contract.md
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
Backend/
├── CoreSRE.Domain/
│   ├── Entities/
│   │   └── WorkflowDefinition.cs          # NEW: Aggregate root
│   ├── ValueObjects/
│   │   ├── WorkflowGraphVO.cs             # NEW: DAG graph (nodes + edges)
│   │   ├── WorkflowNodeVO.cs              # NEW: Graph node
│   │   └── WorkflowEdgeVO.cs              # NEW: Graph edge
│   ├── Enums/
│   │   ├── WorkflowNodeType.cs            # NEW: Agent/Tool/Condition/FanOut/FanIn
│   │   ├── WorkflowEdgeType.cs            # NEW: Normal/Conditional
│   │   └── WorkflowStatus.cs              # NEW: Draft/Published
│   └── Interfaces/
│       └── IWorkflowDefinitionRepository.cs  # NEW
│
├── CoreSRE.Application/
│   └── Workflows/
│       ├── Commands/
│       │   ├── CreateWorkflow/
│       │   │   ├── CreateWorkflowCommand.cs
│       │   │   ├── CreateWorkflowCommandHandler.cs
│       │   │   └── CreateWorkflowCommandValidator.cs
│       │   ├── UpdateWorkflow/
│       │   │   ├── UpdateWorkflowCommand.cs
│       │   │   ├── UpdateWorkflowCommandHandler.cs
│       │   │   └── UpdateWorkflowCommandValidator.cs
│       │   └── DeleteWorkflow/
│       │       ├── DeleteWorkflowCommand.cs
│       │       ├── DeleteWorkflowCommandHandler.cs
│       │       └── DeleteWorkflowCommandValidator.cs
│       ├── Queries/
│       │   ├── GetWorkflows/
│       │   │   ├── GetWorkflowsQuery.cs
│       │   │   └── GetWorkflowsQueryHandler.cs
│       │   └── GetWorkflowById/
│       │       ├── GetWorkflowByIdQuery.cs
│       │       └── GetWorkflowByIdQueryHandler.cs
│       └── DTOs/
│           ├── WorkflowDefinitionDto.cs
│           ├── WorkflowSummaryDto.cs
│           ├── WorkflowGraphDto.cs
│           ├── WorkflowNodeDto.cs
│           └── WorkflowEdgeDto.cs
│
├── CoreSRE.Infrastructure/
│   ├── Persistence/
│   │   └── AppDbContext.cs                # MODIFY: Add DbSet<WorkflowDefinition>
│   ├── Repositories/                      # or existing folder
│   │   └── WorkflowDefinitionRepository.cs  # NEW
│   └── Migrations/                        # NEW: EF Core migration
│
├── CoreSRE/                               # API layer
│   └── Endpoints/                         # or Routes/
│       └── WorkflowEndpoints.cs           # NEW: MapGroup("/api/workflows")
│
├── CoreSRE.Domain.Tests/                  # NEW test files (not yet existing project — may need create or reuse existing)
├── CoreSRE.Application.Tests/
│   └── Workflows/                         # NEW
└── CoreSRE.Infrastructure.Tests/
    └── Workflows/                         # NEW
```

**Structure Decision**: Web application (Option 2). Follows the existing Clean Architecture layout with Domain/Application/Infrastructure/API layers already established. New files added within existing project folders following established naming conventions.

## Complexity Tracking

> No constitution violations detected. All patterns align with existing codebase conventions.
