// =============================================================================
// Agent API Client — typed fetch wrapper functions
// See: specs/005-frontend-pages/contracts/api-contract.md
// =============================================================================

import type {
  AgentRegistration,
  AgentSearchResponse,
  AgentSummary,
  ApiResult,
  CreateAgentRequest,
  UpdateAgentRequest,
} from "@/types/agent";

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
 * GET /api/agents?type={agentType}
 * List all agents, optionally filtered by type.
 */
export async function getAgents(
  type?: string,
): Promise<ApiResult<AgentSummary[]>> {
  const url = type
    ? `/api/agents?type=${encodeURIComponent(type)}`
    : "/api/agents";
  const res = await fetchWithTimeout(url);
  return handleResponse<AgentSummary[]>(res);
}

/**
 * GET /api/agents/{id}
 * Get full agent details by ID.
 */
export async function getAgentById(
  id: string,
): Promise<ApiResult<AgentRegistration>> {
  const res = await fetchWithTimeout(`/api/agents/${encodeURIComponent(id)}`);
  return handleResponse<AgentRegistration>(res);
}

/**
 * POST /api/agents
 * Register a new agent.
 */
export async function createAgent(
  data: CreateAgentRequest,
): Promise<ApiResult<AgentRegistration>> {
  const res = await fetchWithTimeout("/api/agents", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data),
  });
  return handleResponse<AgentRegistration>(res);
}

/**
 * PUT /api/agents/{id}
 * Update an existing agent (agentType is immutable).
 */
export async function updateAgent(
  id: string,
  data: UpdateAgentRequest,
): Promise<ApiResult<AgentRegistration>> {
  const res = await fetchWithTimeout(`/api/agents/${encodeURIComponent(id)}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data),
  });
  return handleResponse<AgentRegistration>(res);
}

/**
 * DELETE /api/agents/{id}
 * Delete an agent. Returns void on 204 No Content.
 */
export async function deleteAgent(id: string): Promise<void> {
  const res = await fetchWithTimeout(`/api/agents/${encodeURIComponent(id)}`, {
    method: "DELETE",
  });
  if (res.status === 204) return;
  if (!res.ok) {
    let body: ApiResult<unknown> | undefined;
    try {
      body = await res.json();
    } catch {
      // response may not be JSON
    }
    throw new ApiError(
      res.status,
      body?.errors,
      body?.errorCode,
      body?.message ?? `Delete failed with status ${res.status}`,
    );
  }
}

/**
 * GET /api/agents/search?q={query}
 * Search agents by skill keywords. Returns unwrapped AgentSearchResponse.
 */
export async function searchAgents(
  query: string,
): Promise<AgentSearchResponse> {
  const res = await fetchWithTimeout(
    `/api/agents/search?q=${encodeURIComponent(query)}`,
  );
  if (!res.ok) {
    let body: ApiResult<unknown> | undefined;
    try {
      body = await res.json();
    } catch {
      // response may not be JSON
    }
    throw new ApiError(
      res.status,
      body?.errors,
      body?.errorCode,
      body?.message ?? `Search failed with status ${res.status}`,
    );
  }
  // Search endpoint returns unwrapped AgentSearchResponse (not wrapped in ApiResult)
  return res.json();
}
