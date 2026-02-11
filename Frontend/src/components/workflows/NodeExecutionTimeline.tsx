import { Clock, CheckCircle2, XCircle, MinusCircle, Loader2 } from "lucide-react";
import type { NodeExecution, NodeExecutionStatus } from "@/types/workflow";

const statusIcon: Record<NodeExecutionStatus, React.ReactNode> = {
  Pending: <Clock className="h-4 w-4 text-gray-400" />,
  Running: <Loader2 className="h-4 w-4 text-blue-500 animate-spin" />,
  Completed: <CheckCircle2 className="h-4 w-4 text-green-500" />,
  Failed: <XCircle className="h-4 w-4 text-red-500" />,
  Skipped: <MinusCircle className="h-4 w-4 text-amber-500" />,
};

interface NodeExecutionTimelineProps {
  nodeExecutions: NodeExecution[];
  onSelect?: (nodeId: string) => void;
  selectedNodeId?: string | null;
}

export function NodeExecutionTimeline({
  nodeExecutions,
  onSelect,
  selectedNodeId,
}: NodeExecutionTimelineProps) {
  if (nodeExecutions.length === 0) {
    return <p className="text-sm text-muted-foreground">暂无节点执行记录</p>;
  }

  // Sort by startedAt ascending
  const sorted = [...nodeExecutions].sort(
    (a, b) =>
      new Date(a.startedAt ?? 0).getTime() - new Date(b.startedAt ?? 0).getTime(),
  );

  return (
    <div className="space-y-1">
      {sorted.map((ne, idx) => {
        const isSelected = selectedNodeId === ne.nodeId;
        const duration =
          ne.startedAt && ne.completedAt
            ? Math.round(
                (new Date(ne.completedAt).getTime() -
                  new Date(ne.startedAt).getTime()) /
                  1000,
              )
            : null;

        return (
          <button
            key={ne.nodeId}
            type="button"
            className={`flex w-full items-center gap-3 rounded-md px-3 py-2 text-left text-sm transition-colors hover:bg-accent ${isSelected ? "bg-accent" : ""}`}
            onClick={() => onSelect?.(ne.nodeId)}
          >
            {/* Timeline connector */}
            <div className="flex flex-col items-center">
              {statusIcon[ne.status]}
              {idx < sorted.length - 1 && (
                <div className="mt-1 h-4 w-px bg-border" />
              )}
            </div>

            {/* Content */}
            <div className="flex-1 min-w-0">
              <p className="font-medium truncate">{ne.nodeId}</p>
              <p className="text-xs text-muted-foreground">
                {ne.status}
                {duration !== null && ` · ${duration}s`}
              </p>
              {ne.errorMessage && (
                <p className="text-xs text-destructive truncate mt-0.5">
                  {ne.errorMessage}
                </p>
              )}
            </div>
          </button>
        );
      })}
    </div>
  );
}
