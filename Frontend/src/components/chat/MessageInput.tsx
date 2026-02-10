import { useState, useCallback, type KeyboardEvent } from "react";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { Send, Square } from "lucide-react";

interface MessageInputProps {
  onSend: (content: string) => void;
  disabled?: boolean;
  isStreaming?: boolean;
  onAbort?: () => void;
}

/**
 * 消息输入框 — 文本输入 + 发送按钮。
 * Enter 发送，Shift+Enter 换行。
 * Agent 未选择或流式传输中时禁用。
 */
export function MessageInput({
  onSend,
  disabled = false,
  isStreaming = false,
  onAbort,
}: MessageInputProps) {
  const [value, setValue] = useState("");

  const isDisabled = disabled || isStreaming;
  const canSend = !isDisabled && value.trim().length > 0;

  const handleSend = useCallback(() => {
    const trimmed = value.trim();
    if (!trimmed) return;
    onSend(trimmed);
    setValue("");
  }, [value, onSend]);

  const handleKeyDown = useCallback(
    (e: KeyboardEvent<HTMLTextAreaElement>) => {
      if (e.key === "Enter" && !e.shiftKey) {
        e.preventDefault();
        if (canSend) handleSend();
      }
    },
    [canSend, handleSend],
  );

  return (
    <div className="border-t bg-background px-4 py-3">
      <div className="relative rounded-lg border bg-muted/30 focus-within:ring-2 focus-within:ring-ring focus-within:ring-offset-1">
        <Textarea
          value={value}
          onChange={(e) => setValue(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder={isStreaming ? "等待回复中…" : "输入消息，Enter 发送，Shift+Enter 换行"}
          disabled={isDisabled}
          className="min-h-[80px] max-h-[200px] resize-none border-0 bg-transparent pr-14 shadow-none focus-visible:ring-0 focus-visible:ring-offset-0"
          rows={3}
        />
        <div className="absolute bottom-2 right-2">
          {isStreaming && onAbort ? (
            <Button
              size="icon"
              variant="destructive"
              onClick={onAbort}
              className="h-8 w-8"
              title="停止生成"
            >
              <Square className="h-4 w-4" />
            </Button>
          ) : (
            <Button
              size="icon"
              onClick={handleSend}
              disabled={!canSend}
              className="h-8 w-8"
            >
              <Send className="h-4 w-4" />
            </Button>
          )}
        </div>
      </div>
    </div>
  );
}
