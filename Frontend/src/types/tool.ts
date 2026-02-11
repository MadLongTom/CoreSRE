// =============================================================================
// Tool Gateway Type Definitions — maps to backend C# DTOs
// See: specs/009-tool-gateway-crud/data-model.md
// =============================================================================

import type { ApiResult } from "@/types/agent";
export type { ApiResult };

export type ToolType = "RestApi" | "McpServer";
export type ToolStatus = "Active" | "Inactive";
export type TransportType = "Rest" | "StreamableHttp" | "Stdio" | "Sse" | "AutoDetect";
export type AuthType = "None" | "ApiKey" | "Bearer" | "OAuth2";

export const TOOL_TYPES: ToolType[] = ["RestApi", "McpServer"];
export const TOOL_STATUSES: ToolStatus[] = ["Active", "Inactive"];
export const REST_TRANSPORT_TYPES: TransportType[] = ["Rest"];
export const MCP_TRANSPORT_TYPES: TransportType[] = ["StreamableHttp", "Sse", "AutoDetect", "Stdio"];
export const AUTH_TYPES: AuthType[] = ["None", "ApiKey", "Bearer", "OAuth2"];
export const HTTP_METHODS = ["GET", "POST", "PUT", "DELETE", "PATCH"] as const;

export interface ToolRegistration {
  id: string;
  name: string;
  description?: string;
  toolType: string;
  status: string;
  connectionConfig: ConnectionConfig;
  authConfig: AuthConfig;
  toolSchema?: ToolSchema;
  discoveryError?: string;
  importSource?: string;
  mcpToolCount: number;
  createdAt: string;
  updatedAt?: string;
}

export interface ConnectionConfig {
  endpoint: string;
  transportType: string;
  httpMethod: string;
}

export interface AuthConfig {
  authType: string;
  hasCredential: boolean;
  maskedCredential?: string;
  apiKeyHeaderName?: string;
  tokenEndpoint?: string;
  clientId?: string;
  hasClientSecret: boolean;
}

export interface ToolSchema {
  inputSchema?: unknown;
  outputSchema?: unknown;
  annotations?: ToolAnnotations;
}

export interface ToolAnnotations {
  readOnly: boolean;
  destructive: boolean;
  idempotent: boolean;
  openWorldHint: boolean;
}

export interface McpToolItem {
  id: string;
  toolRegistrationId: string;
  toolName: string;
  description?: string;
  inputSchema?: unknown;
  outputSchema?: unknown;
  annotations?: ToolAnnotations;
  createdAt: string;
  updatedAt?: string;
}

export interface ToolListResponse {
  items: ToolRegistration[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface CreateToolRequest {
  name: string;
  description?: string;
  toolType: string;
  connectionConfig: {
    endpoint: string;
    transportType: string;
    httpMethod?: string;
  };
  authConfig: {
    authType: string;
    credential?: string;
    apiKeyHeaderName?: string;
    tokenEndpoint?: string;
    clientId?: string;
    clientSecret?: string;
  };
  inputSchema?: string;
}

export interface UpdateToolRequest {
  name: string;
  description?: string;
  connectionConfig: {
    endpoint: string;
    transportType: string;
    httpMethod?: string;
  };
  authConfig: {
    authType: string;
    credential?: string;
    apiKeyHeaderName?: string;
    tokenEndpoint?: string;
    clientId?: string;
    clientSecret?: string;
  };
  inputSchema?: string;
}

export interface InvokeToolRequest {
  mcpToolName?: string;
  parameters: Record<string, unknown>;
  queryParameters?: Record<string, string>;
  headerParameters?: Record<string, string>;
}

export interface ToolInvocationResult {
  success: boolean;
  data?: unknown;
  error?: string;
  durationMs: number;
  toolRegistrationId: string;
  invokedAt: string;
}

export interface OpenApiImportResult {
  totalOperations: number;
  importedCount: number;
  skippedCount: number;
  tools: ToolRegistration[];
  errors: string[];
}
