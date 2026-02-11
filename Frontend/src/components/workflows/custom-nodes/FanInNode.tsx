import { memo } from "react";
import { Handle, Position, type NodeProps } from "@xyflow/react";
import { Merge } from "lucide-react";
import type { DagNodeData } from "@/types/workflow";

function FanInNodeComponent({ data, selected }: NodeProps) {
  const nodeData = data as DagNodeData;
  return (
    <div
      className={`rounded-lg border-2 bg-teal-50 px-4 py-3 min-w-[180px] shadow-sm transition-shadow ${
        selected ? "border-teal-600 shadow-md" : "border-teal-400"
      }`}
    >
      <Handle type="target" position={Position.Top} className="!bg-teal-500" />
      <div className="flex items-center gap-2">
        <Merge className="h-4 w-4 text-teal-600 shrink-0" />
        <div className="min-w-0">
          <div className="text-sm font-medium text-teal-900 truncate">
            {nodeData.displayName}
          </div>
          <div className="text-xs text-teal-500">Fan In</div>
        </div>
      </div>
      <Handle type="source" position={Position.Bottom} className="!bg-teal-500" />
    </div>
  );
}

export const FanInNode = memo(FanInNodeComponent);
