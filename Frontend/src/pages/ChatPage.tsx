import { useEffect, useState, useCallback } from "react";
import { AgentSelector } from "@/components/chat/AgentSelector";
import { ConversationList } from "@/components/chat/ConversationList";
import { MessageArea } from "@/components/chat/MessageArea";
import { MessageInput } from "@/components/chat/MessageInput";
import { useAgentChat } from "@/hooks/use-agent-chat";
import { getAgents, type ApiError } from "@/lib/api/agents";
import {
  createConversation,
  deleteConversation,
  getConversations,
  getConversationById,
  touchConversation,
} from "@/lib/api/chat";
import type { AgentSummary } from "@/types/agent";
import type { ConversationSummary } from "@/types/chat";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";

/**
 * ChatPage — 单 Agent 对话页面。
 *
 * 流程：
 * 1. 用户选择 Agent（AgentSelector）
 * 2. 发送第一条消息 → 自动创建 Conversation → 锁定 Agent 选择器
 * 3. Agent 流式回复（SSE via AG-UI 协议）
 * 4. 每轮对话结束后调用 touchConversation 更新时间戳和标题
 */
export default function ChatPage() {
  // Agent 列表
  const [agents, setAgents] = useState<AgentSummary[]>([]);
  const [agentsLoading, setAgentsLoading] = useState(true);

  // 对话列表
  const [conversations, setConversations] = useState<ConversationSummary[]>([]);
  const [conversationsLoading, setConversationsLoading] = useState(true);

  // 选中的 Agent
  const [selectedAgentId, setSelectedAgentId] = useState<string | null>(null);

  // 对话 ID（首次发送后创建）
  const [conversationId, setConversationId] = useState<string | null>(null);

  // Agent 选择器锁定（发送第一条消息后）
  const isAgentLocked = conversationId !== null;

  // 对话 hook
  const {
    messages,
    isStreaming,
    error,
    sendMessage,
    abortRun,
    reset,
    setMessages,
  } = useAgentChat({
    agentId: selectedAgentId,
    conversationId,
  });

  // 加载 Agent 列表
  useEffect(() => {
    let cancelled = false;
    (async () => {
      setAgentsLoading(true);
      try {
        const result = await getAgents();
        if (!cancelled && result.success && result.data) {
          setAgents(result.data);
        }
      } catch {
        // Agent list loading failure is non-critical for the page
      } finally {
        if (!cancelled) setAgentsLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, []);

  // 加载对话列表
  const loadConversations = useCallback(async () => {
    setConversationsLoading(true);
    try {
      const result = await getConversations();
      if (result.success && result.data) {
        setConversations(result.data);
      }
    } catch {
      // Conversation list loading failure is non-critical
    } finally {
      setConversationsLoading(false);
    }
  }, []);

  useEffect(() => {
    loadConversations();
  }, [loadConversations]);

  // 选中对话 → 加载历史消息
  const handleSelectConversation = useCallback(
    async (conv: ConversationSummary) => {
      if (conv.id === conversationId) return;

      try {
        const result = await getConversationById(conv.id);
        if (result.success && result.data) {
          setConversationId(conv.id);
          setSelectedAgentId(conv.agentId);
          setMessages(result.data.messages);
        }
      } catch (err) {
        console.error("Failed to load conversation:", (err as Error).message);
      }
    },
    [conversationId, setMessages],
  );

  // 发送消息（含首次自动创建 Conversation）
  const handleSend = useCallback(
    async (content: string) => {
      if (!selectedAgentId) return;

      let convId = conversationId;

      // 首次发送 → 创建 Conversation
      if (!convId) {
        try {
          const result = await createConversation({ agentId: selectedAgentId });
          if (result.success && result.data) {
            convId = result.data.id;
            setConversationId(convId);
          } else {
            return; // 创建失败，不发送
          }
        } catch (err) {
          const apiErr = err as ApiError;
          console.error("Failed to create conversation:", apiErr.message);
          return;
        }
      }

      // 发送消息（流式）— 传入 convId 以避免 React state 批处理导致 threadId 不一致
      await sendMessage(content, convId ?? undefined);

      // 对话结束后 touch（更新时间戳 + 首次设置标题）
      if (convId) {
        const isFirstMessage = messages.length === 0;
        touchConversation(convId, isFirstMessage ? { firstMessage: content } : {});
        // 刷新对话列表以反映最新时间戳和标题
        loadConversations();
      }
    },
    [selectedAgentId, conversationId, sendMessage, messages.length, loadConversations],
  );

  // 新建对话（重置状态）
  const handleNewConversation = useCallback(() => {
    reset();
    setConversationId(null);
    setSelectedAgentId(null);
    loadConversations(); // 刷新对话列表
  }, [reset, loadConversations]);

  // 删除对话
  const handleDeleteConversation = useCallback(
    async (deletedId: string) => {
      await deleteConversation(deletedId);
      // 如果删除的是当前活跃对话，重置到新对话状态
      if (deletedId === conversationId) {
        reset();
        setConversationId(null);
        setSelectedAgentId(null);
      }
      loadConversations();
    },
    [conversationId, reset, loadConversations],
  );

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      {/* Unified page header — h-14 matching all other pages */}
      <div className="flex h-14 shrink-0 items-center gap-3 border-b px-6">
        <h1 className="text-lg font-semibold">对话</h1>
        <AgentSelector
          agents={agents}
          selectedAgentId={selectedAgentId}
          onSelectAgent={setSelectedAgentId}
          disabled={isAgentLocked}
          loading={agentsLoading}
        />
        {isAgentLocked && (
          <div className="ml-auto">
            <Button variant="outline" size="sm" onClick={handleNewConversation}>
              <Plus className="mr-2 h-4 w-4" />
              新建对话
            </Button>
          </div>
        )}
      </div>

      {/* Body: sidebar + chat area */}
      <div className="flex flex-1 overflow-hidden">
        {/* Conversation sidebar */}
        <div className="flex w-64 shrink-0 flex-col border-r">
          <div className="flex h-10 shrink-0 items-center border-b px-3">
            <span className="text-xs font-medium text-muted-foreground">历史对话</span>
          </div>
          <div className="flex-1 overflow-y-auto p-2">
            <ConversationList
              conversations={conversations}
              selectedId={conversationId}
              onSelect={handleSelectConversation}
              onDelete={handleDeleteConversation}
              loading={conversationsLoading}
            />
          </div>
        </div>

        {/* Main chat area */}
        <div className="flex flex-1 flex-col overflow-hidden">
          {/* Error banner */}
          {error && (
            <div className="shrink-0 border-b bg-destructive/10 px-4 py-2 text-sm text-destructive">
              {error}
            </div>
          )}

          {/* Messages */}
          <MessageArea messages={messages} isStreaming={isStreaming} />

          {/* Input */}
          <MessageInput
            onSend={handleSend}
            disabled={!selectedAgentId}
            isStreaming={isStreaming}
            onAbort={abortRun}
          />
        </div>
      </div>
    </div>
  );
}
