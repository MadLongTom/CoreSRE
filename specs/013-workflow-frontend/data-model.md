# Data Model: 013-workflow-frontend

**Date**: 2026-02-11

## Frontend Type Definitions

All types map 1:1 to backend DTOs. String literal unions for enums. File: `src/types/workflow.ts`.

### Enums (String Literal Unions)

```typescript
type WorkflowStatus = "Draft" | "Published"
type WorkflowNodeType = "Agent" | "Tool" | "Condition" | "FanOut" | "FanIn"
type WorkflowEdgeType = "Normal" | "Conditional"
type ExecutionStatus = "Pending" | "Running" | "Completed" | "Failed" | "Canceled"
type NodeExecutionStatus = "Pending" | "Running" | "Completed" | "Failed" | "Skipped"
```

### Workflow Definition (CRUD)

#### WorkflowSummary (list view)
| Field | Type | Source |
|-------|------|--------|
| id | `string` | Backend `Guid` serialized as string |
| name | `string` | |
| description | `string \| null` | |
| status | `WorkflowStatus` | |
| nodeCount | `number` | Summary-only field |
| createdAt | `string` | ISO 8601 |
| updatedAt | `string \| null` | ISO 8601 |

#### WorkflowDetail (detail view)
Extends WorkflowSummary with:
| Field | Type | Source |
|-------|------|--------|
| graph | `WorkflowGraph` | Full DAG definition |

#### WorkflowGraph
| Field | Type |
|-------|------|
| nodes | `WorkflowNode[]` |
| edges | `WorkflowEdge[]` |

#### WorkflowNode
| Field | Type | Notes |
|-------|------|-------|
| nodeId | `string` | Unique within graph |
| nodeType | `WorkflowNodeType` | |
| referenceId | `string \| null` | AgentId or ToolId (Guid string) |
| displayName | `string` | |
| config | `string \| null` | JSON string; may contain `{ position: {x, y}, ...otherConfig }` |

#### WorkflowEdge
| Field | Type | Notes |
|-------|------|-------|
| edgeId | `string` | Unique within graph |
| sourceNodeId | `string` | References WorkflowNode.nodeId |
| targetNodeId | `string` | References WorkflowNode.nodeId |
| edgeType | `WorkflowEdgeType` | |
| condition | `string \| null` | JSON Path expression for Conditional edges |

### Workflow Execution (Read-only)

#### WorkflowExecutionSummary (list view)
| Field | Type |
|-------|------|
| id | `string` |
| status | `ExecutionStatus` |
| startedAt | `string \| null` |
| completedAt | `string \| null` |
| createdAt | `string` |

#### WorkflowExecutionDetail (detail view)
Extends WorkflowExecutionSummary with:
| Field | Type |
|-------|------|
| workflowDefinitionId | `string` |
| input | `unknown` | JSON value |
| output | `unknown \| null` | JSON value |
| errorMessage | `string \| null` |
| traceId | `string \| null` |
| graphSnapshot | `WorkflowGraph` |
| nodeExecutions | `NodeExecution[]` |

#### NodeExecution
| Field | Type |
|-------|------|
| nodeId | `string` |
| status | `NodeExecutionStatus` |
| input | `string \| null` | JSON string |
| output | `string \| null` | JSON string |
| errorMessage | `string \| null` |
| startedAt | `string \| null` |
| completedAt | `string \| null` |

### Request Types

#### CreateWorkflowRequest
| Field | Type | Validation |
|-------|------|-----------|
| name | `string` | Required, max 200 chars |
| description | `string \| null` | Optional, max 2000 chars |
| graph | `WorkflowGraph` | Min 2 nodes, min 1 edge |

#### UpdateWorkflowRequest
Same as CreateWorkflowRequest (ID from URL path)

#### ExecuteWorkflowRequest
| Field | Type | Validation |
|-------|------|-----------|
| input | `unknown` | Valid JSON, defaults to `{}` |

### React Flow Internal Types (component-internal, not persisted)

#### DagNode (extends React Flow Node)
| Field | Type | Notes |
|-------|------|-------|
| id | `string` | Maps to WorkflowNode.nodeId |
| type | `string` | Custom node type key |
| position | `{ x: number, y: number }` | Extracted from Config |
| data | `DagNodeData` | Node metadata |

#### DagNodeData
| Field | Type |
|-------|------|
| nodeType | `WorkflowNodeType` |
| referenceId | `string \| null` |
| displayName | `string` |
| config | `Record<string, unknown>` | Parsed Config minus position |

#### DagEdge (extends React Flow Edge)
| Field | Type | Notes |
|-------|------|-------|
| id | `string` | Maps to WorkflowEdge.edgeId |
| source | `string` | sourceNodeId |
| target | `string` | targetNodeId |
| data | `DagEdgeData` | Edge metadata |

#### DagEdgeData
| Field | Type |
|-------|------|
| edgeType | `WorkflowEdgeType` |
| condition | `string \| null` |

## Data Flow

```
Backend API (JSON) ──── WorkflowDetail ──── toReactFlowNodes() ──── React Flow internal state
                                          │                        │
                                          │                        ▼
                                          │                    User edits (drag, connect, delete)
                                          │                        │
                                          │                        ▼
                        WorkflowGraph ◄── fromReactFlowState() ◄── React Flow nodes/edges
                              │
                              ▼
                    API request body (POST/PUT)
```

Conversion utilities needed:
1. `toReactFlowNodes(nodes: WorkflowNode[]) → Node[]` — parse Config for position, extract data
2. `toReactFlowEdges(edges: WorkflowEdge[]) → Edge[]` — map field names
3. `fromReactFlowState(nodes: Node[], edges: Edge[]) → WorkflowGraph` — serialize positions back into Config
4. `autoLayout(nodes: Node[], edges: Edge[]) → Node[]` — dagre layout for nodes without positions
