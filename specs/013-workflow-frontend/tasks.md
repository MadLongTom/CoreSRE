# Tasks: Workflow 前端管理页面

**Input**: Design documents from `/specs/013-workflow-frontend/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: No test tasks included — no frontend test infrastructure exists (justified deviation in plan.md).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story. US8 (Route Extension) is embedded in the Foundational phase as it enables all other stories.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Install dependencies, create shared TypeScript types and API client that ALL user stories depend on.

- [X] T001 Install @xyflow/react and @dagrejs/dagre dependencies in Frontend/package.json
- [X] T002 [P] Create all TypeScript types (enums, API models, request types, React Flow internal types) per data-model.md in Frontend/src/types/workflow.ts
- [X] T003 [P] Create API client with 8 async functions (listWorkflows, getWorkflow, createWorkflow, updateWorkflow, deleteWorkflow, executeWorkflow, listExecutions, getExecution) using fetchWithTimeout/handleResponse/ApiError pattern per contracts/api-contracts.md in Frontend/src/lib/api/workflows.ts

---

## Phase 2: Foundational / US8 — Route Extension (Blocking Prerequisites)

**Purpose**: Routing, sidebar navigation, DAG conversion utilities, and shared badge components that MUST be complete before ANY user story page can be implemented.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 [P] Create DAG conversion utilities (toReactFlowNodes, toReactFlowEdges, fromReactFlowState, autoLayout with dagre) per data-model.md and research.md R-002/R-003 in Frontend/src/lib/dag-utils.ts
- [X] T005 [P] [US8] Add 4 workflow routes (workflows, workflows/new, workflows/:id, workflows/:id/executions/:execId) under AppLayout in Frontend/src/App.tsx — FR-001
- [X] T006 [P] [US8] Add "Workflows" nav item with GitBranch icon after existing nav items in Frontend/src/components/layout/Sidebar.tsx — FR-002
- [X] T007 [P] Create WorkflowStatusBadge component (Draft=gray, Published=green) using shadcn/ui Badge in Frontend/src/components/workflows/WorkflowStatusBadge.tsx — FR-034
- [X] T008 [P] Create ExecutionStatusBadge component (Pending=gray, Running=blue, Completed=green, Failed=red, Canceled=yellow) using shadcn/ui Badge in Frontend/src/components/workflows/ExecutionStatusBadge.tsx — FR-034

**Checkpoint**: Foundation ready — all routes accessible, sidebar navigates to /workflows, shared types/API/utilities available.

---

## Phase 3: User Story 1 — Workflow 列表页面 (Priority: P1) 🎯 MVP

**Goal**: Display all workflows in a table with status filter, empty state, loading animation, error handling, and navigation to detail/create pages.

**Independent Test**: Create workflows via backend API, open /workflows, verify table renders with correct data, status filter works, empty state shows when no data, error state shows on API failure.

### Implementation for User Story 1

- [X] T009 [US1] Create WorkflowListPage with table (name, description truncated to 50 chars, WorkflowStatusBadge, createdAt), status filter (All/Draft/Published), "New Workflow" button linking to /workflows/new, "View Detail" row action linking to /workflows/:id, loading spinner, empty state with icon + "New Workflow" CTA, error state with retry button in Frontend/src/pages/WorkflowListPage.tsx — FR-003, FR-004, FR-005, FR-006

**Checkpoint**: User can browse workflows, filter by status, navigate to create or detail. List page is independently usable.

---

## Phase 4: User Story 2 — Workflow 创建页面与 DAG 图编辑器 (Priority: P1)

**Goal**: Provide a form (name + description) and interactive DAG editor for creating new workflows with drag-and-drop nodes, edge connections, and property editing.

**Independent Test**: Navigate to /workflows/new, fill name/description, drag Agent and Tool nodes onto canvas, connect them, configure properties, save — verify backend creates workflow and redirects to detail page.

### Implementation for User Story 2

- [ ] T010 [P] [US2] Create AgentNode custom React Flow node (blue bg-blue-100/border-blue-500, Bot icon, displays displayName + referenceId badge) in Frontend/src/components/workflows/custom-nodes/AgentNode.tsx — FR-009, R-008
- [ ] T011 [P] [US2] Create ToolNode custom React Flow node (orange bg-orange-100/border-orange-500, Wrench icon, displays displayName + referenceId badge) in Frontend/src/components/workflows/custom-nodes/ToolNode.tsx — FR-009, R-008
- [ ] T012 [P] [US2] Create ConditionNode custom React Flow node (purple bg-purple-100/border-purple-500, GitBranch icon, displays displayName + condition expression) in Frontend/src/components/workflows/custom-nodes/ConditionNode.tsx — FR-009, R-008
- [ ] T013 [P] [US2] Create FanOutNode custom React Flow node (teal bg-teal-100/border-teal-500, Split icon, displays displayName) in Frontend/src/components/workflows/custom-nodes/FanOutNode.tsx — FR-009, R-008
- [ ] T014 [P] [US2] Create FanInNode custom React Flow node (teal bg-teal-100/border-teal-500, Merge icon, displays displayName) in Frontend/src/components/workflows/custom-nodes/FanInNode.tsx — FR-009, R-008
- [ ] T015 [P] [US2] Create NodePanel component with draggable node type buttons (Agent, Tool, Condition, FanOut, FanIn) for drag-and-drop onto canvas in Frontend/src/components/workflows/NodePanel.tsx — FR-008
- [ ] T016 [P] [US2] Create NodePropertyPanel component for editing selected node properties (displayName, nodeType, referenceId via Agent/Tool dropdown from GET /api/agents and GET /api/tools) and selected edge properties (edgeType Normal/Conditional, condition expression input with placeholder "$.path == \"value\"") in Frontend/src/components/workflows/NodePropertyPanel.tsx — FR-009, FR-010
- [ ] T017 [US2] Create DagEditor interactive component wrapping ReactFlow with custom node types registered, NodePanel sidebar, NodePropertyPanel, drag-and-drop from NodePanel to canvas, edge creation by connecting handles, self-loop rejection, canvas zoom/pan, using toReactFlowNodes/toReactFlowEdges/fromReactFlowState from dag-utils in Frontend/src/components/workflows/DagEditor.tsx — FR-008, FR-009, FR-010, FR-035
- [ ] T018 [US2] Create WorkflowCreatePage with name input (required, max 200 chars), description textarea (optional, max 2000 chars), DagEditor component, "Save" button with frontend validation (name non-empty, ≥2 nodes, ≥1 edge), POST /api/workflows on submit, handle 400/409 error display, redirect to detail page on 201 success in Frontend/src/pages/WorkflowCreatePage.tsx — FR-007, FR-011, FR-012

**Checkpoint**: User can create workflows with visual DAG editing. Custom nodes with type-specific colors are rendered. Properties (agent/tool selection, conditions) can be configured.

---

## Phase 5: User Story 3 — Workflow 详情与编辑页面 (Priority: P1)

**Goal**: View workflow details with read-only DAG visualization, switch to edit mode (Draft only), modify and save changes, publish/unpublish workflow status.

**Independent Test**: Create a Draft workflow via API, open /workflows/:id, verify read-only DAG renders correctly, click Edit to enter edit mode, modify description and DAG, save and verify changes persist, publish and verify edit button disappears.

### Implementation for User Story 3

- [X] T019 [P] [US3] Create DagViewer read-only component wrapping ReactFlow with custom node types, nodes/edges from toReactFlowNodes/toReactFlowEdges, nodesDraggable=false, nodesConnectable=false, elementsSelectable=false, canvas zoom/pan enabled, using autoLayout for nodes without saved positions in Frontend/src/components/workflows/DagViewer.tsx — FR-014, FR-035
- [X] T020 [US3] Create WorkflowDetailPage with: (1) metadata section showing name, description, WorkflowStatusBadge, created/updated timestamps; (2) DagViewer in read-only mode; (3) "Edit" button (Draft only) toggling to edit mode with editable name/description fields and DagEditor replacing DagViewer; (4) "Save" button in edit mode calling PUT /api/workflows/{id}, reverting to read-only on success; (5) "Cancel" button discarding unsaved changes; (6) "Publish"/"Unpublish" toggle button calling PUT to update status; (7) 404 handling with "Workflow not found" message and link back to list in Frontend/src/pages/WorkflowDetailPage.tsx — FR-013, FR-014, FR-015, FR-016, FR-017

**Checkpoint**: Full CRUD read/edit cycle works. User can view DAG visualization, edit Draft workflows, publish/unpublish.

---

## Phase 6: User Story 4 — Workflow 删除确认 (Priority: P1)

**Goal**: Delete Draft workflows with confirmation dialog from list or detail page. Published workflows cannot be deleted.

**Independent Test**: Open list page with a Draft workflow, click delete, confirm in dialog, verify workflow removed. Verify Published workflow delete button is disabled with tooltip.

### Implementation for User Story 4

- [X] T021 [P] [US4] Create DeleteWorkflowDialog component (shadcn/ui AlertDialog) showing workflow name, warning "此操作不可恢复", Confirm/Cancel buttons, calling DELETE /api/workflows/{id}, handling 409 conflict error display, calling onSuccess callback on 204 in Frontend/src/components/workflows/DeleteWorkflowDialog.tsx — FR-018
- [X] T022 [US4] Add delete button to WorkflowListPage row actions (disabled with tooltip for Published status "Published 工作流需先取消发布才能删除") and WorkflowDetailPage header (Draft only), both opening DeleteWorkflowDialog, list page removes row on success, detail page navigates to /workflows on success in Frontend/src/pages/WorkflowListPage.tsx and Frontend/src/pages/WorkflowDetailPage.tsx — FR-006, FR-019

**Checkpoint**: Complete CRUD lifecycle works end-to-end (create → list → view → edit → publish → unpublish → delete).

---

## Phase 7: User Story 5 — 工作流执行触发 (Priority: P2)

**Goal**: Trigger workflow execution from Published workflow detail page with JSON input dialog.

**Independent Test**: Open a Published workflow detail page, click Execute, input valid JSON, submit — verify 202 accepted, dialog closes.

### Implementation for User Story 5

- [X] T023 [P] [US5] Create ExecuteWorkflowDialog component (shadcn/ui Dialog) with textarea JSON editor (pre-filled with {}), syntax highlighting hint via monospace font, JSON.parse validation on submit showing "JSON 格式无效" error below textarea, POST /api/workflows/{id}/execute on valid JSON, handle 400 error display, close dialog and call onSuccess on 202 in Frontend/src/components/workflows/ExecuteWorkflowDialog.tsx — FR-020, FR-021, FR-022, FR-023
- [X] T024 [US5] Add "Execute" button to WorkflowDetailPage (visible only for Published status, disabled/hidden for Draft with tooltip "需先发布才能执行"), opening ExecuteWorkflowDialog, refresh execution history on successful execution in Frontend/src/pages/WorkflowDetailPage.tsx — FR-020

**Checkpoint**: User can trigger workflow execution. Execute → returns 202 → dialog closes.

---

## Phase 8: User Story 6 — 执行历史列表 (Priority: P2)

**Goal**: Display execution history table below workflow detail page with status badges, filter, refresh, and navigation to execution detail.

**Independent Test**: Create executions via API for a workflow, open detail page, verify execution history table shows records with correct status badges, filter by status works, click row navigates to execution detail.

### Implementation for User Story 6

- [X] T025 [P] [US6] Create ExecutionHistoryTable component with table (execution ID first 8 chars, ExecutionStatusBadge, startedAt, completedAt), status filter dropdown (All/Pending/Running/Completed/Failed/Canceled), "Refresh" button calling GET /api/workflows/{id}/executions, row click navigating to /workflows/{id}/executions/{execId}, empty state "暂无执行记录" in Frontend/src/components/workflows/ExecutionHistoryTable.tsx — FR-024, FR-025, FR-026
- [X] T026 [US6] Integrate ExecutionHistoryTable as a section below DAG area in WorkflowDetailPage, passing workflowId prop, exposing refresh ref for US5 execute success callback in Frontend/src/pages/WorkflowDetailPage.tsx — FR-024

**Checkpoint**: Workflow detail page shows execution history. Users can browse, filter, refresh, and navigate to execution details.

---

## Phase 9: User Story 7 — 执行详情页面 (Priority: P2)

**Goal**: Display full execution details including overview, DAG visualization with per-node execution status coloring, and node execution timeline.

**Independent Test**: Create an execution with mixed node statuses via API, open /workflows/:id/executions/:execId, verify DAG nodes colored by status, click Failed node to see error details, timeline shows correct execution order.

### Implementation for User Story 7

- [X] T027 [P] [US7] Create DagExecutionViewer component wrapping ReactFlow in read-only mode, rendering from graphSnapshot using toReactFlowNodes/toReactFlowEdges, overlaying each node with execution status colors (Completed=green border+bg, Failed=red border+bg, Running=blue pulse, Pending=gray, Skipped=light gray+opacity), onClick node showing side panel with nodeId/status/input JSON/output JSON/error/timestamps per R-008 execution overlay colors in Frontend/src/components/workflows/DagExecutionViewer.tsx — FR-029, FR-030
- [X] T028 [P] [US7] Create NodeExecutionTimeline component displaying list of node executions sorted by startedAt with columns: node name, ExecutionStatusBadge, startedAt, duration (completedAt - startedAt), using shadcn/ui Table in Frontend/src/components/workflows/NodeExecutionTimeline.tsx — FR-031
- [X] T029 [US7] Create WorkflowExecutionDetailPage with: (1) breadcrumb back to workflow detail; (2) execution overview section with ExecutionStatusBadge, startedAt, completedAt, duration, collapsible input JSON, output JSON or errorMessage; (3) DagExecutionViewer rendering graphSnapshot with nodeExecutions status overlay; (4) NodeExecutionTimeline below DAG; (5) 404 handling with "执行记录未找到" message and link back to workflow detail; data from GET /api/workflows/{id}/executions/{execId} in Frontend/src/pages/WorkflowExecutionDetailPage.tsx — FR-027, FR-028, FR-029, FR-031

**Checkpoint**: Complete execution flow works end-to-end (execute → view history → view execution detail → identify failed nodes via colored DAG).

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories — loading states, error boundaries, edge case handling.

- [X] T030 [P] Ensure all API-triggering buttons (save, delete, execute, publish) show loading spinner and are disabled during request to prevent double submission across all pages — FR-032
- [X] T031 [P] Add unsaved-changes prompt (window beforeunload + route navigation guard) when leaving WorkflowDetailPage in edit mode or WorkflowCreatePage with dirty form data
- [X] T032 Run quickstart.md verification steps end-to-end: install deps → create workflow with 3+ nodes → view list → edit → publish → execute → view execution detail with colored DAG

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — **BLOCKS all user stories**
- **US1 (Phase 3)**: Depends on Foundational — can start immediately after Phase 2
- **US2 (Phase 4)**: Depends on Foundational — can start in parallel with US1
- **US3 (Phase 5)**: Depends on Foundational + reuses custom nodes from US2 (T010-T014) — start after US2 or copy custom node pattern
- **US4 (Phase 6)**: Depends on US1 + US3 (modifies both pages) — start after Phase 5
- **US5 (Phase 7)**: Depends on US3 (modifies WorkflowDetailPage) — start after Phase 5
- **US6 (Phase 8)**: Depends on US3 (modifies WorkflowDetailPage) — start after Phase 5, can parallel with US5
- **US7 (Phase 9)**: Depends on Foundational — can parallel with US5/US6 (independent page)
- **Polish (Phase 10)**: Depends on all desired user stories being complete

### User Story Dependencies

- **US8 (Routes)**: Foundational — no dependencies on other stories
- **US1 (List)**: Independent after Foundational — **MVP candidate**
- **US2 (Create + DAG)**: Independent after Foundational — produces custom nodes reused by US3, US7
- **US3 (Detail/Edit)**: Uses custom nodes from US2 and DagViewer (builds on editor patterns)
- **US4 (Delete)**: Modifies US1 (list page) and US3 (detail page) — must come after both
- **US5 (Execute)**: Modifies US3 (detail page) — must come after US3
- **US6 (Exec History)**: Modifies US3 (detail page) — must come after US3, parallel with US5
- **US7 (Exec Detail)**: Independent page using shared components — can parallel with US5/US6

### Within Each User Story

- Shared components (badges, nodes, panels) before composite components (DagEditor, DagViewer)
- Composite components before page components
- Core page implementation before integration with other pages

### Parallel Opportunities

Within Phase 2: T004, T005, T006, T007, T008 all parallel (different files)
Within Phase 4: T010-T016 all parallel (different files), then T017, then T018
Within Phase 9: T027, T028 parallel (different files), then T029
Cross-story: US1 and US2 can run in parallel after Phase 2
Cross-story: US5, US6, US7 can partially overlap (US7 is an independent page)

---

## Parallel Example: User Story 2

```
# Launch all custom nodes + panels in parallel (7 different files):
T010: AgentNode in custom-nodes/AgentNode.tsx
T011: ToolNode in custom-nodes/ToolNode.tsx
T012: ConditionNode in custom-nodes/ConditionNode.tsx
T013: FanOutNode in custom-nodes/FanOutNode.tsx
T014: FanInNode in custom-nodes/FanInNode.tsx
T015: NodePanel in NodePanel.tsx
T016: NodePropertyPanel in NodePropertyPanel.tsx

