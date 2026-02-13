import { memo } from "react";
import { Position, type NodeProps } from "@xyflow/react";
import { Merge } from "lucide-react";
import type { DagNodeData } from "@/types/workflow";
import { PortHandles } from "./PortHandles";

function FanInNodeComponent({ data, selected }: NodeProps) {
  const nodeData = data as DagNodeData;
  return (
    <div
      className={`rounded-lg border-2 bg-teal-50 px-4 py-3 min-w-[180px] shadow-sm transition-shadow ${
        selected ? "border-teal-600 shadow-md" : "border-teal-400"
      }`}
    >
      <PortHandles type="target" count={nodeData.inputCount} color="teal-500" position={Position.Top} />
      <div className="flex items-center gap-2">
        <Merge className="h-4 w-4 text-teal-600 shrink-0" />
        <div className="min-w-0">
          <div className="text-sm font-medium text-teal-900 truncate">
            {nodeData.displayName}
          </div>
          <div className="text-xs text-teal-500">
            Fan In{nodeData.inputCount > 1 ? ` · ${nodeData.inputCount} 端口` : ""}
          </div>
        </div>
      </div>
      <PortHandles type="source" count={nodeData.outputCount} color="teal-500" position={Position.Bottom} />
    </div>
  );
}

export const FanInNode = memo(FanInNodeComponent);
