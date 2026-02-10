import { useEffect, useRef } from "react";
import { MessageBubble } from "@/components/chat/MessageBubble";
import type { ChatMessage } from "@/types/chat";
import { Loader2, MessageSquare } from "lucide-react";

interface MessageAreaProps {
  messages: ChatMessage[];
  isStreaming: boolean;
}

/**
 * 消息展示区域 — 可滚动的消息列表。
 * 新消息或流式内容更新时自动滚动到底部。
 */
export function MessageArea({ messages, isStreaming }: MessageAreaProps) {
  const bottomRef = useRef<HTMLDivElement>(null);

  // 自动滚动到底部
  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  if (messages.length === 0) {
    return (
      <div className="flex flex-1 flex-col items-center justify-center gap-3 text-muted-foreground">
        <MessageSquare className="h-12 w-12 opacity-30" />
        <p className="text-sm">选择 Agent 并发送消息开始对话</p>
      </div>
    );
  }

  return (
    <div className="flex flex-1 flex-col gap-4 overflow-y-auto p-4">
      {messages.map((msg, i) => (
        <MessageBubble key={i} message={msg} />
      ))}

      {/* 流式加载指示器 */}
      {isStreaming && (
        <div className="flex items-center gap-2 text-muted-foreground">
          <Loader2 className="h-4 w-4 animate-spin" />
          <span className="text-xs">正在生成回复…</span>
        </div>
      )}

      <div ref={bottomRef} />
    </div>
  );
}
