import { useState, useCallback, useRef } from "react";
import type { ChatMessage, ToolCall } from "@/types/chat";

interface UseAgentChatOptions {
  /** Agent registration ID */
  agentId: string | null;
  /** Conversation ID (set after creation) */
  conversationId: string | null;
}

interface UseAgentChatReturn {
  messages: ChatMessage[];
  isStreaming: boolean;
  error: string | null;
  /** Send a user message and get streaming response.
   *  @param content - The message text.
   *  @param overrideThreadId - Explicit thread/conversation ID to use (bypasses hook state to avoid React batching race). */
  sendMessage: (content: string, overrideThreadId?: string) => Promise<void>;
  /** Abort an in-flight streaming response */
  abortRun: () => void;
  /** Reset hook state (for new conversation) */
  reset: () => void;
  /** Set initial messages (for loading history) */
  setMessages: (messages: ChatMessage[]) => void;
}

/**
 * useAgentChat — 核心 AG-UI 流式对话 hook。
 *
 * 直接使用 fetch + ReadableStream 消费 SSE 事件流。
 * 事件格式：data: {"type":"TEXT_MESSAGE_CONTENT","messageId":"...","delta":"..."}\n\n
 *
 * 生命周期：
 * 1. sendMessage → POST /api/chat/stream (SSE)
 * 2. RUN_STARTED → TEXT_MESSAGE_START → TEXT_MESSAGE_CONTENT* → TEXT_MESSAGE_END → RUN_FINISHED
 * 3. 逐 token 更新 assistant 消息
 */
export function useAgentChat({
  agentId,
  conversationId,
}: UseAgentChatOptions): UseAgentChatReturn {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [isStreaming, setIsStreaming] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const abortControllerRef = useRef<AbortController | null>(null);

  const sendMessage = useCallback(
    async (content: string, overrideThreadId?: string) => {
      if (!agentId || isStreaming) return;

      setError(null);

      // Add user message
      const userMessage: ChatMessage = {
        index: messages.length,
        role: "user",
        content,
      };

      const updatedMessages = [...messages, userMessage];
      setMessages(updatedMessages);

      // Prepare assistant message placeholder
      const assistantMessage: ChatMessage = {
        index: updatedMessages.length,
        role: "assistant",
        content: "",
      };

      setMessages([...updatedMessages, assistantMessage]);
      setIsStreaming(true);

      // Prepare request
      const controller = new AbortController();
      abortControllerRef.current = controller;

      try {
        // Use explicit overrideThreadId to avoid React state batching race
        const threadId = overrideThreadId ?? conversationId ?? crypto.randomUUID();

        const res = await fetch("/api/chat/stream", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            threadId,
            runId: crypto.randomUUID(),
            messages: updatedMessages.map((m) => ({
              role: m.role,
              content: m.content,
            })),
            context: [{ description: "agentId", value: agentId }],
          }),
          signal: controller.signal,
        });

        if (!res.ok) {
          const errBody = await res.text();
          throw new Error(errBody || `HTTP ${res.status}`);
        }

        // Read SSE stream
        const reader = res.body?.getReader();
        if (!reader) throw new Error("No response body");

        const decoder = new TextDecoder();
        let buffer = "";
        let accumulatedContent = "";

        while (true) {
          const { done, value } = await reader.read();
          if (done) break;

          buffer += decoder.decode(value, { stream: true });

          // Parse SSE events (lines starting with "data: ")
          const lines = buffer.split("\n");
          buffer = lines.pop() ?? ""; // keep incomplete last line

          for (const line of lines) {
            const trimmed = line.trim();
            if (!trimmed.startsWith("data: ")) continue;

            const jsonStr = trimmed.slice(6); // remove "data: " prefix
            if (!jsonStr) continue;

            try {
              const event = JSON.parse(jsonStr);

              switch (event.type) {
                case "TEXT_MESSAGE_CONTENT":
                  accumulatedContent += event.delta ?? "";
                  // Update assistant message in-place
                  setMessages((prev) => {
                    const copy = [...prev];
                    const last = copy[copy.length - 1];
                    if (last && last.role === "assistant") {
                      copy[copy.length - 1] = {
                        ...last,
                        content: accumulatedContent,
                      };
                    }
                    return copy;
                  });
                  break;

                case "TOOL_CALL_START": {
                  const tc: ToolCall = {
                    toolCallId: event.toolCallId,
                    toolName: event.toolCallName,
                    status: "calling",
                  };
                  setMessages((prev) => {
                    const copy = [...prev];
                    const last = copy[copy.length - 1];
                    if (last && last.role === "assistant") {
                      copy[copy.length - 1] = {
                        ...last,
                        toolCalls: [...(last.toolCalls ?? []), tc],
                      };
                    }
                    return copy;
                  });
                  break;
                }

                case "TOOL_CALL_ARGS":
                  setMessages((prev) => {
                    const copy = [...prev];
                    const last = copy[copy.length - 1];
                    if (last && last.role === "assistant" && last.toolCalls) {
                      const updatedCalls = last.toolCalls.map((tc) =>
                        tc.toolCallId === event.toolCallId
                          ? { ...tc, args: (tc.args ?? "") + (event.delta ?? "") }
                          : tc,
                      );
                      copy[copy.length - 1] = { ...last, toolCalls: updatedCalls };
                    }
                    return copy;
                  });
                  break;

                case "TOOL_CALL_END":
                  setMessages((prev) => {
                    const copy = [...prev];
                    const last = copy[copy.length - 1];
                    if (last && last.role === "assistant" && last.toolCalls) {
                      const updatedCalls = last.toolCalls.map((tc) =>
                        tc.toolCallId === event.toolCallId
                          ? { ...tc, status: "completed" as const, result: event.result ?? tc.result }
                          : tc,
                      );
                      copy[copy.length - 1] = { ...last, toolCalls: updatedCalls };
                    }
                    return copy;
                  });
                  break;

                case "RUN_ERROR":
                  setError(event.message ?? "Agent 运行出错");
                  break;

                case "RUN_FINISHED":
                  // Stream complete
                  break;
              }
            } catch {
              // Ignore malformed JSON lines
            }
          }
        }
      } catch (err) {
        if ((err as Error).name === "AbortError") {
          // User cancelled — keep partial response
        } else {
          setError((err as Error).message ?? "发送消息失败");
          // Remove empty assistant message on error
          setMessages((prev) => {
            const last = prev[prev.length - 1];
            if (last && last.role === "assistant" && !last.content) {
              return prev.slice(0, -1);
            }
            return prev;
          });
        }
      } finally {
        setIsStreaming(false);
        abortControllerRef.current = null;
      }
    },
    [agentId, conversationId, messages, isStreaming],
  );

  const abortRun = useCallback(() => {
    abortControllerRef.current?.abort();
  }, []);

  const reset = useCallback(() => {
    abortControllerRef.current?.abort();
    setMessages([]);
    setIsStreaming(false);
    setError(null);
  }, []);

  return {
    messages,
    isStreaming,
    error,
    sendMessage,
    abortRun,
    reset,
    setMessages,
  };
}
