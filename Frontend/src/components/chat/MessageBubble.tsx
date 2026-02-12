import { useState } from "react";
import { cn } from "@/lib/utils";
import type { ChatMessage } from "@/types/chat";
import { Bot, User, Brain } from "lucide-react";
import ReactMarkdown from "react-markdown";
import { ToolCallCard } from "@/components/chat/ToolCallCard";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";

interface MessageBubbleProps {
  message: ChatMessage;
}

/**
 * 单条消息气泡 — 根据角色显示不同样式。
 * user: 右对齐蓝色背景，底部可显示“已使用记忆”提示
 * assistant: 左对齐灰色背景，可包含工具调用卡片
 */
export function MessageBubble({ message }: MessageBubbleProps) {
  const isUser = message.role === "user";
  const hasToolCalls = !isUser && message.toolCalls && message.toolCalls.length > 0;
  const hasMemory = isUser && !!message.memoryContext;

  return (
    <div
      className={cn(
        "flex gap-3",
        isUser ? "flex-row-reverse" : "flex-row",
      )}
    >
      {/* Avatar */}
      <div
        className={cn(
          "flex h-8 w-8 shrink-0 items-center justify-center rounded-full",
          isUser
            ? "bg-primary text-primary-foreground"
            : "bg-muted text-muted-foreground",
        )}
      >
        {isUser ? <User className="h-4 w-4" /> : <Bot className="h-4 w-4" />}
      </div>

      {/* Content */}
      <div className="max-w-[75%] space-y-1">
        {/* Tool call cards (before text content) */}
        {hasToolCalls &&
          message.toolCalls!.map((tc) => (
            <ToolCallCard key={tc.toolCallId} toolCall={tc} />
          ))}

        {/* Text bubble — only render if there's text content */}
        {message.content && (
          <div
            className={cn(
              "rounded-lg px-4 py-2 text-sm leading-relaxed",
              isUser
                ? "bg-primary text-primary-foreground whitespace-pre-wrap"
                : "bg-muted text-foreground",
            )}
          >
            {isUser ? (
              message.content
            ) : (
              <ReactMarkdown
                components={{
                  p: ({ children }) => <p className="mb-2 last:mb-0">{children}</p>,
                  ul: ({ children }) => <ul className="mb-2 list-disc pl-4 last:mb-0">{children}</ul>,
                  ol: ({ children }) => <ol className="mb-2 list-decimal pl-4 last:mb-0">{children}</ol>,
                  li: ({ children }) => <li className="mb-0.5">{children}</li>,
                  code: ({ className, children, ...props }) => {
                    const isInline = !className;
                    return isInline ? (
                      <code className="rounded bg-black/10 px-1 py-0.5 text-[13px] font-mono" {...props}>{children}</code>
                    ) : (
                      <code className={cn("block overflow-x-auto rounded bg-black/10 p-2 text-[13px] font-mono my-1", className)} {...props}>{children}</code>
                    );
                  },
                  pre: ({ children }) => <pre className="overflow-x-auto rounded bg-black/10 p-2 my-2">{children}</pre>,
                  h1: ({ children }) => <h1 className="text-lg font-bold mb-1">{children}</h1>,
                  h2: ({ children }) => <h2 className="text-base font-bold mb-1">{children}</h2>,
                  h3: ({ children }) => <h3 className="text-sm font-bold mb-1">{children}</h3>,
                  a: ({ href, children }) => <a href={href} className="underline text-blue-600" target="_blank" rel="noopener noreferrer">{children}</a>,
                  blockquote: ({ children }) => <blockquote className="border-l-2 border-muted-foreground/30 pl-3 my-2 italic">{children}</blockquote>,
                  table: ({ children }) => <div className="overflow-x-auto my-2"><table className="text-xs border-collapse">{children}</table></div>,
                  th: ({ children }) => <th className="border border-border px-2 py-1 bg-muted font-semibold text-left">{children}</th>,
                  td: ({ children }) => <td className="border border-border px-2 py-1">{children}</td>,
                  hr: () => <hr className="my-2 border-border" />,
                }}
              >
                {message.content}
              </ReactMarkdown>
            )}
          </div>
        )}

        {/* Memory context hint — below user bubble */}
        {hasMemory && <MemoryHint memoryContext={message.memoryContext!} />}
      </div>
    </div>
  );
}

/**
 * 记忆提示组件 — 显示在用户消息下方，点击弹窗查看完整记忆内容。
 */
function MemoryHint({ memoryContext }: { memoryContext: string }) {
  const [open, setOpen] = useState(false);

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <button
          type="button"
          className="flex items-center gap-1 text-xs text-violet-500 hover:text-violet-600 dark:text-violet-400 dark:hover:text-violet-300 transition-colors mt-0.5 ml-auto"
        >
          <Brain className="h-3 w-3" />
          <span>已使用记忆</span>
        </button>
      </DialogTrigger>
      <DialogContent className="max-w-xl max-h-[70vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <Brain className="h-4 w-4 text-violet-500" />
            语义记忆上下文
          </DialogTitle>
        </DialogHeader>
        <div className="text-sm text-muted-foreground whitespace-pre-wrap leading-relaxed border rounded-md p-3 bg-muted/30">
          {memoryContext}
        </div>
      </DialogContent>
    </Dialog>
  );
}
