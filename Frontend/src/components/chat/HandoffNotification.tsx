import type { TeamHandoff } from "@/types/chat";
import { ArrowRight } from "lucide-react";

interface HandoffNotificationProps {
  handoff: TeamHandoff;
}

/**
 * Handoff 通知 — 在 Handoffs 模式下展示 Agent 交接。
 * 显示为消息流中的系统通知。
 */
export function HandoffNotification({ handoff }: HandoffNotificationProps) {
  return (
    <div className="flex items-center justify-center gap-2 py-2">
      <div className="flex items-center gap-1.5 rounded-full bg-amber-50 px-3 py-1.5 text-xs text-amber-700 dark:bg-amber-900/30 dark:text-amber-300 ring-1 ring-inset ring-amber-200 dark:ring-amber-700">
        <span className="text-sm">🔀</span>
        <span className="font-medium">{handoff.fromAgentName ?? "Unknown"}</span>
        <ArrowRight className="h-3 w-3" />
        <span className="font-medium">{handoff.toAgentName ?? "Unknown"}</span>
      </div>
    </div>
  );
}
