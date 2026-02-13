import { memo } from "react";
import { Position, type NodeProps } from "@xyflow/react";
import { GitBranch } from "lucide-react";
import type { DagNodeData } from "@/types/workflow";
import { PortHandles } from "./PortHandles";

function ConditionNodeComponent({ data, selected }: NodeProps) {
  const nodeData = data as DagNodeData;
  const outputCount = Math.max(nodeData.outputCount, 2); // Condition 至少 2 个输出端口
  return (
    <div
      className={`rounded-lg border-2 bg-purple-50 px-4 py-3 min-w-[180px] shadow-sm transition-shadow ${
        selected ? "border-purple-600 shadow-md" : "border-purple-400"
      }`}
    >
      <PortHandles type="target" count={nodeData.inputCount} color="purple-500" position={Position.Top} />
      <div className="flex items-center gap-2">
        <GitBranch className="h-4 w-4 text-purple-600 shrink-0" />
        <div className="min-w-0">
          <div className="text-sm font-medium text-purple-900 truncate">
            {nodeData.displayName}
          </div>
          <div className="text-xs text-purple-500">
            Condition · {outputCount} 端口
          </div>
        </div>
      </div>
      <PortHandles type="source" count={outputCount} color="purple-500" position={Position.Bottom} />
    </div>
  );
}

export const ConditionNode = memo(ConditionNodeComponent);
