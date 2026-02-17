import { useState, useMemo } from "react";
import { ChevronDown, ChevronRight, Loader2, CheckCircle2, XCircle, Terminal } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import type { ToolCall } from "@/types/chat";

interface ToolCallCardProps {
  toolCall: ToolCall;
}

/* ---------- helpers ---------- */

const tryParseJson = (s?: string): Record<string, unknown> | null => {
  if (!s) return null;
  try { return JSON.parse(s); } catch { return null; }
};

const formatJson = (json?: string): string => {
  if (!json) return "";
  try { return JSON.stringify(JSON.parse(json), null, 2); } catch { return json; }
};

/* ---------- Terminal-style card for run_command ---------- */

function RunCommandCard({ toolCall }: ToolCallCardProps) {
  const [resultExpanded, setResultExpanded] = useState(false);

  const args = useMemo(() => tryParseJson(toolCall.args), [toolCall.args]);
  const result = useMemo(() => tryParseJson(toolCall.result), [toolCall.result]);

  const command = (args?.command as string) ?? "";
  const workDir = (args?.working_directory as string) ?? "/workspace";
  const exitCode = result?.exitCode as number | undefined;
  const stdout = (result?.stdout as string) ?? "";
  const stderr = (result?.stderr as string) ?? "";
  const errorMsg = (result?.error as string) ?? "";

  const isCalling = toolCall.status === "calling";
  const isSuccess = toolCall.status === "completed" && (exitCode === 0 || exitCode === undefined);

  return (
    <div className="my-1 overflow-hidden rounded-lg border border-zinc-700 bg-zinc-900 text-xs font-mono shadow-md">
      {/* Title bar */}
      <div className="flex items-center gap-2 bg-zinc-800 px-3 py-1.5 select-none">
        <Terminal className="h-3.5 w-3.5 text-green-400" />
        <span className="text-[11px] text-zinc-300 font-medium">Terminal</span>
        {isCalling ? (
          <Badge className="ml-auto gap-1 text-[10px] px-1.5 py-0 bg-blue-500/20 text-blue-400 border-blue-500/30">
            <Loader2 className="h-3 w-3 animate-spin" /> 运行中...
          </Badge>
        ) : exitCode !== undefined ? (
          <Badge className={`ml-auto gap-1 text-[10px] px-1.5 py-0 ${
            isSuccess
              ? "bg-green-500/20 text-green-400 border-green-500/30"
              : "bg-red-500/20 text-red-400 border-red-500/30"
          }`}>
            {isSuccess ? <CheckCircle2 className="h-3 w-3" /> : <XCircle className="h-3 w-3" />}
            exit {exitCode}
          </Badge>
        ) : toolCall.status === "failed" ? (
          <Badge className="ml-auto gap-1 text-[10px] px-1.5 py-0 bg-red-500/20 text-red-400 border-red-500/30">
            <XCircle className="h-3 w-3" /> 失败
          </Badge>
        ) : null}
      </div>

      {/* Prompt line */}
      <div className="px-3 py-2 space-y-0.5">
        <div className="flex items-start gap-1.5 leading-relaxed">
          <span className="text-green-400 shrink-0">$</span>
          <span className="text-zinc-100 whitespace-pre-wrap break-all">{command}</span>
        </div>
        {workDir !== "/workspace" && (
          <div className="text-zinc-500 text-[10px]">wd: {workDir}</div>
        )}
      </div>

      {/* Output */}
      {toolCall.result && (
        <div className="border-t border-zinc-700/60">
          <button
            type="button"
            className="flex w-full items-center gap-1 px-3 py-1 text-zinc-400 hover:text-zinc-200 transition-colors"
            onClick={() => setResultExpanded(!resultExpanded)}
          >
            {resultExpanded ? <ChevronDown className="h-3 w-3" /> : <ChevronRight className="h-3 w-3" />}
            <span>输出</span>
          </button>
          {resultExpanded && (
            <div className="px-3 pb-2 space-y-1">
              {stdout && (
                <pre className="whitespace-pre-wrap break-all text-[11px] leading-relaxed text-zinc-200 max-h-64 overflow-y-auto">
                  {stdout}
                </pre>
              )}
              {stderr && (
                <pre className="whitespace-pre-wrap break-all text-[11px] leading-relaxed text-red-400 max-h-40 overflow-y-auto">
                  {stderr}
                </pre>
              )}
              {errorMsg && (
                <pre className="whitespace-pre-wrap break-all text-[11px] leading-relaxed text-red-400">
                  {errorMsg}
                </pre>
              )}
              {!stdout && !stderr && !errorMsg && (
                <span className="text-zinc-500 italic">(无输出)</span>
              )}
            </div>
          )}
        </div>
      )}

      {/* Spinner line when calling */}
      {isCalling && !toolCall.result && (
        <div className="flex items-center gap-2 px-3 py-2 border-t border-zinc-700/60 text-zinc-400">
          <Loader2 className="h-3 w-3 animate-spin" />
          <span className="animate-pulse">等待输出...</span>
        </div>
      )}
    </div>
  );
}

/* ---------- Generic tool call card ---------- */
function GenericToolCallCard({ toolCall }: ToolCallCardProps) {
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

/* ---------- Dispatcher ---------- */

const TERMINAL_TOOLS = new Set(["run_command", "execute_code"]);

export function ToolCallCard({ toolCall }: ToolCallCardProps) {
  if (TERMINAL_TOOLS.has(toolCall.toolName)) {
    return <RunCommandCard toolCall={toolCall} />;
  }
  return <GenericToolCallCard toolCall={toolCall} />;
}