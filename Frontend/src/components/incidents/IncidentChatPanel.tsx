import { useCallback, useEffect, useRef, useState } from "react";
import type {
  ChatMessagePayload,
  InterventionRequestPayload,
  InterventionResponseBody,
} from "@/types/incident";
import {
  Bot,
  User,
  Send,
  Loader2,
  ShieldCheck,
  ShieldX,
  Wrench,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { Badge } from "@/components/ui/badge";

interface IncidentChatPanelProps {
  messages: ChatMessagePayload[];
  incidentId: string;
  isAgentProcessing: boolean;
  agentName?: string | null;
  isResolved: boolean;
  pendingRequests: InterventionRequestPayload[];
}

export function IncidentChatPanel({
  messages,
  incidentId,
  isAgentProcessing,
  agentName,
  isResolved,
  pendingRequests,
}: IncidentChatPanelProps) {
  const [inputValue, setInputValue] = useState("");
  const [sending, setSending] = useState(false);
  const [respondingIds, setRespondingIds] = useState<Set<string>>(new Set());
  const scrollRef = useRef<HTMLDivElement>(null);

  // Auto-scroll to bottom on new messages or new pending requests
  useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [messages, pendingRequests]);

  // ── Proactive human message ──
  const handleSend = useCallback(async () => {
    const trimmed = inputValue.trim();
    if (!trimmed || sending) return;

    setSending(true);
    try {
      const resp = await fetch(`/api/incidents/${incidentId}/intervene`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ message: trimmed }),
      });
      if (resp.ok) {
        setInputValue("");
      }
    } catch {
      // Error handled silently
    } finally {
      setSending(false);
    }
  }, [incidentId, inputValue, sending]);

  // ── Respond to structured intervention request ──
  const handleRespond = useCallback(
    async (requestId: string, body: InterventionResponseBody) => {
      setRespondingIds((prev) => new Set(prev).add(requestId));
      try {
        await fetch(
          `/api/incidents/${incidentId}/interventions/${requestId}/respond`,
          {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(body),
          }
        );
      } catch {
        // Error handled silently
      } finally {
        setRespondingIds((prev) => {
          const next = new Set(prev);
          next.delete(requestId);
          return next;
        });
      }
    },
    [incidentId]
  );

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  };

  const canIntervene = isAgentProcessing && !isResolved;

  return (
    <div className="flex h-full flex-col">
      {/* Messages area */}
      <div ref={scrollRef} className="flex-1 overflow-y-auto">
        {messages.length === 0 && !isAgentProcessing ? (
          <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
            等待 Agent 对话…
          </div>
        ) : (
          <div className="flex flex-col gap-3 p-4">
            {messages.map((msg, idx) => {
              const isAssistant = msg.role === "assistant";
              return (
                <div
                  key={idx}
                  className={cn(
                    "flex gap-3",
                    isAssistant
                      ? "items-start"
                      : "items-start flex-row-reverse"
                  )}
                >
                  <div
                    className={cn(
                      "flex h-7 w-7 shrink-0 items-center justify-center rounded-full",
                      isAssistant
                        ? "bg-purple-100 text-purple-600"
                        : "bg-blue-100 text-blue-600"
                    )}
                  >
                    {isAssistant ? (
                      <Bot className="h-4 w-4" />
                    ) : (
                      <User className="h-4 w-4" />
                    )}
                  </div>
                  <div
                    className={cn(
                      "max-w-[80%] rounded-lg px-3 py-2 text-sm",
                      isAssistant
                        ? "bg-muted"
                        : "bg-primary text-primary-foreground"
                    )}
                  >
                    {msg.agentName && isAssistant && (
                      <div className="mb-1 text-xs font-medium text-muted-foreground">
                        {msg.agentName}
                      </div>
                    )}
                    <div className="whitespace-pre-wrap">{msg.content}</div>
                  </div>
                </div>
              );
            })}

            {/* ── Pending Intervention Requests (Feature A/B) ── */}
            {pendingRequests.map((req) => (
              <ToolApprovalCard
                key={req.requestId}
                request={req}
                isResponding={respondingIds.has(req.requestId)}
                onRespond={(body) => handleRespond(req.requestId, body)}
              />
            ))}

            {/* Streaming indicator */}
            {isAgentProcessing && pendingRequests.length === 0 && (
              <div className="flex items-center gap-2 px-2 py-1">
                <Loader2 className="h-4 w-4 animate-spin text-purple-500" />
                <span className="text-xs text-muted-foreground">
                  {agentName ? `${agentName} 正在处理…` : "Agent 正在处理…"}
                </span>
              </div>
            )}

            {/* Paused indicator when there are pending requests */}
            {isAgentProcessing && pendingRequests.length > 0 && (
              <div className="flex items-center gap-2 px-2 py-1">
                <ShieldCheck className="h-4 w-4 text-amber-500" />
                <span className="text-xs text-amber-600 font-medium">
                  Agent 已暂停 — 等待人工审批
                </span>
              </div>
            )}
          </div>
        )}
      </div>

      {/* Human intervention input */}
      {canIntervene && (
        <div className="border-t p-3">
          <div className="mb-1.5 flex items-center gap-1.5">
            <User className="h-3.5 w-3.5 text-blue-500" />
            <span className="text-xs font-medium text-muted-foreground">
              人工介入 — 向 Agent 发送指令
            </span>
          </div>
          <div className="flex gap-2">
            <Textarea
              value={inputValue}
              onChange={(e) => setInputValue(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder="输入消息干预 Agent 对话…"
              className="min-h-[40px] max-h-[120px] resize-none text-sm"
              disabled={sending}
            />
            <Button
              size="icon"
              variant="default"
              onClick={handleSend}
              disabled={!inputValue.trim() || sending}
              className="h-10 w-10 shrink-0"
            >
              {sending ? (
                <Loader2 className="h-4 w-4 animate-spin" />
              ) : (
                <Send className="h-4 w-4" />
              )}
            </Button>
          </div>
        </div>
      )}

      {/* Resolved state */}
      {isResolved && messages.length > 0 && (
        <div className="border-t px-4 py-2 text-center text-xs text-muted-foreground">
          对话已结束 — 事故已解决
        </div>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------
// ToolApprovalCard — renders a tool approval request with approve/reject buttons
// ---------------------------------------------------------------------------
function ToolApprovalCard({
  request,
  isResponding,
  onRespond,
}: {
  request: InterventionRequestPayload;
  isResponding: boolean;
  onRespond: (body: InterventionResponseBody) => void;
}) {
  if (request.type === "ToolApproval") {
    return (
      <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 dark:border-amber-800 dark:bg-amber-950/30">
        <div className="mb-2 flex items-center gap-2">
          <Wrench className="h-4 w-4 text-amber-600" />
          <span className="text-sm font-medium text-amber-800 dark:text-amber-200">
            工具审批请求
          </span>
          <Badge variant="outline" className="text-xs">
            {request.toolName}
          </Badge>
        </div>
        <p className="mb-2 text-sm text-muted-foreground">{request.prompt}</p>
        {request.toolArguments && (
          <pre className="mb-3 max-h-32 overflow-auto rounded bg-muted p-2 text-xs">
            {JSON.stringify(request.toolArguments, null, 2)}
          </pre>
        )}
        <div className="flex gap-2">
          <Button
            size="sm"
            variant="default"
            className="gap-1.5"
            disabled={isResponding}
            onClick={() =>
              onRespond({ responseType: "Approved", approved: true })
            }
          >
            {isResponding ? (
              <Loader2 className="h-3.5 w-3.5 animate-spin" />
            ) : (
              <ShieldCheck className="h-3.5 w-3.5" />
            )}
            批准
          </Button>
          <Button
            size="sm"
            variant="destructive"
            className="gap-1.5"
            disabled={isResponding}
            onClick={() =>
              onRespond({ responseType: "Rejected", approved: false })
            }
          >
            <ShieldX className="h-3.5 w-3.5" />
            拒绝
          </Button>
        </div>
      </div>
    );
  }

  // FreeTextInput / Choice — generic card
  const [textValue, setTextValue] = useState("");

  return (
    <div className="rounded-lg border border-blue-200 bg-blue-50 p-3 dark:border-blue-800 dark:bg-blue-950/30">
      <div className="mb-2 flex items-center gap-2">
        <User className="h-4 w-4 text-blue-600" />
        <span className="text-sm font-medium text-blue-800 dark:text-blue-200">
          Agent 请求输入
        </span>
      </div>
      <p className="mb-2 text-sm text-muted-foreground">{request.prompt}</p>

      {request.type === "Choice" && request.choices ? (
        <div className="flex flex-wrap gap-2">
          {request.choices.map((choice) => (
            <Button
              key={choice}
              size="sm"
              variant="outline"
              disabled={isResponding}
              onClick={() =>
                onRespond({ responseType: "ChoiceSelected", content: choice })
              }
            >
              {choice}
            </Button>
          ))}
        </div>
      ) : (
        <div className="flex gap-2">
          <Textarea
            value={textValue}
            onChange={(e) => setTextValue(e.target.value)}
            placeholder="输入回复…"
            className="min-h-[40px] max-h-[80px] resize-none text-sm"
            disabled={isResponding}
          />
          <Button
            size="icon"
            disabled={!textValue.trim() || isResponding}
            onClick={() =>
              onRespond({ responseType: "TextInput", content: textValue })
            }
            className="h-10 w-10 shrink-0"
          >
            {isResponding ? (
              <Loader2 className="h-4 w-4 animate-spin" />
            ) : (
              <Send className="h-4 w-4" />
            )}
          </Button>
        </div>
      )}
    </div>
  );
}
