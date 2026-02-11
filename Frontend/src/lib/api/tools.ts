// =============================================================================
// Tool Gateway API Client — typed fetch wrapper functions
// =============================================================================

import type { ApiResult } from "@/types/agent";
import type {
  BindableTool,
  CreateToolRequest,
  McpToolItem,
  OpenApiImportResult,
  ToolInvocationResult,
  ToolListResponse,
  ToolRegistration,
  UpdateToolRequest,
  InvokeToolRequest,
} from "@/types/tool";
import { ApiError } from "@/lib/api/agents";

export { ApiError };

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
    } catch {}
    throw new ApiError(
      res.status,
      body?.errors,
      body?.errorCode,
      body?.message ?? `Request failed with status ${res.status}`,
    );
  }
  return res.json();
}

// ── CRUD ──

export async function getTools(params?: {
  toolType?: string;
  status?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}): Promise<ApiResult<ToolListResponse>> {
  const sp = new URLSearchParams();
  if (params?.toolType) sp.set("toolType", params.toolType);
  if (params?.status) sp.set("status", params.status);
  if (params?.search) sp.set("search", params.search);
  if (params?.page) sp.set("page", String(params.page));
  if (params?.pageSize) sp.set("pageSize", String(params.pageSize));
  const qs = sp.toString();
  const url = qs ? `/api/tools?${qs}` : "/api/tools";
  const res = await fetchWithTimeout(url);
  return handleResponse<ToolListResponse>(res);
}

export async function getToolById(
  id: string,
): Promise<ApiResult<ToolRegistration>> {
  const res = await fetchWithTimeout(
    `/api/tools/${encodeURIComponent(id)}`,
  );
  return handleResponse<ToolRegistration>(res);
}

export async function createTool(
  data: CreateToolRequest,
): Promise<ApiResult<ToolRegistration>> {
  const res = await fetchWithTimeout("/api/tools", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data),
  });
  return handleResponse<ToolRegistration>(res);
}

export async function updateTool(
  id: string,
  data: UpdateToolRequest,
): Promise<ApiResult<ToolRegistration>> {
  const res = await fetchWithTimeout(
    `/api/tools/${encodeURIComponent(id)}`,
    {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(data),
    },
  );
  return handleResponse<ToolRegistration>(res);
}

export async function deleteTool(id: string): Promise<void> {
  const res = await fetchWithTimeout(
    `/api/tools/${encodeURIComponent(id)}`,
    { method: "DELETE" },
  );
  if (res.status === 204) return;
  if (!res.ok) {
    let body: ApiResult<unknown> | undefined;
    try {
      body = await res.json();
    } catch {}
    throw new ApiError(
      res.status,
      body?.errors,
      body?.errorCode,
      body?.message ?? `Delete failed with status ${res.status}`,
    );
  }
}

// ── MCP sub-tools ──

export async function getMcpTools(
  toolId: string,
): Promise<ApiResult<McpToolItem[]>> {
  const res = await fetchWithTimeout(
    `/api/tools/${encodeURIComponent(toolId)}/mcp-tools`,
  );
  return handleResponse<McpToolItem[]>(res);
}

// ── Invoke ──

export async function invokeTool(
  toolId: string,
  data: InvokeToolRequest,
): Promise<ApiResult<ToolInvocationResult>> {
  const res = await fetchWithTimeout(
    `/api/tools/${encodeURIComponent(toolId)}/invoke`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(data),
    },
  );
  return handleResponse<ToolInvocationResult>(res);
}

// ── OpenAPI Import ──

export async function importOpenApi(
  file: File,
  baseUrl?: string,
  authConfig?: {
    authType: string;
    credential?: string;
    apiKeyHeaderName?: string;
  },
): Promise<ApiResult<OpenApiImportResult>> {
  const formData = new FormData();
  formData.append("file", file);
  if (baseUrl) formData.append("baseUrl", baseUrl);
  if (authConfig) {
    formData.append("authConfig.authType", authConfig.authType);
    if (authConfig.credential)
      formData.append("authConfig.credential", authConfig.credential);
    if (authConfig.apiKeyHeaderName)
      formData.append(
        "authConfig.apiKeyHeaderName",
        authConfig.apiKeyHeaderName,
      );
  }

  const res = await fetchWithTimeout("/api/tools/import-openapi", {
    method: "POST",
    body: formData,
  });
  return handleResponse<OpenApiImportResult>(res);
}

// ── Available Functions (flat tool picker list) ──

export async function getAvailableFunctions(params?: {
  search?: string;
  status?: string;
}): Promise<ApiResult<BindableTool[]>> {
  const sp = new URLSearchParams();
  if (params?.search) sp.set("search", params.search);
  if (params?.status) sp.set("status", params.status);
  const qs = sp.toString();
  const url = qs ? `/api/tools/available-functions?${qs}` : "/api/tools/available-functions";
  const res = await fetchWithTimeout(url);
  return handleResponse<BindableTool[]>(res);
}
