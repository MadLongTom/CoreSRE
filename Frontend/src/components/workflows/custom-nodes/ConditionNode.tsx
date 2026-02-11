import { memo } from "react";
import { Handle, Position, type NodeProps } from "@xyflow/react";
import { GitBranch } from "lucide-react";
import type { DagNodeData } from "@/types/workflow";

function ConditionNodeComponent({ data, selected }: NodeProps) {
  const nodeData = data as DagNodeData;
  return (
    <div
      className={`rounded-lg border-2 bg-purple-50 px-4 py-3 min-w-[180px] shadow-sm transition-shadow ${
        selected ? "border-purple-600 shadow-md" : "border-purple-400"
      }`}
    >
      <Handle type="target" position={Position.Top} className="!bg-purple-500" />
      <div className="flex items-center gap-2">
        <GitBranch className="h-4 w-4 text-purple-600 shrink-0" />
        <div className="min-w-0">
          <div className="text-sm font-medium text-purple-900 truncate">
            {nodeData.displayName}
          </div>
          <div className="text-xs text-purple-500">Condition</div>
        </div>
      </div>
      <Handle type="source" position={Position.Bottom} className="!bg-purple-500" />
    </div>
  );
}

export const ConditionNode = memo(ConditionNodeComponent);
