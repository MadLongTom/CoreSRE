// =============================================================================
// Skills API Client — typed fetch wrapper functions
// =============================================================================

import { ApiError } from "@/lib/api/agents";
import type { ApiResult } from "@/types/agent";
import type {
  CreateSkillRequest,
  SkillFileEntry,
  SkillPagedResponse,
  SkillRegistration,
  UpdateSkillRequest,
} from "@/types/skill";

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

/** GET /api/skills — list skills with optional filters */
export async function getSkills(params?: {
  scope?: string;
  status?: string;
  category?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}): Promise<ApiResult<SkillPagedResponse>> {
  const query = new URLSearchParams();
  if (params?.scope) query.set("scope", params.scope);
  if (params?.status) query.set("status", params.status);
  if (params?.category) query.set("category", params.category);
  if (params?.search) query.set("search", params.search);
  if (params?.page) query.set("page", String(params.page));
  if (params?.pageSize) query.set("pageSize", String(params.pageSize));

  const qs = query.toString();
  const res = await fetchWithTimeout(`/api/skills${qs ? `?${qs}` : ""}`);
  return handleResponse<SkillPagedResponse>(res);
}

/** GET /api/skills/:id — get skill details */
export async function getSkillById(
  id: string,
): Promise<ApiResult<SkillRegistration>> {
  const res = await fetchWithTimeout(`/api/skills/${id}`);
  return handleResponse<SkillRegistration>(res);
}

/** POST /api/skills — create a new skill */
export async function createSkill(
  data: CreateSkillRequest,
): Promise<ApiResult<SkillRegistration>> {
  const res = await fetchWithTimeout("/api/skills", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data),
  });
  return handleResponse<SkillRegistration>(res);
}

/** PUT /api/skills/:id — update skill */
export async function updateSkill(
  id: string,
  data: UpdateSkillRequest,
): Promise<ApiResult<SkillRegistration>> {
  const res = await fetchWithTimeout(`/api/skills/${id}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ id, ...data }),
  });
  return handleResponse<SkillRegistration>(res);
}

/** DELETE /api/skills/:id — delete skill */
export async function deleteSkill(id: string): Promise<void> {
  const res = await fetchWithTimeout(`/api/skills/${id}`, {
    method: "DELETE",
  });
  if (!res.ok && res.status !== 204) {
    throw new ApiError(res.status, undefined, undefined, "Failed to delete skill");
  }
}

/** POST /api/skills/:id/files — upload files to skill package */
export async function uploadSkillFiles(
  id: string,
  files: File[],
): Promise<ApiResult<{ uploaded: number }>> {
  const form = new FormData();
  for (const file of files) {
    form.append("files", file);
  }
  const res = await fetchWithTimeout(`/api/skills/${id}/files`, {
    method: "POST",
    body: form,
  });
  return handleResponse<{ uploaded: number }>(res);
}

/** GET /api/skills/:id/files — list files in skill package */
export async function getSkillFiles(
  id: string,
): Promise<ApiResult<SkillFileEntry[]>> {
  const res = await fetchWithTimeout(`/api/skills/${id}/files`);
  return handleResponse<SkillFileEntry[]>(res);
}

/** DELETE /api/skills/:id/files/:key — delete a file from skill package */
export async function deleteSkillFile(
  id: string,
  fileKey: string,
): Promise<void> {
  const encodedKey = encodeURIComponent(fileKey);
  const res = await fetchWithTimeout(
    `/api/skills/${id}/files/${encodedKey}`,
    { method: "DELETE" },
  );
  if (!res.ok && res.status !== 204) {
    throw new ApiError(res.status, undefined, undefined, "Failed to delete file");
  }
}
