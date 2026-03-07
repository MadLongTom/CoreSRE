// =============================================================================
// Incidents API Client — typed fetch wrappers for incident endpoints
// =============================================================================

import { ApiError } from "@/lib/api/agents";
import type { ApiResult } from "@/types/agent";
import type { RcaAccuracyRating, SopEffectivenessRating } from "@/types/incident";

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
// Post-mortem (Spec 023)
// ---------------------------------------------------------------------------

/** POST /api/incidents/:id/post-mortem */
export async function annotatePostMortem(
  id: string,
  data: {
    actualRootCause: string;
    rcaAccuracy: RcaAccuracyRating;
    sopEffectiveness?: SopEffectivenessRating;
    improvementNotes?: string;
    annotatedBy: string;
  },
): Promise<ApiResult<void>> {
  const res = await fetchWithTimeout(`/api/incidents/${id}/post-mortem`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data),
  });
  return handleResponse<void>(res);
}

// ---------------------------------------------------------------------------
// Step Retry (Spec 024)
// ---------------------------------------------------------------------------

/** POST /api/incidents/:id/steps/:stepNumber/retry */
export async function retryStepExecution(
  id: string,
  stepNumber: number,
): Promise<ApiResult<void>> {
  const res = await fetchWithTimeout(
    `/api/incidents/${id}/steps/${stepNumber}/retry`,
    { method: "POST" },
  );
  return handleResponse<void>(res);
}

// ---------------------------------------------------------------------------
// Fallback (Spec 025)
// ---------------------------------------------------------------------------

/** POST /api/incidents/:id/fallback-rca */
export async function fallbackToRca(
  id: string,
  data: { reason: string },
): Promise<ApiResult<void>> {
  const res = await fetchWithTimeout(`/api/incidents/${id}/fallback-rca`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data),
  });
  return handleResponse<void>(res);
}
