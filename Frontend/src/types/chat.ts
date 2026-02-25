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
  role: "user" | "assistant" | "tool" | "system";
  content: string;
  toolCalls?: ToolCall[];  // present on assistant messages with tool usage
  /** Semantic memory context injected for this user turn (system role, not shown directly) */
  memoryContext?: string | null;
  /** Originating participant agent GUID — present only in Team mode conversations */
  participantAgentId?: string;
  /** Originating participant agent name — present only in Team mode conversations */
  participantAgentName?: string;
  /** Team handoff notification data — present on system messages for handoff events */
  teamHandoff?: TeamHandoff;
}

// ---------------------------------------------------------------------------
// Team Mode Types (MagneticOne ledger, progress indicator)
// ---------------------------------------------------------------------------

/** Outer ledger maintained by MagneticOne orchestrator — high-level plan/progress */
export interface OuterLedger {
  facts: string;
  plan: string;
  nextStep: string;
  progress: string;
  isComplete: boolean;
  /** Synthesized final answer from MagneticOne orchestrator. */
  finalAnswer?: string;
  /** Current orchestrator iteration (1-based). */
  iteration: number;
  /** Consecutive stall count. */
  nStalls: number;
  /** Max stalls before replanning. */
  maxStalls: number;
}

/** Inner ledger entry — per-agent task execution log in MagneticOne mode */
export interface InnerLedgerEntry {
  agentName: string;
  task: string;
  status: "running" | "completed" | "failed";
  summary?: string;
  timestamp: string;
}

/** Orchestrator instruction — emitted per-step showing the orchestrator's decision */
export interface OrchestratorMessage {
  iteration: number;
  targetAgent: string;
  instruction: string;
  isRequestSatisfied: boolean;
  isProgressBeingMade: boolean;
  isInLoop: boolean;
  reason: string;
}

/** Orchestrator thought — raw LLM response from each orchestrator call */
export interface OrchestratorThought {
  category: "facts" | "plan" | "facts_update" | "plan_update" | "progress_ledger" | "final_answer";
  content: string;
}

/** Team orchestration progress indicator state */
export interface TeamProgress {
  currentAgentId: string;
  currentAgentName: string;
  step?: number;
  totalSteps?: number;
  mode: string;
}

/** Handoff notification — displayed as a system message in Handoffs mode */
export interface TeamHandoff {
  fromAgentId: string;
  fromAgentName: string;
  toAgentId: string;
  toAgentName: string;
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
