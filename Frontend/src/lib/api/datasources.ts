// =============================================================================
// DataSource API Client — typed fetch wrapper functions
// =============================================================================

import type { ApiResult } from "@/types/agent";
import type {
  CreateDataSourceRequest,
  DataSourceDiscoverResult,
  DataSourceListResponse,
  DataSourceQueryRequest,
  DataSourceQueryResult,
  DataSourceRegistration,
  DataSourceTestResult,
  UpdateDataSourceRequest,
} from "@/types/datasource";
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

export async function getDataSources(params?: {
  category?: string;
  status?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}): Promise<ApiResult<DataSourceListResponse>> {
  const sp = new URLSearchParams();
  if (params?.category) sp.set("category", params.category);
  if (params?.status) sp.set("status", params.status);
  if (params?.search) sp.set("search", params.search);
  if (params?.page) sp.set("page", String(params.page));
  if (params?.pageSize) sp.set("pageSize", String(params.pageSize));
  const qs = sp.toString();
  const url = qs ? `/api/datasources?${qs}` : "/api/datasources";
  const res = await fetchWithTimeout(url);
  return handleResponse<DataSourceListResponse>(res);
}

export async function getDataSourceById(
  id: string,
): Promise<ApiResult<DataSourceRegistration>> {
  const res = await fetchWithTimeout(
    `/api/datasources/${encodeURIComponent(id)}`,
  );
  return handleResponse<DataSourceRegistration>(res);
}

export async function createDataSource(
  data: CreateDataSourceRequest,
): Promise<ApiResult<DataSourceRegistration>> {
  const res = await fetchWithTimeout("/api/datasources", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data),
  });
  return handleResponse<DataSourceRegistration>(res);
}

export async function updateDataSource(
  id: string,
  data: UpdateDataSourceRequest,
): Promise<ApiResult<DataSourceRegistration>> {
  const res = await fetchWithTimeout(
    `/api/datasources/${encodeURIComponent(id)}`,
    {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(data),
    },
  );
  return handleResponse<DataSourceRegistration>(res);
}

export async function deleteDataSource(id: string): Promise<void> {
  const res = await fetchWithTimeout(
    `/api/datasources/${encodeURIComponent(id)}`,
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

// ── Query ──

export async function queryDataSource(
  id: string,
  query: DataSourceQueryRequest,
): Promise<ApiResult<DataSourceQueryResult>> {
  const res = await fetchWithTimeout(
    `/api/datasources/${encodeURIComponent(id)}/query`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(query),
    },
  );
  return handleResponse<DataSourceQueryResult>(res);
}

// ── Test Connection ──

export async function testDataSourceConnection(
  id: string,
): Promise<ApiResult<DataSourceTestResult>> {
  const res = await fetchWithTimeout(
    `/api/datasources/${encodeURIComponent(id)}/test`,
    { method: "POST" },
  );
  return handleResponse<DataSourceTestResult>(res);
}

// ── Discover Metadata ──

export async function discoverMetadata(
  id: string,
): Promise<ApiResult<DataSourceDiscoverResult>> {
  const res = await fetchWithTimeout(
    `/api/datasources/${encodeURIComponent(id)}/discover`,
    { method: "POST" },
  );
  return handleResponse<DataSourceDiscoverResult>(res);
}
