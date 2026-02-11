# Quickstart: 013-workflow-frontend

**Date**: 2026-02-11

## Prerequisites

- Node.js 20+ installed
- Backend running on `http://localhost:5156` (Workflow CRUD + Execution APIs from spec-011/012)
- Frontend dev server: `cd Frontend && npm run dev` (port 5173)

## Setup Steps

### 1. Install new dependencies

```bash
cd Frontend
npm install @xyflow/react @dagrejs/dagre
```

### 2. Add TypeScript types

Create `Frontend/src/types/workflow.ts` with all workflow types (see data-model.md).

### 3. Add API client

Create `Frontend/src/lib/api/workflows.ts` following the pattern in `agents.ts`:
- 8 async functions mapping to 8 backend endpoints
- Reuse `fetchWithTimeout()`, `handleResponse<T>()`, `ApiError` from existing code

### 4. Add routes

In `Frontend/src/App.tsx`, add 4 routes under `<AppLayout />`:
```tsx
{ path: "workflows", element: <WorkflowListPage /> },
{ path: "workflows/new", element: <WorkflowCreatePage /> },
{ path: "workflows/:id", element: <WorkflowDetailPage /> },
{ path: "workflows/:id/executions/:execId", element: <WorkflowExecutionDetailPage /> },
```

### 5. Add sidebar nav item

In `Frontend/src/components/layout/Sidebar.tsx`, add:
```tsx
{ to: "/workflows", label: "工作流管理", icon: GitBranch }
```

### 6. Create pages

Create 4 page components in `Frontend/src/pages/`:
- `WorkflowListPage.tsx` — table + status filter + empty state
- `WorkflowCreatePage.tsx` — name/description form + DAG editor
- `WorkflowDetailPage.tsx` — detail view + DAG viewer + edit mode + execution history + execute dialog
- `WorkflowExecutionDetailPage.tsx` — execution overview + DAG execution viz + node timeline

### 7. Create workflow components

Create components in `Frontend/src/components/workflows/`:
- Status badges, delete dialog, execute dialog
- DAG editor (interactive), DAG viewer (read-only), DAG execution viewer (with status colors)
- Custom React Flow nodes (5 types)
- Node panel, property panel, execution timeline

## Verification

1. Navigate to `http://localhost:5173/workflows` — should show empty list
2. Click "New Workflow" → create page with DAG editor
3. Add nodes, connect edges, save → redirects to detail page
4. View DAG visualization in read-only mode
5. Edit (if Draft), Publish, Execute (if Published)
6. View execution history, click to see execution detail with colored DAG

## Key Files Created

```
Frontend/src/
├── types/workflow.ts                          # All TypeScript types
├── lib/api/workflows.ts                       # API client (8 functions)
├── pages/
│   ├── WorkflowListPage.tsx
│   ├── WorkflowCreatePage.tsx
│   ├── WorkflowDetailPage.tsx
│   └── WorkflowExecutionDetailPage.tsx
├── components/workflows/
│   ├── WorkflowStatusBadge.tsx
│   ├── ExecutionStatusBadge.tsx
│   ├── DeleteWorkflowDialog.tsx
│   ├── ExecuteWorkflowDialog.tsx
│   ├── ExecutionHistoryTable.tsx
│   ├── NodeExecutionTimeline.tsx
│   ├── DagEditor.tsx
│   ├── DagViewer.tsx
│   ├── DagExecutionViewer.tsx
│   ├── NodePanel.tsx
│   ├── NodePropertyPanel.tsx
│   └── custom-nodes/
│       ├── AgentNode.tsx
│       ├── ToolNode.tsx
│       ├── ConditionNode.tsx
│       ├── FanOutNode.tsx
│       └── FanInNode.tsx
└── lib/
    └── dag-utils.ts                           # React Flow ↔ WorkflowGraph converters

Files Modified:
├── App.tsx                                    # +4 routes, +4 imports
└── components/layout/Sidebar.tsx              # +1 nav item
```

## New Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `@xyflow/react` | latest | DAG editor/viewer (React Flow v12) |
| `@dagrejs/dagre` | latest | Auto-layout for DAG nodes without saved positions |
