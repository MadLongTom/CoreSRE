// =============================================================================
// Chat API Client — typed fetch wrapper functions
// See: specs/007-agent-chat-ui/contracts/chat-api.yaml
// =============================================================================

import type {
  ApiResult,
  Conversation,
  ConversationSummary,
  CreateConversationRequest,
  TouchConversationRequest,
} from "@/types/chat";

// ---------------------------------------------------------------------------
// Error class
// ---------------------------------------------------------------------------

export class ApiError extends Error {
  readonly status: number;
  readonly errors?: string[];
  readonly errorCode?: number;

  constructor(
    status: number,
    errors?: string[],
    errorCode?: number,
    message?: string,
  ) {
    super(message ?? `API error: ${status}`);
    this.name = "ApiError";
    this.status = status;
    this.errors = errors;
    this.errorCode = errorCode;
  }
}

// ---------------------------------------------------------------------------
// Timeout helper
// ---------------------------------------------------------------------------

const TIMEOUT_MS = 10_000;

function fetchWithTimeout(
  input: RequestInfo | URL,
  init?: RequestInit,
): Promise<Response> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), TIMEOUT_MS);
  return fetch(input, { ...init, signal: controller.signal }).finally(() =>
    clearTimeout(timer),
  );
}

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

async function handleResponse<T>(res: Response): Promise<ApiResult<T>> {
  if (!res.ok) {
    let body: ApiResult<T> | undefined;
    try {
      body = await res.json();
    } catch {
      // response may not be JSON
    }
    throw new ApiError(
      res.status,
      body?.errors,
      body?.errorCode,
      body?.message ?? `Request failed with status ${res.status}`,
    );
  }
  return res.json();
}

// ---------------------------------------------------------------------------
// Public API functions
// ---------------------------------------------------------------------------

/**
 * POST /api/chat/conversations
 * Create a new conversation bound to a specific agent.
 */
export async function createConversation(
  data: CreateConversationRequest,
): Promise<ApiResult<Conversation>> {
  const res = await fetchWithTimeout("/api/chat/conversations", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data),
  });
  return handleResponse<Conversation>(res);
}

/**
 * GET /api/chat/conversations
 * Retrieve all conversations sorted by most recent activity.
 */
export async function getConversations(): Promise<
  ApiResult<ConversationSummary[]>
> {
  const res = await fetchWithTimeout("/api/chat/conversations");
  return handleResponse<ConversationSummary[]>(res);
}

/**
 * GET /api/chat/conversations/{id}
 * Retrieve conversation details with full message history.
 */
export async function getConversationById(
  id: string,
): Promise<ApiResult<Conversation>> {
  const res = await fetchWithTimeout(
    `/api/chat/conversations/${encodeURIComponent(id)}`,
  );
  return handleResponse<Conversation>(res);
}

/**
 * POST /api/chat/conversations/{id}/touch
 * Touch a conversation (update timestamp + set title from first message).
 */
export async function touchConversation(
  id: string,
  data: TouchConversationRequest,
): Promise<void> {
  try {
    await fetchWithTimeout(
      `/api/chat/conversations/${encodeURIComponent(id)}/touch`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(data),
      },
    );
  } catch {
    // Touch failures are non-critical, silently ignore
  }
}

/**
 * DELETE /api/chat/conversations/{id}
 * Delete a conversation and its associated session record.
 */
export async function deleteConversation(id: string): Promise<void> {
  const res = await fetchWithTimeout(
    `/api/chat/conversations/${encodeURIComponent(id)}`,
    { method: "DELETE" },
  );
  if (!res.ok) {
    throw new ApiError(res.status, undefined, undefined, `Delete failed: ${res.status}`);
  }
}
