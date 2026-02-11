# Implementation Plan: Workflow 前端管理页面

**Branch**: `013-workflow-frontend` | **Date**: 2026-02-11 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/013-workflow-frontend/spec.md`

## Summary

Build frontend pages for Workflow CRUD (spec-011) and Workflow Execution Engine (spec-012). Includes a visual DAG graph editor/viewer using React Flow (`@xyflow/react`), workflow list/create/detail/edit pages, execution trigger dialog, execution history, and execution detail page with per-node status visualization. Follows existing frontend conventions (React 19 + Vite 7 + TypeScript + shadcn/ui + Tailwind CSS v4).

## Technical Context

**Language/Version**: TypeScript 5.9 / React 19.2 / Vite 7.3  
**Primary Dependencies**: @xyflow/react (React Flow v12), @dagrejs/dagre, shadcn/ui, react-router 7.13, lucide-react, zod 4.3, react-hook-form 7.71  
**Storage**: N/A (frontend only, all persistence via backend API)  
**Testing**: No frontend test runner configured (consistent with existing frontend; no Vitest/Jest setup)  
**Target Platform**: Desktop browsers (Chrome, Firefox, Edge) at ≥1280px viewport  
**Project Type**: Web frontend (React SPA)  
**Performance Goals**: List page renders in <2s, DAG editor interaction at 60fps  
**Constraints**: No new UI framework, no Monaco Editor, bundle size increase <500KB for @xyflow/react  
**Scale/Scope**: <100 workflows, <20 nodes per workflow, <100 executions per workflow

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Spec-Driven Development | ✅ PASS | spec.md exists with 8 user stories, 35 FRs, 38 acceptance scenarios |
| II. TDD (NON-NEGOTIABLE) | ⚠️ JUSTIFIED DEVIATION | Constitution defines TDD for backend C# test projects. No frontend test infrastructure exists (0 test files across all existing frontend pages). This is a **frontend-only** feature. Frontend behavior validated via spec acceptance scenarios (manual testing). Adding Vitest would expand scope beyond this spec. |
| III. DDD | ✅ N/A | DDD layers apply to backend (.NET). Frontend is outside DDD architecture. No backend code changes in this spec. |
| IV. Test Immutability | ✅ N/A | No existing frontend tests to modify |
| V. Interface-Before-Implementation | ✅ PASS (adapted) | TypeScript types defined in `data-model.md` before implementation. API client interface documented in `contracts/api-contracts.md`. Frontend uses type definitions rather than C# interfaces. |

**Post-Phase-1 Re-Check**: All gates re-evaluated — same results. No backend domain/application/infrastructure changes.

## Project Structure

### Documentation (this feature)

```text
specs/013-workflow-frontend/
├── plan.md              # This file
├── research.md          # Phase 0 output — library decisions, architecture choices
├── data-model.md        # Phase 1 output — TypeScript type definitions
├── quickstart.md        # Phase 1 output — setup guide
├── contracts/
│   └── api-contracts.md # Phase 1 output — API endpoint contracts
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
Frontend/
├── src/
│   ├── types/
│   │   └── workflow.ts                     # TypeScript types (NEW)
│   ├── lib/
│   │   ├── api/
│   │   │   └── workflows.ts               # API client functions (NEW)
│   │   └── dag-utils.ts                    # React Flow ↔ backend model converters (NEW)
│   ├── pages/
│   │   ├── WorkflowListPage.tsx            # (NEW)
│   │   ├── WorkflowCreatePage.tsx          # (NEW)
│   │   ├── WorkflowDetailPage.tsx          # (NEW)
│   │   └── WorkflowExecutionDetailPage.tsx # (NEW)
│   ├── components/
│   │   └── workflows/
│   │       ├── WorkflowStatusBadge.tsx     # (NEW)
│   │       ├── ExecutionStatusBadge.tsx     # (NEW)
│   │       ├── DeleteWorkflowDialog.tsx    # (NEW)
│   │       ├── ExecuteWorkflowDialog.tsx   # (NEW)
│   │       ├── ExecutionHistoryTable.tsx   # (NEW)
│   │       ├── NodeExecutionTimeline.tsx   # (NEW)
│   │       ├── DagEditor.tsx              # (NEW)
│   │       ├── DagViewer.tsx              # (NEW)
│   │       ├── DagExecutionViewer.tsx     # (NEW)
│   │       ├── NodePanel.tsx              # (NEW)
│   │       ├── NodePropertyPanel.tsx      # (NEW)
│   │       └── custom-nodes/
│   │           ├── AgentNode.tsx           # (NEW)
│   │           ├── ToolNode.tsx            # (NEW)
│   │           ├── ConditionNode.tsx       # (NEW)
│   │           ├── FanOutNode.tsx          # (NEW)
│   │           └── FanInNode.tsx           # (NEW)
│   └── App.tsx                             # (MODIFIED — add routes)
│   └── components/layout/Sidebar.tsx       # (MODIFIED — add nav item)
└── package.json                            # (MODIFIED — add @xyflow/react, @dagrejs/dagre)
```

**Structure Decision**: Follows the existing frontend per-resource pattern (`pages/{Resource}Page.tsx`, `components/{resource}/...`, `types/{resource}.ts`, `lib/api/{resource}.ts`). New `lib/dag-utils.ts` for React Flow conversion utilities. Custom React Flow nodes in `components/workflows/custom-nodes/`.

## Complexity Tracking

| Deviation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| No frontend TDD | No test runner exists in frontend; all 13 existing pages have 0 tests. Adding Vitest/testing-library would expand scope. | Adding test infra is a separate cross-cutting concern spec |
| New dependency @xyflow/react (~400KB) | DAG editor requires interactive graph editing (drag/drop nodes, connect edges, zoom/pan). No simpler way to provide this. | Manual SVG/Canvas would take 10x development time |
| New dependency @dagrejs/dagre (~30KB) | Auto-layout for nodes loaded without position data | Fixed grid layout doesn't represent topology |
| Position data in Config field | Backend NodeVO has no position fields; adding them requires a separate backend spec + migration | localStorage loses data across devices |
