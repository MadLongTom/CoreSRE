# Research: 013-workflow-frontend

**Date**: 2026-02-11  
**Feature**: Workflow 前端管理页面

## R-001: DAG Editor Library Selection

**Decision**: Use `@xyflow/react` (React Flow v12+)

**Rationale**:
- Most mature and widely adopted React graph editor library (15k+ GitHub stars)
- Built-in support for: custom nodes, custom edges, drag-and-drop, zoom/pan, minimap, controls
- TypeScript-first with comprehensive type definitions
- Supports both controlled and uncontrolled flow modes
- No external layout engine required for manual node placement
- MIT license (free for commercial use; attribution via watermark in free tier, removable with pro subscription)

**Alternatives considered**:
- **vis-network**: Canvas-based, not React-native, complex integration with React state
- **dagre + custom SVG**: Full control but enormous development effort for basic interaction features
- **mermaid.js**: Declarative rendering only, no interactive editing capability
- **Custom Canvas/SVG**: Maximum flexibility but 10x+ development time for drag/drop, zoom, connections

**Integration pattern**:
- Install: `npm install @xyflow/react`
- Import CSS: `import '@xyflow/react/dist/style.css'`
- Node data model: `{ id, type, position: {x, y}, data: {...} }` 
- Edge data model: `{ id, source, target, type?, data?: {...} }`
- Custom node types registered via `nodeTypes` prop
- Custom edge types registered via `edgeTypes` prop

---

## R-002: Node Position Persistence Strategy

**Decision**: Store position coordinates in the backend `Config` JSON field of `WorkflowNodeVO`

**Rationale**:
- The backend `WorkflowNodeVO` has a `Config` field (`string?`, JSON) already designed for arbitrary node configuration
- Adding position data to Config avoids any backend schema changes or migrations
- Position data is inherently tied to the node and should persist across sessions/devices
- Config is already serialized/deserialized as JSON — adding `position: {x, y}` is trivial
- React Flow nodes require `position: {x, y}` — mapping to/from Config is straightforward

**Alternatives considered**:
- **Add `PositionX`/`PositionY` to backend `WorkflowNodeVO`**: Requires backend domain + DTO + migration changes outside this spec's scope. Cleaner data model but higher change footprint
- **localStorage**: Positions lost when switching devices or clearing browser data. Not suitable for collaborative use
- **Separate frontend-only persistence table**: Over-engineering for position data

**Implementation**:
- When saving to backend: serialize `{ ..existingConfig, position: { x, y } }` into the Config field
- When loading from backend: parse Config JSON, extract `position` for React Flow, pass remaining fields as node `data`
- If Config is null or has no position: assign default auto-layout positions (simple grid layout)

---

## R-003: DAG Auto-Layout for Initial Display

**Decision**: Use simple dagre-based auto-layout for nodes without saved positions; manual positioning by user for ongoing edits

**Rationale**:
- Nodes loaded from backend without position data (e.g., created via API directly) need initial layout
- `dagre` library provides topological graph layout suitable for DAG visualization
- After initial layout, React Flow's drag-and-drop handles repositioning
- User-placed positions are saved back to Config, so dagre is only for fallback

**Alternatives considered**:
- **Fixed grid layout**: Too rigid, doesn't represent actual graph topology
- **No auto-layout (all at 0,0)**: Terrible UX — overlapping nodes
- **elk.js**: More powerful but heavier dependency; dagre is sufficient for DAG patterns

**Implementation**:
- Install: `npm install @dagrejs/dagre` (lightweight, ~30KB)
- Apply layout only when nodes lack position data in their Config
- Direction: top-to-bottom (TB) — natural DAG reading direction

---

## R-004: Frontend Type Mapping to Backend DTOs

**Decision**: Define TypeScript types in `src/types/workflow.ts` that map 1:1 to backend DTOs, following existing project conventions

**Rationale**:
- Existing types (`agent.ts`, `tool.ts`, `provider.ts`) follow this exact pattern
- String literal union types for enums (e.g., `type WorkflowStatus = "Draft" | "Published"`)
- Const arrays for iteration in UI (e.g., `WORKFLOW_STATUSES`)
- Summary types for list views, Detail types for full views
- Re-export shared `ApiResult<T>` type

**Backend DTO → Frontend Type mapping**:

| Backend DTO | Frontend Type |
|------------|---------------|
| `WorkflowSummaryDto` | `WorkflowSummary` |
| `WorkflowDefinitionDto` | `WorkflowDetail` |
| `WorkflowGraphDto` | `WorkflowGraph` |
| `WorkflowNodeDto` | `WorkflowNode` |
| `WorkflowEdgeDto` | `WorkflowEdge` |
| `WorkflowExecutionSummaryDto` | `WorkflowExecutionSummary` |
| `WorkflowExecutionDto` | `WorkflowExecutionDetail` |
| `NodeExecutionDto` | `NodeExecution` |

**Key differences from spec Key Entities**:
- Spec mentioned `position (x, y)` on WorkflowNode — this is embedded in Config JSON, not a separate field
- React Flow types (`Node`, `Edge`) are internal to the DAG editor component; conversion happens at the component boundary

