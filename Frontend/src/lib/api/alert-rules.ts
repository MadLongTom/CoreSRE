// =============================================================================
// Alert Rules API Client — typed fetch wrappers (Spec 025: canary & health)
// =============================================================================

import { ApiError } from "@/lib/api/agents";
import type { ApiResult } from "@/types/agent";
import type { AlertRuleHealth, CanaryReport } from "@/types/alert-rule";

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

async function handleResponse<T>(res: Response): Promise<ApiResult<T>> {
  if (!res.ok) {
    let body: ApiResult<T> | undefined;
    try {
      body = await res.json();
    } catch { /* ignore */ }
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
// Canary (Spec 025)
// ---------------------------------------------------------------------------

/** POST /api/alert-rules/:id/canary/start */
export async function startCanary(
  id: string,
  data: { canarySopId: string },
): Promise<ApiResult<void>> {
  const res = await fetchWithTimeout(`/api/alert-rules/${id}/canary/start`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data),
  });
  return handleResponse<void>(res);
}

/** POST /api/alert-rules/:id/canary/stop */
export async function stopCanary(id: string): Promise<ApiResult<void>> {
  const res = await fetchWithTimeout(`/api/alert-rules/${id}/canary/stop`, {
    method: "POST",
  });
  return handleResponse<void>(res);
}

/** GET /api/alert-rules/:id/canary/report */
export async function getCanaryReport(
  id: string,
): Promise<ApiResult<CanaryReport>> {
  const res = await fetchWithTimeout(`/api/alert-rules/${id}/canary/report`);
  return handleResponse<CanaryReport>(res);
}

// ---------------------------------------------------------------------------
// Health (Spec 025)
// ---------------------------------------------------------------------------

/** GET /api/alert-rules/:id/health */
export async function getAlertRuleHealth(
  id: string,
): Promise<ApiResult<AlertRuleHealth>> {
  const res = await fetchWithTimeout(`/api/alert-rules/${id}/health`);
  return handleResponse<AlertRuleHealth>(res);
}
