// =============================================================================
// Workflow Type Definitions — maps to backend C# DTOs
// See: specs/013-workflow-frontend/data-model.md
// =============================================================================

import type { ApiResult } from "@/types/agent";

// Re-export ApiResult for convenience
export type { ApiResult };

// ---------------------------------------------------------------------------
// Enums (string literal unions)
// ---------------------------------------------------------------------------

/** Maps to backend WorkflowStatus enum: Draft, Published */
export type WorkflowStatus = "Draft" | "Published";

/** Maps to backend WorkflowNodeType enum */
export type WorkflowNodeType = "Agent" | "Tool" | "Condition" | "FanOut" | "FanIn";

/** Maps to backend WorkflowEdgeType enum */
export type WorkflowEdgeType = "Normal" | "Conditional";

/** Maps to backend ExecutionStatus enum */
export type ExecutionStatus = "Pending" | "Running" | "Completed" | "Failed" | "Canceled";

/** Maps to backend NodeExecutionStatus enum */
export type NodeExecutionStatus = "Pending" | "Running" | "Completed" | "Failed" | "Skipped";

/** All valid workflow statuses for iteration */
export const WORKFLOW_STATUSES: WorkflowStatus[] = ["Draft", "Published"];

/** All valid node types for iteration */
export const WORKFLOW_NODE_TYPES: WorkflowNodeType[] = [
  "Agent",
  "Tool",
  "Condition",
  "FanOut",
  "FanIn",
];

/** All valid edge types for iteration */
export const WORKFLOW_EDGE_TYPES: WorkflowEdgeType[] = ["Normal", "Conditional"];

/** All valid execution statuses for iteration */
export const EXECUTION_STATUSES: ExecutionStatus[] = [
  "Pending",
  "Running",
  "Completed",
  "Failed",
  "Canceled",
];

/** All valid node execution statuses for iteration */
export const NODE_EXECUTION_STATUSES: NodeExecutionStatus[] = [
  "Pending",
  "Running",
  "Completed",
  "Failed",
  "Skipped",
];

// ---------------------------------------------------------------------------
// Workflow Definition (CRUD)
// ---------------------------------------------------------------------------

/** Maps to backend WorkflowSummaryDto — used in GET /api/workflows list */
export interface WorkflowSummary {
  id: string;
  name: string;
  description: string | null;
  status: WorkflowStatus;
  nodeCount: number;
  createdAt: string;
  updatedAt: string | null;
}

/** Maps to backend WorkflowDefinitionDto — used in GET /api/workflows/{id} */
export interface WorkflowDetail extends WorkflowSummary {
  graph: WorkflowGraph;
}

/** Maps to backend WorkflowGraphVO */
export interface WorkflowGraph {
  nodes: WorkflowNode[];
  edges: WorkflowEdge[];
}

/** Maps to backend WorkflowNodeVO */
export interface WorkflowNode {
  nodeId: string;
  nodeType: WorkflowNodeType;
  referenceId: string | null;
  displayName: string;
  config: string | null;
}

/** Maps to backend WorkflowEdgeVO */
export interface WorkflowEdge {
  edgeId: string;
  sourceNodeId: string;
  targetNodeId: string;
  edgeType: WorkflowEdgeType;
  condition: string | null;
}

// ---------------------------------------------------------------------------
// Workflow Execution (Read-only)
// ---------------------------------------------------------------------------

/** Maps to backend WorkflowExecutionSummaryDto — used in execution list */
export interface WorkflowExecutionSummary {
  id: string;
  status: ExecutionStatus;
  startedAt: string | null;
  completedAt: string | null;
  createdAt: string;
}

/** Maps to backend WorkflowExecutionDto — used in execution detail */
export interface WorkflowExecutionDetail extends WorkflowExecutionSummary {
  workflowDefinitionId: string;
  input: unknown;
  output: unknown | null;
  errorMessage: string | null;
  traceId: string | null;
  graphSnapshot: WorkflowGraph;
  nodeExecutions: NodeExecution[];
}

/** Maps to backend NodeExecutionDto */
export interface NodeExecution {
  nodeId: string;
  status: NodeExecutionStatus;
  input: string | null;
  output: string | null;
  errorMessage: string | null;
  startedAt: string | null;
  completedAt: string | null;
}

// ---------------------------------------------------------------------------
// Request Types (Command Bodies)
// ---------------------------------------------------------------------------

/** POST /api/workflows body */
export interface CreateWorkflowRequest {
  name: string;
  description?: string | null;
  graph: WorkflowGraph;
}

/** PUT /api/workflows/{id} body */
export interface UpdateWorkflowRequest {
  name: string;
  description?: string | null;
  graph: WorkflowGraph;
  status?: WorkflowStatus;
}

/** POST /api/workflows/{id}/execute body */
export interface ExecuteWorkflowRequest {
  inputData?: unknown;
}

// ---------------------------------------------------------------------------
// React Flow Internal Types (component-internal, not persisted)
// ---------------------------------------------------------------------------

/** Data payload for custom React Flow nodes */
export interface DagNodeData {
  nodeType: WorkflowNodeType;
  referenceId: string | null;
  displayName: string;
  config: Record<string, unknown>;
}

/** Data payload for React Flow edges */
export interface DagEdgeData {
  edgeType: WorkflowEdgeType;
  condition: string | null;
}
