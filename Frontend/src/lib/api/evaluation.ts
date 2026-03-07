// =============================================================================
// Evaluation API Client — typed fetch wrapper functions (Spec 023 + 025)
// =============================================================================

import { ApiError } from "@/lib/api/agents";
import type { ApiResult } from "@/types/agent";
import type {
  EvaluationDashboard,
  SopEffectiveness,
  FeedbackSummary,
} from "@/types/evaluation";

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
// Evaluation Dashboard (Spec 023)
// ---------------------------------------------------------------------------

/** GET /api/evaluation/dashboard */
export async function getEvaluationDashboard(params?: {
  from?: string;
  to?: string;
}): Promise<ApiResult<EvaluationDashboard>> {
  const query = new URLSearchParams();
  if (params?.from) query.set("from", params.from);
  if (params?.to) query.set("to", params.to);
  const qs = query.toString();
  const res = await fetchWithTimeout(
    `/api/evaluation/dashboard${qs ? `?${qs}` : ""}`,
  );
  return handleResponse<EvaluationDashboard>(res);
}

/** GET /api/evaluation/sops */
export async function getSopEffectiveness(params?: {
  from?: string;
  to?: string;
}): Promise<ApiResult<SopEffectiveness[]>> {
  const query = new URLSearchParams();
  if (params?.from) query.set("from", params.from);
  if (params?.to) query.set("to", params.to);
  const qs = query.toString();
  const res = await fetchWithTimeout(
    `/api/evaluation/sops${qs ? `?${qs}` : ""}`,
  );
  return handleResponse<SopEffectiveness[]>(res);
}

// ---------------------------------------------------------------------------
// Feedback Summary (Spec 025)
// ---------------------------------------------------------------------------

/** GET /api/evaluation/feedback-summary */
export async function getFeedbackSummary(params?: {
  from?: string;
  to?: string;
}): Promise<ApiResult<FeedbackSummary>> {
  const query = new URLSearchParams();
  if (params?.from) query.set("from", params.from);
  if (params?.to) query.set("to", params.to);
  const qs = query.toString();
  const res = await fetchWithTimeout(
    `/api/evaluation/feedback-summary${qs ? `?${qs}` : ""}`,
  );
  return handleResponse<FeedbackSummary>(res);
}
