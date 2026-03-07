import { useCallback, useState } from "react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  CheckCircle,
  Circle,
  Loader2,
  XCircle,
  SkipForward,
  RefreshCw,
  Wrench,
} from "lucide-react";
import { retryStepExecution } from "@/lib/api/incidents";
import type {
  SopStepDefinition,
  SopStepExecution,
  StepExecutionStatus,
} from "@/types/incident";

const STATUS_CONFIG: Record<
  StepExecutionStatus,
  { icon: React.ElementType; color: string; label: string }
> = {
  Pending: { icon: Circle, color: "text-gray-400", label: "待执行" },
  Running: { icon: Loader2, color: "text-blue-500", label: "执行中" },
  Completed: { icon: CheckCircle, color: "text-green-500", label: "已完成" },
  Failed: { icon: XCircle, color: "text-red-500", label: "失败" },
  Skipped: { icon: SkipForward, color: "text-gray-400", label: "已跳过" },
};

interface Props {
  incidentId: string;
  steps: SopStepDefinition[];
  executions: SopStepExecution[];
  onRefresh: () => void;
}

export function StepExecutionPanel({
  incidentId,
  steps,
  executions,
  onRefresh,
}: Props) {
  const [retrying, setRetrying] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  const executionMap = new Map(executions.map((e) => [e.stepNumber, e]));

  const handleRetry = useCallback(
    async (stepNumber: number) => {
      setRetrying(stepNumber);
      setError(null);
      try {
        await retryStepExecution(incidentId, stepNumber);
        onRefresh();
      } catch (err) {
        setError(err instanceof Error ? err.message : "重试失败");
      } finally {
        setRetrying(null);
      }
    },
    [incidentId, onRefresh],
  );

  if (steps.length === 0) return null;

  const completedCount = executions.filter(
    (e) => e.status === "Completed",
  ).length;
  const totalSteps = steps.length;
  const progressPct = totalSteps > 0 ? (completedCount / totalSteps) * 100 : 0;

  return (
    <div className="space-y-3 rounded-md border p-3">
      <div className="flex items-center justify-between">
        <h4 className="text-xs font-semibold">SOP 步骤执行</h4>
        <span className="text-xs text-muted-foreground">
          {completedCount}/{totalSteps} 步已完成
        </span>
      </div>

      {/* Progress bar */}
      <div className="h-1.5 rounded-full bg-muted overflow-hidden">
        <div
          className="h-full rounded-full bg-green-500 transition-all"
          style={{ width: `${progressPct}%` }}
        />
      </div>

      {error && (
        <div className="rounded bg-destructive/10 p-2 text-xs text-destructive">
          {error}
        </div>
      )}

      {/* Step list */}
      <div className="space-y-1">
        {steps.map((step) => {
          const exec = executionMap.get(step.stepNumber);
          const status = exec?.status ?? "Pending";
          const config = STATUS_CONFIG[status];
          const Icon = config.icon;

          return (
            <div
              key={step.stepNumber}
              className="flex items-start gap-2 rounded-md px-2 py-1.5 hover:bg-muted/50"
            >
              <Icon
                className={`h-4 w-4 mt-0.5 shrink-0 ${config.color} ${
                  status === "Running" ? "animate-spin" : ""
                }`}
              />
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2">
                  <span className="text-xs font-medium">
                    步骤 {step.stepNumber}
                  </span>
                  <Badge variant="outline" className="text-[10px] px-1 py-0">
                    {config.label}
                  </Badge>
                  {step.stepType === "Structured" && step.toolName && (
                    <span className="flex items-center gap-0.5 text-[10px] text-muted-foreground">
                      <Wrench className="h-3 w-3" />
                      {step.toolName}
                    </span>
                  )}
                  {step.requiresApproval && (
                    <Badge
                      variant="secondary"
                      className="text-[10px] px-1 py-0"
                    >
                      需审批
                    </Badge>
                  )}
                </div>
                <p className="text-xs text-muted-foreground mt-0.5 line-clamp-2">
                  {step.description}
                </p>
                {exec?.errorMessage && (
                  <p className="text-xs text-destructive mt-0.5">
                    {exec.errorMessage}
                  </p>
                )}
                {exec && exec.retryCount > 0 && (
                  <span className="text-[10px] text-muted-foreground">
                    已重试 {exec.retryCount} 次
                  </span>
                )}
              </div>

              {/* Retry button for failed steps */}
              {status === "Failed" && (
                <Button
                  size="sm"
                  variant="ghost"
                  className="h-7 px-2"
                  disabled={retrying != null}
                  onClick={() => handleRetry(step.stepNumber)}
                >
                  {retrying === step.stepNumber ? (
                    <Loader2 className="h-3.5 w-3.5 animate-spin" />
                  ) : (
                    <RefreshCw className="h-3.5 w-3.5" />
                  )}
                </Button>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}
