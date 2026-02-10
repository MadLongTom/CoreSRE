// =============================================================================
// Provider API Client — typed fetch wrapper functions
// See: specs/006-llm-provider-config/contracts/api-contract.md
// =============================================================================

import type { ApiResult } from "@/types/agent";
import type {
  CreateProviderRequest,
  DiscoveredModel,
  LlmProvider,
  LlmProviderSummary,
  UpdateProviderRequest,
} from "@/types/provider";
import { ApiError } from "@/lib/api/agents";

export { ApiError };

// ---------------------------------------------------------------------------
// Timeout helper
// ---------------------------------------------------------------------------

const TIMEOUT_MS = 15_000; // Longer timeout for model discovery

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
 * GET /api/providers
 * List all providers.
 */
export async function getProviders(): Promise<ApiResult<LlmProviderSummary[]>> {
  const res = await fetchWithTimeout("/api/providers");
  return handleResponse<LlmProviderSummary[]>(res);
}

/**
 * GET /api/providers/{id}
 * Get full provider details by ID.
 */
export async function getProviderById(
  id: string,
): Promise<ApiResult<LlmProvider>> {
  const res = await fetchWithTimeout(
    `/api/providers/${encodeURIComponent(id)}`,
  );
  return handleResponse<LlmProvider>(res);
}

/**
 * POST /api/providers
 * Register a new provider.
 */
export async function createProvider(
  data: CreateProviderRequest,
): Promise<ApiResult<LlmProvider>> {
  const res = await fetchWithTimeout("/api/providers", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data),
  });
  return handleResponse<LlmProvider>(res);
}

/**
 * PUT /api/providers/{id}
 * Update an existing provider.
 */
export async function updateProvider(
  id: string,
  data: UpdateProviderRequest,
): Promise<ApiResult<LlmProvider>> {
  const res = await fetchWithTimeout(
    `/api/providers/${encodeURIComponent(id)}`,
    {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(data),
    },
  );
  return handleResponse<LlmProvider>(res);
}

/**
 * DELETE /api/providers/{id}
 * Delete a provider.
 */
export async function deleteProvider(id: string): Promise<void> {
  const res = await fetchWithTimeout(
    `/api/providers/${encodeURIComponent(id)}`,
    { method: "DELETE" },
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
      body?.message ?? `Delete failed with status ${res.status}`,
    );
  }
}

/**
 * POST /api/providers/{id}/discover
 * Trigger model discovery for a provider.
 */
export async function discoverModels(
  id: string,
): Promise<ApiResult<LlmProvider>> {
  const res = await fetchWithTimeout(
    `/api/providers/${encodeURIComponent(id)}/discover`,
    { method: "POST" },
  );
  return handleResponse<LlmProvider>(res);
}

/**
 * GET /api/providers/{id}/models
 * Get discovered models for a provider.
 */
export async function getProviderModels(
  id: string,
): Promise<ApiResult<DiscoveredModel[]>> {
  const res = await fetchWithTimeout(
    `/api/providers/${encodeURIComponent(id)}/models`,
  );
  return handleResponse<DiscoveredModel[]>(res);
}
