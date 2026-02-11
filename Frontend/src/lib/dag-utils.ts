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
      },
    };
  });
}

/**
 * Convert backend WorkflowEdge[] to React Flow Edge[].
 */
export function toReactFlowEdges(
  edges: WorkflowEdge[],
): Edge<DagEdgeData>[] {
  return edges.map((edge) => ({
    id: edge.edgeId,
    source: edge.sourceNodeId,
    target: edge.targetNodeId,
    type: edge.edgeType === "Conditional" ? "default" : "default",
    animated: edge.edgeType === "Conditional",
    label: edge.edgeType === "Conditional" && edge.condition
      ? edge.condition
      : undefined,
    data: {
      edgeType: edge.edgeType,
      condition: edge.condition,
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
    };
  });

  const workflowEdges: WorkflowEdge[] = edges.map((edge) => ({
    edgeId: edge.id,
    sourceNodeId: edge.source,
    targetNodeId: edge.target,
    edgeType: edge.data?.edgeType ?? "Normal",
    condition: edge.data?.condition ?? null,
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
