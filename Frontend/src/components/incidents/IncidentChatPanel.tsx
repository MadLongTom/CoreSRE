import type { ChatMessagePayload } from "@/types/incident";
import { Bot, User } from "lucide-react";
import { cn } from "@/lib/utils";

export function IncidentChatPanel({
  messages,
}: {
  messages: ChatMessagePayload[];
}) {
  if (messages.length === 0) {
    return (
      <div className="flex flex-1 items-center justify-center text-sm text-muted-foreground">
        等待 Agent 对话…
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-3 overflow-y-auto p-4">
      {messages.map((msg, idx) => {
        const isAssistant = msg.role === "assistant";
        return (
          <div
            key={idx}
            className={cn(
              "flex gap-3",
              isAssistant ? "items-start" : "items-start flex-row-reverse"
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
    </div>
  );
}
