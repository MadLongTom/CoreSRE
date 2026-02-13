import { memo } from "react";
import { Position, type NodeProps } from "@xyflow/react";
import { Play } from "lucide-react";
import type { DagNodeData } from "@/types/workflow";
import { PortHandles } from "./PortHandles";

function StartNodeComponent({ data, selected }: NodeProps) {
  const nodeData = data as DagNodeData;
  return (
    <div
      className={`rounded-lg border-2 bg-green-50 px-4 py-3 min-w-[180px] shadow-sm transition-shadow ${
        selected ? "border-green-600 shadow-md" : "border-green-400"
      }`}
    >
      <div className="flex items-center gap-2">
        <Play className="h-4 w-4 text-green-600 shrink-0" />
        <div className="min-w-0">
          <div className="text-sm font-medium text-green-900 truncate">
            {nodeData.displayName}
          </div>
          <div className="text-xs text-green-500">开始</div>
        </div>
      </div>
      <PortHandles type="source" count={nodeData.outputCount} color="green-500" position={Position.Bottom} />
    </div>
  );
}

export const StartNode = memo(StartNodeComponent);
