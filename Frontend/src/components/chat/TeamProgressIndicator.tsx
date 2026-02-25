import type { TeamProgress } from "@/types/chat";
import { Loader2 } from "lucide-react";

interface TeamProgressIndicatorProps {
  progress: TeamProgress;
}

/**
 * TeamProgressIndicator — 显示当前正在执行的 Team 参与者 Agent 状态。
 * 例如: "Agent 2/3: LogAnalyzer is thinking..."
 * 在所有 Team 对话流式响应期间，显示在消息输入区域上方。
 */
export function TeamProgressIndicator({ progress }: TeamProgressIndicatorProps) {
  const stepLabel =
    progress.step != null && progress.totalSteps != null
      ? `${progress.step}/${progress.totalSteps}`
      : progress.step != null
        ? `#${progress.step}`
        : null;

  return (
    <div className="flex items-center gap-2 border-t bg-muted/40 px-4 py-2 text-sm text-muted-foreground">
      <Loader2 className="h-3.5 w-3.5 animate-spin text-purple-500" />
      {stepLabel && (
        <span className="rounded bg-purple-100 px-1.5 py-0.5 text-xs font-medium text-purple-700 dark:bg-purple-900/40 dark:text-purple-300">
          Step {stepLabel}
        </span>
      )}
      <span>
        <span className="font-medium text-foreground">{progress.currentAgentName}</span>
        {" is thinking…"}
      </span>
    </div>
  );
}