# Then sequentially:
T017: DagEditor (depends on T010-T016)
T018: WorkflowCreatePage (depends on T017)
```

---

## Implementation Strategy

### MVP First (US8 + US1 Only)

1. Complete Phase 1: Setup (install deps, types, API client)
2. Complete Phase 2: Foundational (routes, sidebar, badges, dag-utils)
3. Complete Phase 3: US1 — WorkflowListPage
4. **STOP and VALIDATE**: Navigate to /workflows, verify table renders with backend data
5. Deploy/demo if ready — users can browse workflows

### Incremental Delivery

1. Setup + Foundational → Foundation ready (routes, types, API)
2. US1 → Workflow list browsable → **MVP!**
3. US2 → Workflow creation with DAG editor → Deploy/Demo
4. US3 → Workflow detail view + editing → Deploy/Demo
5. US4 → Delete capability → Full CRUD complete → Deploy/Demo
6. US5 → Execute trigger → Deploy/Demo
7. US6 → Execution history → Deploy/Demo
8. US7 → Execution detail with colored DAG → Deploy/Demo (Feature Complete)
9. Polish → Production-ready

### Parallel Team Strategy

With multiple developers after Phase 2:

- **Developer A**: US1 (list) → US4 (delete integration)
- **Developer B**: US2 (create + DAG) → US3 (detail/edit)
- **Developer C**: US5 (execute) + US6 (exec history) after US3 done
- **Developer D**: US7 (execution detail page, independent)

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story for traceability
- No test tasks included — 0 existing frontend test files, justified deviation per plan.md
- Each user story is independently completable and testable via manual verification
- WorkflowDetailPage (T020) is incrementally extended by US4 (T022), US5 (T024), US6 (T026)
- Custom nodes (T010-T014) are shared by DagEditor (US2), DagViewer (US3), DagExecutionViewer (US7)
- Reference spec.md for acceptance scenarios, data-model.md for type definitions, contracts/api-contracts.md for API shapes, research.md for architecture decisions
- Commit after each task or logical group
