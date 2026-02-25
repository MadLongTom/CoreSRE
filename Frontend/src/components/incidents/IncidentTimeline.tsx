import type { IncidentTimelineItem } from "@/types/incident";
import {
  AlertCircle,
  ArrowRightLeft,
  Bot,
  CheckCircle,
  Clock,
  FileText,
  MessageSquare,
  Settings,
  Wrench,
  Zap,
} from "lucide-react";
import { cn } from "@/lib/utils";

const EVENT_ICONS: Record<string, React.ElementType> = {
  AlertReceived: AlertCircle,
  StatusChanged: ArrowRightLeft,
  AgentMessage: Bot,
  ToolInvoked: Wrench,
  SopStepCompleted: CheckCircle,
  RcaCompleted: Settings,
  SopGenerated: FileText,
  Resolved: CheckCircle,
  Escalated: Zap,
  ManualNote: MessageSquare,
  Timeout: Clock,
};

const EVENT_COLORS: Record<string, string> = {
  AlertReceived: "text-red-500",
  StatusChanged: "text-blue-500",
  AgentMessage: "text-purple-500",
  ToolInvoked: "text-orange-500",
  SopStepCompleted: "text-green-500",
  RcaCompleted: "text-emerald-600",
  SopGenerated: "text-teal-500",
  Resolved: "text-green-600",
  Escalated: "text-red-600",
  ManualNote: "text-gray-500",
  Timeout: "text-yellow-600",
};

export function IncidentTimeline({
  items,
}: {
  items: IncidentTimelineItem[];
}) {
  if (items.length === 0) {
    return (
      <div className="flex items-center justify-center py-8 text-sm text-muted-foreground">
        暂无时间线事件
      </div>
    );
  }

  const sorted = [...items].sort(
    (a, b) => new Date(b.timestamp).getTime() - new Date(a.timestamp).getTime()
  );

  return (
    <div className="space-y-0">
      {sorted.map((item, idx) => {
        const Icon = EVENT_ICONS[item.eventType] ?? AlertCircle;
        const color = EVENT_COLORS[item.eventType] ?? "text-gray-400";
        const time = new Date(item.timestamp);

        return (
          <div key={idx} className="flex gap-3 py-3">
            <div className="flex flex-col items-center">
              <div className={cn("rounded-full p-1", color)}>
                <Icon className="h-4 w-4" />
              </div>
              {idx < sorted.length - 1 && (
                <div className="mt-1 flex-1 border-l border-border" />
              )}
            </div>
            <div className="min-w-0 flex-1 pb-1">
              <div className="flex items-baseline gap-2">
                <span className="text-xs font-medium">{item.eventType}</span>
                <span className="text-xs text-muted-foreground">
                  {time.toLocaleTimeString()}
                </span>
              </div>
              <p className="mt-0.5 text-sm text-muted-foreground">
                {item.summary}
              </p>
            </div>
          </div>
        );
      })}
    </div>
  );
}
