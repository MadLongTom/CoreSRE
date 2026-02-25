import { useState, useCallback, useRef } from "react";
import type { ChatMessage, ToolCall, TeamHandoff, TeamProgress, OuterLedger, InnerLedgerEntry, OrchestratorMessage, OrchestratorThought } from "@/types/chat";

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
  /** Team progress indicator — current participant agent step */
  teamProgress: TeamProgress | null;
  /** MagneticOne outer ledger — high-level plan/progress */
  outerLedger: OuterLedger | null;
  /** MagneticOne inner ledger — per-agent task entries */
  innerLedgerEntries: InnerLedgerEntry[];
  /** MagneticOne orchestrator messages — per-step reasoning */
  orchestratorMessages: OrchestratorMessage[];
  /** MagneticOne orchestrator thoughts — raw LLM responses */
  orchestratorThoughts: OrchestratorThought[];
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
  const [teamProgress, setTeamProgress] = useState<TeamProgress | null>(null);
  const [outerLedger, setOuterLedger] = useState<OuterLedger | null>(null);
  const [innerLedgerEntries, setInnerLedgerEntries] = useState<InnerLedgerEntry[]>([]);
  const [orchestratorMessages, setOrchestratorMessages] = useState<OrchestratorMessage[]>([]);
  const [orchestratorThoughts, setOrchestratorThoughts] = useState<OrchestratorThought[]>([]);
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
          headers: {
            "Content-Type": "application/json",
            "Accept": "text/event-stream",
          },
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

        console.log("[SSE] Stream connected, status:", res.status, "headers:", Object.fromEntries(res.headers.entries()));

        // Read SSE stream
        const reader = res.body?.getReader();
        if (!reader) throw new Error("No response body");

        const decoder = new TextDecoder();
        let buffer = "";

        // Track messages by messageId for Team concurrent mode (multiple parallel bubbles)
        const contentByMessageId = new Map<string, string>();
        // Track current active messageId (last TEXT_MESSAGE_START)
        let activeMessageId: string | null = null;

        while (true) {
          const { done, value } = await reader.read();
          if (done) {
            console.log("[SSE] Stream ended (done=true)");
            break;
          }

          const chunk = decoder.decode(value, { stream: true });
          if (chunk.length > 0) {
            console.log(`[SSE] Chunk received: ${chunk.length} bytes, first 200 chars:`, chunk.slice(0, 200));
          }
          buffer += chunk;

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
              console.log("[SSE] Received event:", event.type, event);

              switch (event.type) {
                case "TEXT_MESSAGE_START": {
                  const msgId = event.messageId as string | undefined;
                  if (msgId) {
                    activeMessageId = msgId;
                    if (!contentByMessageId.has(msgId)) {
                      contentByMessageId.set(msgId, "");
                    }
                  }

                  if (event.participantAgentId) {
                    // Team agent — create a new assistant message for this participant
                    setMessages((prev) => {
                      const copy = [...prev];
                      // Check if the last message is an empty placeholder we can reuse
                      const last = copy[copy.length - 1];
                      if (last && last.role === "assistant" && !last.content && !last.participantAgentId) {
                        // Reuse the empty placeholder
                        copy[copy.length - 1] = {
                          ...last,
                          participantAgentId: event.participantAgentId,
                          participantAgentName: event.participantAgentName,
                        };
                      } else {
                        // Add a new assistant message for this participant
                        copy.push({
                          index: copy.length,
                          role: "assistant",
                          content: "",
                          participantAgentId: event.participantAgentId,
                          participantAgentName: event.participantAgentName,
                        });
                      }
                      return copy;
                    });
                  }
                  break;
                }

                case "TEXT_MESSAGE_CONTENT": {
                  const msgId = (event.messageId as string | undefined) ?? activeMessageId;
                  const delta = (event.delta as string) ?? "";

                  if (msgId) {
                    const prev = contentByMessageId.get(msgId) ?? "";
                    contentByMessageId.set(msgId, prev + delta);
                  }

                  const accumulated = msgId ? (contentByMessageId.get(msgId) ?? delta) : delta;

                  // Update the correct assistant message
                  setMessages((prevMsgs) => {
                    const copy = [...prevMsgs];
                    // Find matching message by participantAgentId for Team concurrent bubbles
                    if (event.participantAgentId) {
                      for (let i = copy.length - 1; i >= 0; i--) {
                        if (copy[i].role === "assistant" && copy[i].participantAgentId === event.participantAgentId) {
                          copy[i] = { ...copy[i], content: accumulated };
                          return copy;
                        }
                      }
                    }
                    // Fallback: update last assistant message (non-team mode)
                    const last = copy[copy.length - 1];
                    if (last && last.role === "assistant") {
                      copy[copy.length - 1] = { ...last, content: accumulated };
                    }
                    return copy;
                  });
                  break;
                }

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

                case "TEAM_HANDOFF":
                  // Insert a handoff notification as a system message
                  setMessages((prev) => [
                    ...prev,
                    {
                      index: prev.length,
                      role: "system" as const,
                      content: "",
                      teamHandoff: {
                        fromAgentId: event.fromAgentId,
                        fromAgentName: event.fromAgentName,
                        toAgentId: event.toAgentId,
                        toAgentName: event.toAgentName,
                      },
                    },
                  ]);
                  break;

                case "RUN_ERROR": {
                  // Team-specific error attribution
                  const errMsg = event.message ?? "Agent 运行出错";
                  setError(errMsg);
                  // Clear progress indicator on error
                  setTeamProgress(null);
                  break;
                }

                case "RUN_FINISHED":
                  // Stream complete — clear team progress indicator
                  setTeamProgress(null);
                  break;

                case "TEAM_PROGRESS":
                  setTeamProgress({
                    currentAgentId: event.currentAgentId,
                    currentAgentName: event.currentAgentName,
                    step: event.step,
                    totalSteps: event.totalSteps,
                    mode: event.mode ?? "",
                  });
                  break;

                case "TEAM_LEDGER_UPDATE": {
                  const ledgerType = event.ledgerType as string;
                  if (ledgerType === "outer") {
                    try {
                      const parsed = JSON.parse(event.content as string) as OuterLedger;
                      setOuterLedger(parsed);
                    } catch {
                      // Ignore malformed outer ledger JSON
                    }
                  } else if (ledgerType === "inner") {
                    try {
                      const entry = JSON.parse(event.content as string) as InnerLedgerEntry;
                      if (entry.status === "completed") {
                        // Update the most recent running entry for this agent
                        setInnerLedgerEntries((prev) => {
                          const copy = [...prev];
                          for (let i = copy.length - 1; i >= 0; i--) {
                            if (copy[i].agentName === entry.agentName && copy[i].status === "running") {
                              copy[i] = { ...copy[i], status: "completed", summary: entry.summary ?? copy[i].summary };
                              break;
                            }
                          }
                          return copy;
                        });
                      } else {
                        setInnerLedgerEntries((prev) => [...prev, entry]);
                      }
                    } catch {
                      // Ignore malformed inner ledger JSON
                    }
                  } else if (ledgerType === "orchestrator") {
                    try {
                      const msg = JSON.parse(event.content as string) as OrchestratorMessage;
                      setOrchestratorMessages((prev) => [...prev, msg]);
                    } catch {
                      // Ignore malformed orchestrator message JSON
                    }
                  } else if (ledgerType === "thought") {
                    try {
                      const thought = JSON.parse(event.content as string) as OrchestratorThought;
                      setOrchestratorThoughts((prev) => [...prev, thought]);
                    } catch {
                      // Ignore malformed thought JSON
                    }
                  }
                  break;
                }
              }
            } catch {
              console.warn("[SSE] Malformed JSON line:", jsonStr.slice(0, 200));
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
    setTeamProgress(null);
    setOuterLedger(null);
    setInnerLedgerEntries([]);
    setOrchestratorMessages([]);
    setOrchestratorThoughts([]);
  }, []);

  return {
    messages,
    isStreaming,
    error,
    teamProgress,
    outerLedger,
    innerLedgerEntries,
    orchestratorMessages,
    orchestratorThoughts,
    sendMessage,
    abortRun,
    reset,
    setMessages,
  };
}
