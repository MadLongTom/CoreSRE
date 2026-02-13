// =============================================================================
// Sandbox API Client — typed fetch wrapper functions
// =============================================================================

import { ApiError } from "@/lib/api/agents";
import type { ApiResult } from "@/types/agent";
import type {
  CreateSandboxRequest,
  ExecSandboxRequest,
  SandboxExecResult,
  SandboxInstance,
  SandboxPagedResponse,
  UpdateSandboxRequest,
} from "@/types/sandbox";

// ---------------------------------------------------------------------------
// Timeout helper
// ---------------------------------------------------------------------------

const TIMEOUT_MS = 15_000;

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

/** GET /api/sandboxes — list sandboxes with optional filters */
export async function getSandboxes(params?: {
  status?: string;
  agentId?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}): Promise<ApiResult<SandboxPagedResponse>> {
  const query = new URLSearchParams();
  if (params?.status) query.set("status", params.status);
  if (params?.agentId) query.set("agentId", params.agentId);
  if (params?.search) query.set("search", params.search);
  if (params?.page) query.set("page", String(params.page));
  if (params?.pageSize) query.set("pageSize", String(params.pageSize));

  const qs = query.toString();
  const res = await fetchWithTimeout(`/api/sandboxes${qs ? `?${qs}` : ""}`);
  return handleResponse<SandboxPagedResponse>(res);
}

/** GET /api/sandboxes/:id — get sandbox details */
export async function getSandboxById(
  id: string,
): Promise<ApiResult<SandboxInstance>> {
  const res = await fetchWithTimeout(`/api/sandboxes/${id}`);
  return handleResponse<SandboxInstance>(res);
}

/** POST /api/sandboxes — create a new sandbox */
export async function createSandbox(
  data: CreateSandboxRequest,
): Promise<ApiResult<SandboxInstance>> {
  const res = await fetchWithTimeout("/api/sandboxes", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data),
  });
  return handleResponse<SandboxInstance>(res);
}

/** PUT /api/sandboxes/:id — update sandbox config */
export async function updateSandbox(
  id: string,
  data: UpdateSandboxRequest,
): Promise<ApiResult<SandboxInstance>> {
  const res = await fetchWithTimeout(`/api/sandboxes/${id}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ id, ...data }),
  });
  return handleResponse<SandboxInstance>(res);
}

/** DELETE /api/sandboxes/:id — delete sandbox */
export async function deleteSandbox(id: string): Promise<void> {
  const res = await fetchWithTimeout(`/api/sandboxes/${id}`, {
    method: "DELETE",
  });
  if (!res.ok && res.status !== 204) {
    throw new ApiError(res.status, undefined, undefined, "Failed to delete sandbox");
  }
}

/** POST /api/sandboxes/:id/start — start a sandbox */
export async function startSandbox(
  id: string,
): Promise<ApiResult<SandboxInstance>> {
  const res = await fetchWithTimeout(`/api/sandboxes/${id}/start`, {
    method: "POST",
  });
  return handleResponse<SandboxInstance>(res);
}

/** POST /api/sandboxes/:id/stop — stop a sandbox */
export async function stopSandbox(
  id: string,
): Promise<ApiResult<SandboxInstance>> {
  const res = await fetchWithTimeout(`/api/sandboxes/${id}/stop`, {
    method: "POST",
  });
  return handleResponse<SandboxInstance>(res);
}

/** POST /api/sandboxes/:id/exec — execute command in sandbox */
export async function execSandbox(
  id: string,
  data: ExecSandboxRequest,
): Promise<ApiResult<SandboxExecResult>> {
  const res = await fetchWithTimeout(`/api/sandboxes/${id}/exec`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ id, ...data }),
  });
  return handleResponse<SandboxExecResult>(res);
}
