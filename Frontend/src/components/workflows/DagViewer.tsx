import { useMemo } from "react";
import {
  ReactFlow,
  Background,
  Controls,
  MiniMap,
  type NodeTypes,
} from "@xyflow/react";
import "@xyflow/react/dist/style.css";

import { AgentNode } from "./custom-nodes/AgentNode";
import { ToolNode } from "./custom-nodes/ToolNode";
import { ConditionNode } from "./custom-nodes/ConditionNode";
import { FanOutNode } from "./custom-nodes/FanOutNode";
import { FanInNode } from "./custom-nodes/FanInNode";
import { toReactFlowNodes, toReactFlowEdges, autoLayout } from "@/lib/dag-utils";
import type { WorkflowNode, WorkflowEdge } from "@/types/workflow";

const nodeTypes: NodeTypes = {
  Agent: AgentNode,
  Tool: ToolNode,
  Condition: ConditionNode,
  FanOut: FanOutNode,
  FanIn: FanInNode,
};

interface DagViewerProps {
  nodes: WorkflowNode[];
  edges: WorkflowEdge[];
  className?: string;
  /** Optional highlight map: nodeId → CSS color for execution overlay */
  nodeColors?: Record<string, string>;
}

export function DagViewer({ nodes, edges, className, nodeColors }: DagViewerProps) {
  const { rfNodes, rfEdges } = useMemo(() => {
    let rfNodes = toReactFlowNodes(nodes);
    let rfEdges = toReactFlowEdges(edges);

    // Auto-layout if all nodes are at origin
    const allAtOrigin = rfNodes.every(
      (n) => (n.position?.x ?? 0) === 0 && (n.position?.y ?? 0) === 0,
    );
    if (allAtOrigin && rfNodes.length > 0) {
      const laid = autoLayout(rfNodes, rfEdges);
      rfNodes = laid.nodes;
      rfEdges = laid.edges;
    }

    // Apply execution highlight colors
    if (nodeColors) {
      rfNodes = rfNodes.map((n) => {
        const color = nodeColors[n.id];
        if (color) {
          return {
            ...n,
            style: { ...(n.style ?? {}), borderColor: color, borderWidth: 3 },
          };
        }
        return n;
      });
    }

    return { rfNodes, rfEdges };
  }, [nodes, edges, nodeColors]);

  return (
    <div className={className ?? "h-[500px] rounded-md border"}>
      <ReactFlow
        nodes={rfNodes}
        edges={rfEdges}
        nodeTypes={nodeTypes}
        nodesDraggable={false}
        nodesConnectable={false}
        elementsSelectable={false}
        fitView
        fitViewOptions={{ padding: 0.2 }}
        proOptions={{ hideAttribution: true }}
      >
        <Background />
        <Controls showInteractive={false} />
        <MiniMap pannable={false} zoomable={false} />
      </ReactFlow>
    </div>
  );
}
