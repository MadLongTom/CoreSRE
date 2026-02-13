// =============================================================================
// DAG Conversion Utilities — React Flow ↔ Backend WorkflowGraph converters
// See: specs/013-workflow-frontend/data-model.md, research.md R-002/R-003
// =============================================================================

import type { Node, Edge } from "@xyflow/react";
import dagre from "@dagrejs/dagre";
import type {
  DagNodeData,
  DagEdgeData,
  WorkflowNode,
  WorkflowEdge,
  WorkflowGraph,
} from "@/types/workflow";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const NODE_WIDTH = 200;
const NODE_HEIGHT = 80;

// ---------------------------------------------------------------------------
// Port Handle Utilities — multi-port support for React Flow
// ---------------------------------------------------------------------------

/**
 * Generate a unique React Flow handle ID for a port.
 * Format: "source-0", "source-1", "target-0", "target-1", etc.
 */
export function portHandleId(type: "source" | "target", index: number): string {
  return `${type}-${index}`;
}

/**
 * Parse port index from a React Flow handle ID.
 * Returns 0 if handle is null/undefined or doesn't match expected format.
 */
export function parsePortIndex(handleId: string | null | undefined): number {
  if (!handleId) return 0;
  const match = handleId.match(/^(?:source|target)-(\d+)$/);
  return match ? parseInt(match[1], 10) : 0;
}

// ---------------------------------------------------------------------------
// Backend → React Flow
// ---------------------------------------------------------------------------

/**
 * Convert backend WorkflowNode[] to React Flow Node[].
 * Parses Config JSON for position; extracts remaining config as node data.
 */
export function toReactFlowNodes(
  nodes: WorkflowNode[],
): Node<DagNodeData>[] {
  return nodes.map((node) => {
    let position = { x: 0, y: 0 };
    let config: Record<string, unknown> = {};

    if (node.config) {
      try {
        const parsed = JSON.parse(node.config);
        if (parsed.position && typeof parsed.position.x === "number" && typeof parsed.position.y === "number") {
          position = { x: parsed.position.x, y: parsed.position.y };
        }
        // Extract remaining config (everything except position)
        const { position: _pos, ...rest } = parsed;
        config = rest;
      } catch {
        // Invalid JSON in config — use defaults
      }
    }

    return {
      id: node.nodeId,
      type: node.nodeType,
      position,
      data: {
        nodeType: node.nodeType,
        referenceId: node.referenceId,
        displayName: node.displayName,
        config,
        inputCount: node.inputCount ?? 1,
        outputCount: node.outputCount ?? 1,
      },
    };
  });
}

/**
 * Convert backend WorkflowEdge[] to React Flow Edge[].
 * Maps sourcePortIndex/targetPortIndex to React Flow sourceHandle/targetHandle.
 */
export function toReactFlowEdges(
  edges: WorkflowEdge[],
): Edge<DagEdgeData>[] {
  return edges.map((edge) => ({
    id: edge.edgeId,
    source: edge.sourceNodeId,
    target: edge.targetNodeId,
    sourceHandle: portHandleId("source", edge.sourcePortIndex ?? 0),
    targetHandle: portHandleId("target", edge.targetPortIndex ?? 0),
    type: edge.edgeType === "Conditional" ? "default" : "default",
    animated: edge.edgeType === "Conditional",
    label: edge.edgeType === "Conditional" && edge.condition
      ? edge.condition
      : undefined,
    data: {
      edgeType: edge.edgeType,
      condition: edge.condition,
      sourcePortIndex: edge.sourcePortIndex ?? 0,
      targetPortIndex: edge.targetPortIndex ?? 0,
    },
  }));
}

// ---------------------------------------------------------------------------
// React Flow → Backend
// ---------------------------------------------------------------------------

/**
 * Convert React Flow state back to backend WorkflowGraph.
 * Serializes node positions into Config JSON field.
 */
export function fromReactFlowState(
  nodes: Node<DagNodeData>[],
  edges: Edge<DagEdgeData>[],
): WorkflowGraph {
  const workflowNodes: WorkflowNode[] = nodes.map((node) => {
    const data = node.data;
    // Merge position into config
    const configObj = {
      ...data.config,
      position: { x: node.position.x, y: node.position.y },
    };

    return {
      nodeId: node.id,
      nodeType: data.nodeType,
      referenceId: data.referenceId,
      displayName: data.displayName,
      config: JSON.stringify(configObj),
      inputCount: data.inputCount,
      outputCount: data.outputCount,
    };
  });

  const workflowEdges: WorkflowEdge[] = edges.map((edge) => ({
    edgeId: edge.id,
    sourceNodeId: edge.source,
    targetNodeId: edge.target,
    edgeType: edge.data?.edgeType ?? "Normal",
    condition: edge.data?.condition ?? null,
    sourcePortIndex: edge.data?.sourcePortIndex ?? parsePortIndex(edge.sourceHandle),
    targetPortIndex: edge.data?.targetPortIndex ?? parsePortIndex(edge.targetHandle),
  }));

  return { nodes: workflowNodes, edges: workflowEdges };
}

// ---------------------------------------------------------------------------
// Auto-Layout (dagre) — for nodes without saved positions
// ---------------------------------------------------------------------------

/**
 * Apply dagre auto-layout to nodes. Only repositions nodes that are at (0,0).
 * Direction: top-to-bottom (TB).
 */
export function autoLayout(
  nodes: Node<DagNodeData>[],
  edges: Edge<DagEdgeData>[],
): Node<DagNodeData>[] {
  // Check if any nodes need layout (all at origin)
  const needsLayout = nodes.every(
    (n) => n.position.x === 0 && n.position.y === 0,
  );

  if (!needsLayout || nodes.length === 0) {
    return nodes;
  }

  const g = new dagre.graphlib.Graph();
  g.setDefaultEdgeLabel(() => ({}));
  g.setGraph({ rankdir: "TB", nodesep: 60, ranksep: 80 });

  nodes.forEach((node) => {
    g.setNode(node.id, { width: NODE_WIDTH, height: NODE_HEIGHT });
  });

  edges.forEach((edge) => {
    g.setEdge(edge.source, edge.target);
  });

  dagre.layout(g);

  return nodes.map((node) => {
    const dagreNode = g.node(node.id);
    return {
      ...node,
      position: {
        x: dagreNode.x - NODE_WIDTH / 2,
        y: dagreNode.y - NODE_HEIGHT / 2,
      },
    };
  });
}
