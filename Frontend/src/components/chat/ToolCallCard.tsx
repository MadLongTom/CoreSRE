import { useState } from "react";
import { ChevronDown, ChevronRight, Loader2, CheckCircle2, XCircle } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import type { ToolCall } from "@/types/chat";

interface ToolCallCardProps {
  toolCall: ToolCall;
}

/**
 * 工具调用卡片 — 展示工具名称、状态徽章、可折叠的参数和结果区域。
 *
 * Status states:
 * - calling: 蓝色 + 旋转图标
 * - completed: 绿色 + 勾选图标
 * - failed: 红色 + 错误图标
 */
export function ToolCallCard({ toolCall }: ToolCallCardProps) {
  const [argsExpanded, setArgsExpanded] = useState(false);
  const [resultExpanded, setResultExpanded] = useState(
    toolCall.status === "failed",
  );

  const statusConfig = {
    calling: {
      label: "调用中...",
      variant: "default" as const,
      icon: <Loader2 className="h-3 w-3 animate-spin" />,
      className: "bg-blue-500/10 text-blue-600 border-blue-200",
    },
    completed: {
      label: "已完成",
      variant: "default" as const,
      icon: <CheckCircle2 className="h-3 w-3" />,
      className: "bg-green-500/10 text-green-600 border-green-200",
    },
    failed: {
      label: "失败",
      variant: "destructive" as const,
      icon: <XCircle className="h-3 w-3" />,
      className: "bg-red-500/10 text-red-600 border-red-200",
    },
  };

  const status = statusConfig[toolCall.status];

  const formatJson = (json?: string): string => {
    if (!json) return "";
    try {
      return JSON.stringify(JSON.parse(json), null, 2);
    } catch {
      return json;
    }
  };

  return (
    <div className="my-1 rounded-md border border-border/50 bg-muted/30 text-xs">
      {/* Header */}
      <div className="flex items-center gap-2 px-3 py-2">
        <span className="font-medium text-foreground">
          🔧 {toolCall.toolName}
        </span>
        <Badge variant={status.variant} className={`gap-1 text-[10px] px-1.5 py-0 ${status.className}`}>
          {status.icon}
          {status.label}
        </Badge>
      </div>

      {/* Args section */}
      {toolCall.args && (
        <div className="border-t border-border/30">
          <button
            type="button"
            className="flex w-full items-center gap-1 px-3 py-1.5 text-muted-foreground hover:text-foreground transition-colors"
            onClick={() => setArgsExpanded(!argsExpanded)}
          >
            {argsExpanded ? (
              <ChevronDown className="h-3 w-3" />
            ) : (
              <ChevronRight className="h-3 w-3" />
            )}
            <span>参数</span>
          </button>
          {argsExpanded && (
            <pre className="mx-3 mb-2 overflow-x-auto rounded bg-muted p-2 text-[11px] leading-relaxed">
              {formatJson(toolCall.args)}
            </pre>
          )}
        </div>
      )}

      {/* Result section */}
      {toolCall.result && (
        <div className="border-t border-border/30">
          <button
            type="button"
            className="flex w-full items-center gap-1 px-3 py-1.5 text-muted-foreground hover:text-foreground transition-colors"
            onClick={() => setResultExpanded(!resultExpanded)}
          >
            {resultExpanded ? (
              <ChevronDown className="h-3 w-3" />
            ) : (
              <ChevronRight className="h-3 w-3" />
            )}
            <span>结果</span>
          </button>
          {resultExpanded && (
            <pre className="mx-3 mb-2 overflow-x-auto rounded bg-muted p-2 text-[11px] leading-relaxed">
              {formatJson(toolCall.result)}
            </pre>
          )}
        </div>
      )}
    </div>
  );
}
