import { Play, Bot, Wrench, GitBranch, Split, Merge } from "lucide-react";
import type { WorkflowNodeType } from "@/types/workflow";

const nodeTypeConfig: {
  type: WorkflowNodeType;
  label: string;
  icon: typeof Bot;
  color: string;
}[] = [
  { type: "Start", label: "Start", icon: Play, color: "text-green-600 border-green-300 bg-green-50" },
  { type: "Agent", label: "Agent", icon: Bot, color: "text-blue-600 border-blue-300 bg-blue-50" },
  { type: "Tool", label: "Tool", icon: Wrench, color: "text-orange-600 border-orange-300 bg-orange-50" },
  { type: "Condition", label: "Condition", icon: GitBranch, color: "text-purple-600 border-purple-300 bg-purple-50" },
  { type: "FanOut", label: "Fan Out", icon: Split, color: "text-teal-600 border-teal-300 bg-teal-50" },
  { type: "FanIn", label: "Fan In", icon: Merge, color: "text-teal-600 border-teal-300 bg-teal-50" },
];

interface NodePanelProps {
  className?: string;
}

export function NodePanel({ className }: NodePanelProps) {
  const onDragStart = (
    event: React.DragEvent,
    nodeType: WorkflowNodeType,
  ) => {
    event.dataTransfer.setData("application/reactflow-nodetype", nodeType);
    event.dataTransfer.effectAllowed = "move";
  };

  return (
    <div className={`space-y-2 ${className ?? ""}`}>
      <h3 className="text-sm font-medium text-muted-foreground mb-3">
        节点类型
      </h3>
      {nodeTypeConfig.map(({ type, label, icon: Icon, color }) => (
        <div
          key={type}
          draggable
          onDragStart={(e) => onDragStart(e, type)}
          className={`flex items-center gap-2 rounded-md border px-3 py-2 text-sm cursor-grab active:cursor-grabbing select-none ${color}`}
        >
          <Icon className="h-4 w-4 shrink-0" />
          {label}
        </div>
      ))}
    </div>
  );
}
