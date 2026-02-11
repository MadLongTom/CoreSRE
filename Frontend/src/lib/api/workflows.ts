// =============================================================================
// Workflow API Client — typed fetch wrapper functions
// See: specs/013-workflow-frontend/contracts/api-contracts.md
// =============================================================================

import type {
  ApiResult,
  CreateWorkflowRequest,
  ExecuteWorkflowRequest,
  UpdateWorkflowRequest,
  WorkflowDetail,
  WorkflowExecutionDetail,
  WorkflowExecutionSummary,
  WorkflowSummary,
} from "@/types/workflow";
import { ApiError } from "@/lib/api/agents";

export { ApiError };

// ---------------------------------------------------------------------------
// Timeout helper (same as agents.ts pattern)
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
 * GET /api/workflows?status={status}
 * List all workflow definitions, optionally filtered by status.
 */
export async function getWorkflows(
  status?: string,
): Promise<ApiResult<WorkflowSummary[]>> {
  const url = status
    ? `/api/workflows?status=${encodeURIComponent(status)}`
    : "/api/workflows";
  const res = await fetchWithTimeout(url);
  return handleResponse<WorkflowSummary[]>(res);
}

/**
 * GET /api/workflows/{id}
 * Get full workflow definition by ID including graph.
 */
export async function getWorkflowById(
  id: string,
): Promise<ApiResult<WorkflowDetail>> {
  const res = await fetchWithTimeout(
    `/api/workflows/${encodeURIComponent(id)}`,
  );
  return handleResponse<WorkflowDetail>(res);
}

/**
 * POST /api/workflows
 * Create a new workflow definition.
 */
export async function createWorkflow(
  data: CreateWorkflowRequest,
): Promise<ApiResult<WorkflowDetail>> {
  const res = await fetchWithTimeout("/api/workflows", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data),
  });
  return handleResponse<WorkflowDetail>(res);
}

/**
 * PUT /api/workflows/{id}
 * Update an existing workflow definition. Only Draft status allowed.
 */
export async function updateWorkflow(
  id: string,
  data: UpdateWorkflowRequest,
): Promise<ApiResult<WorkflowDetail>> {
  const res = await fetchWithTimeout(
    `/api/workflows/${encodeURIComponent(id)}`,
    {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(data),
    },
  );
  return handleResponse<WorkflowDetail>(res);
}

/**
 * DELETE /api/workflows/{id}
 * Delete a workflow definition. Only Draft status allowed. Returns void on 204.
 */
export async function deleteWorkflow(id: string): Promise<void> {
  const res = await fetchWithTimeout(
    `/api/workflows/${encodeURIComponent(id)}`,
    {
      method: "DELETE",
    },
  );
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
 * POST /api/workflows/{id}/execute
 * Trigger workflow execution. Only Published status allowed.
 */
export async function executeWorkflow(
  id: string,
  data: ExecuteWorkflowRequest,
): Promise<ApiResult<WorkflowExecutionDetail>> {
  const res = await fetchWithTimeout(
    `/api/workflows/${encodeURIComponent(id)}/execute`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(data),
    },
  );
  return handleResponse<WorkflowExecutionDetail>(res);
}

/**
 * GET /api/workflows/{id}/executions?status={status}
 * List execution records for a workflow, optionally filtered by status.
 */
export async function getWorkflowExecutions(
  id: string,
  status?: string,
): Promise<ApiResult<WorkflowExecutionSummary[]>> {
  let url = `/api/workflows/${encodeURIComponent(id)}/executions`;
  if (status) {
    url += `?status=${encodeURIComponent(status)}`;
  }
  const res = await fetchWithTimeout(url);
  return handleResponse<WorkflowExecutionSummary[]>(res);
}

/**
 * GET /api/workflows/{id}/executions/{execId}
 * Get full execution detail with node-level data and graph snapshot.
 */
export async function getWorkflowExecutionById(
  id: string,
  execId: string,
): Promise<ApiResult<WorkflowExecutionDetail>> {
  const res = await fetchWithTimeout(
    `/api/workflows/${encodeURIComponent(id)}/executions/${encodeURIComponent(execId)}`,
  );
  return handleResponse<WorkflowExecutionDetail>(res);
}
