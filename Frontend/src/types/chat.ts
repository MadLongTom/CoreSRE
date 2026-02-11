// =============================================================================
// Chat Type Definitions — maps to backend C# DTOs
// See: specs/007-agent-chat-ui/contracts/chat-api.yaml
// =============================================================================

// ---------------------------------------------------------------------------
// API Response Wrapper (reuse from agent.ts pattern)
// ---------------------------------------------------------------------------

import type { ApiResult } from "@/types/agent";
export type { ApiResult };

// ---------------------------------------------------------------------------
// Conversation Summary (list item)
// ---------------------------------------------------------------------------

/** Maps to backend ConversationSummaryDto — used in GET /api/chat/conversations */
export interface ConversationSummary {
  id: string;
  agentId: string;
  agentName: string;
  agentType: string;
  title: string | null;
  lastMessage: string | null;
  lastMessageAt: string | null;
  createdAt: string;
}

// ---------------------------------------------------------------------------
// Conversation Detail (with messages)
// ---------------------------------------------------------------------------

/** Maps to backend ConversationDto — used in GET /api/chat/conversations/{id} */
export interface Conversation {
  id: string;
  agentId: string;
  agentName: string;
  agentType: string;
  title: string | null;
  createdAt: string;
  updatedAt: string | null;
  messages: ChatMessage[];
}

// ---------------------------------------------------------------------------
// Tool Call (AG-UI tool invocation tracking)
// ---------------------------------------------------------------------------

/** Tracks a single tool call within an assistant message's streaming lifecycle */
export interface ToolCall {
  toolCallId: string;
  toolName: string;
  status: "calling" | "completed" | "failed";
  args?: string;   // JSON string of tool arguments
  result?: string;  // JSON string of tool result or error message
}

// ---------------------------------------------------------------------------
// Chat Message (projected from AgentSessionRecord.SessionData)
// ---------------------------------------------------------------------------

/** Maps to backend ChatMessageDto — NOT a DB entity, extracted from SessionData JSONB */
export interface ChatMessage {
  index: number;
  role: "user" | "assistant";
  content: string;
  toolCalls?: ToolCall[];  // present on assistant messages with tool usage
}

// ---------------------------------------------------------------------------
// Request Types
// ---------------------------------------------------------------------------

/** POST /api/chat/conversations request body */
export interface CreateConversationRequest {
  agentId: string;
}

/** POST /api/chat/conversations/{id}/touch request body */
export interface TouchConversationRequest {
  firstMessage?: string;
}