---

## R-005: API Client Architecture

**Decision**: Create `src/lib/api/workflows.ts` following the existing `agents.ts` pattern

**Rationale**:
- Consistency with established project conventions
- `fetchWithTimeout()` + `handleResponse<T>()` pattern already proven
- Standalone async function exports (no class)
- Error handling via `ApiError` class with `status`, `errors[]`, `errorCode`

**Functions to implement**:

| Function | HTTP | Endpoint |
|----------|------|----------|
| `getWorkflows(status?)` | GET | `/api/workflows?status=` |
| `getWorkflowById(id)` | GET | `/api/workflows/{id}` |
| `createWorkflow(data)` | POST | `/api/workflows` |
| `updateWorkflow(id, data)` | PUT | `/api/workflows/{id}` |
| `deleteWorkflow(id)` | DELETE | `/api/workflows/{id}` |
| `executeWorkflow(id, input)` | POST | `/api/workflows/{id}/execute` |
| `getWorkflowExecutions(id, status?)` | GET | `/api/workflows/{id}/executions` |
| `getWorkflowExecutionById(id, execId)` | GET | `/api/workflows/{id}/executions/{execId}` |

---

## R-006: JSON Input Editor Approach

**Decision**: Use a styled `<textarea>` with `monospace` font, JSON.parse validation, and manual syntax error display

**Rationale**:
- Spec explicitly assumes "textarea + 前端 JSON.parse 校验的简单方案，不引入 Monaco Editor 等重量级依赖"
- Monaco Editor adds ~3MB to bundle size — overkill for a simple JSON input field
- `JSON.parse()` try/catch provides adequate validation with error line/position info
- `<textarea>` with `font-family: monospace` and appropriate styling gives adequate code-editing feel

**Alternatives considered**:
- **Monaco Editor**: Full IDE experience but massive bundle impact (~3MB gzipped)
- **CodeMirror 6**: Lighter (~200KB) but still significant for one textarea
- **react-json-editor**: Various quality levels, adds maintenance dependency

---

## R-007: Component Architecture

**Decision**: Follow existing per-resource component organization with shared DAG editor components

**Structure**:
```
Frontend/src/
├── components/
│   └── workflows/
│       ├── WorkflowStatusBadge.tsx     # Draft/Published badge
│       ├── ExecutionStatusBadge.tsx     # Pending/Running/Completed/Failed/Canceled badge
│       ├── DeleteWorkflowDialog.tsx     # Confirmation dialog (mirrors DeleteAgentDialog)
│       ├── ExecuteWorkflowDialog.tsx    # JSON input + execute submit
│       ├── ExecutionHistoryTable.tsx    # Execution list sub-component
│       ├── NodeExecutionTimeline.tsx    # Timeline list for execution detail
│       ├── DagEditor.tsx               # Interactive DAG editor (create/edit mode)
│       ├── DagViewer.tsx               # Read-only DAG visualization
│       ├── DagExecutionViewer.tsx       # DAG viz with execution status coloring
│       ├── NodePanel.tsx               # Sidebar panel for dragging node types
│       ├── NodePropertyPanel.tsx       # Properties panel for selected node/edge
│       └── custom-nodes/
│           ├── AgentNode.tsx           # Custom React Flow node for Agent type
│           ├── ToolNode.tsx            # Custom React Flow node for Tool type
│           ├── ConditionNode.tsx       # Custom React Flow node for Condition type
│           ├── FanOutNode.tsx          # Custom React Flow node for FanOut type
│           └── FanInNode.tsx           # Custom React Flow node for FanIn type
├── pages/
│   ├── WorkflowListPage.tsx
│   ├── WorkflowCreatePage.tsx
│   ├── WorkflowDetailPage.tsx
│   └── WorkflowExecutionDetailPage.tsx
├── types/
│   └── workflow.ts
└── lib/api/
    └── workflows.ts
```

---

## R-008: Custom Node Design Colors

**Decision**: Use distinct colors per node type for visual differentiation

| Node Type | Color | Icon (lucide-react) |
|-----------|-------|---------------------|
| Agent | Blue (`bg-blue-100 border-blue-500`) | `Bot` |
| Tool | Orange (`bg-orange-100 border-orange-500`) | `Wrench` |
| Condition | Purple (`bg-purple-100 border-purple-500`) | `GitBranch` |
| FanOut | Teal (`bg-teal-100 border-teal-500`) | `Split` |
| FanIn | Teal (`bg-teal-100 border-teal-500`) | `Merge` |

**Execution status overlay colors**:
| Status | Color |
|--------|-------|
| Pending | Gray (`border-gray-300`) |
| Running | Blue pulse (`border-blue-500 animate-pulse`) |
| Completed | Green (`border-green-500 bg-green-50`) |
| Failed | Red (`border-red-500 bg-red-50`) |
| Skipped | Light gray (`border-gray-200 bg-gray-50 opacity-60`) |
