import { useMemo } from "react";
import { DagViewer } from "./DagViewer";
import type { WorkflowNode, WorkflowEdge, NodeExecution, NodeExecutionStatus } from "@/types/workflow";

const statusColorMap: Record<NodeExecutionStatus, string> = {
  Pending: "#a3a3a3",    // gray
  Running: "#3b82f6",    // blue
  Completed: "#22c55e",  // green
  Failed: "#ef4444",     // red
  Skipped: "#f59e0b",    // amber
};

interface DagExecutionViewerProps {
  nodes: WorkflowNode[];
  edges: WorkflowEdge[];
  nodeExecutions: NodeExecution[];
  className?: string;
}

export function DagExecutionViewer({
  nodes,
  edges,
  nodeExecutions,
  className,
}: DagExecutionViewerProps) {
  const nodeColors = useMemo(() => {
    const map: Record<string, string> = {};
    for (const ne of nodeExecutions) {
      map[ne.nodeId] = statusColorMap[ne.status] ?? "#a3a3a3";
    }
    return map;
  }, [nodeExecutions]);

  return (
    <DagViewer
      nodes={nodes}
      edges={edges}
      nodeColors={nodeColors}
      className={className}
    />
  );
}
