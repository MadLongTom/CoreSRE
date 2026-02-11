import { memo } from "react";
import { Handle, Position, type NodeProps } from "@xyflow/react";
import { Wrench } from "lucide-react";
import type { DagNodeData } from "@/types/workflow";

function ToolNodeComponent({ data, selected }: NodeProps) {
  const nodeData = data as DagNodeData;
  return (
    <div
      className={`rounded-lg border-2 bg-orange-50 px-4 py-3 min-w-[180px] shadow-sm transition-shadow ${
        selected ? "border-orange-600 shadow-md" : "border-orange-400"
      }`}
    >
      <Handle type="target" position={Position.Top} className="!bg-orange-500" />
      <div className="flex items-center gap-2">
        <Wrench className="h-4 w-4 text-orange-600 shrink-0" />
        <div className="min-w-0">
          <div className="text-sm font-medium text-orange-900 truncate">
            {nodeData.displayName}
          </div>
          {nodeData.referenceId && (
            <div className="text-xs text-orange-500 truncate">
              Tool: {nodeData.referenceId.slice(0, 8)}…
            </div>
          )}
        </div>
      </div>
      <Handle type="source" position={Position.Bottom} className="!bg-orange-500" />
    </div>
  );
}

export const ToolNode = memo(ToolNodeComponent);
