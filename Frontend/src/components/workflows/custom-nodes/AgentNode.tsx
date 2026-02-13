import { memo } from "react";
import { Position, type NodeProps } from "@xyflow/react";
import { Bot } from "lucide-react";
import type { DagNodeData } from "@/types/workflow";
import { PortHandles } from "./PortHandles";

function AgentNodeComponent({ data, selected }: NodeProps) {
  const nodeData = data as DagNodeData;
  return (
    <div
      className={`rounded-lg border-2 bg-blue-50 px-4 py-3 min-w-[180px] shadow-sm transition-shadow ${
        selected ? "border-blue-600 shadow-md" : "border-blue-400"
      }`}
    >
      <PortHandles type="target" count={nodeData.inputCount} color="blue-500" position={Position.Top} />
      <div className="flex items-center gap-2">
        <Bot className="h-4 w-4 text-blue-600 shrink-0" />
        <div className="min-w-0">
          <div className="text-sm font-medium text-blue-900 truncate">
            {nodeData.displayName}
          </div>
          {nodeData.referenceId && (
            <div className="text-xs text-blue-500 truncate">
              Agent: {nodeData.referenceId.slice(0, 8)}…
            </div>
          )}
        </div>
      </div>
      <PortHandles type="source" count={nodeData.outputCount} color="blue-500" position={Position.Bottom} />
    </div>
  );
}

export const AgentNode = memo(AgentNodeComponent);
