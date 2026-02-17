// =============================================================================
// DataSource Type Definitions — maps to backend C# DTOs
// See: docs/specs/DATASOURCE-SPEC-INDEX.md
// =============================================================================

import type { ApiResult } from "@/types/agent";
export type { ApiResult };

// ── Enums ──

export type DataSourceCategory = "Metrics" | "Logs" | "Tracing" | "Alerting" | "Deployment" | "Git";
export type DataSourceProduct =
  | "Prometheus" | "VictoriaMetrics" | "Mimir"
  | "Loki" | "Elasticsearch"
  | "Jaeger" | "Tempo"
  | "Alertmanager" | "PagerDuty"
  | "Kubernetes" | "ArgoCD"
  | "GitHub" | "GitLab";
export type DataSourceStatus = "Registered" | "Connected" | "Disconnected" | "Error";
export type AuthType = "None" | "ApiKey" | "Bearer" | "OAuth2";

export const DATA_SOURCE_CATEGORIES: DataSourceCategory[] = [
  "Metrics", "Logs", "Tracing", "Alerting", "Deployment", "Git",
];

export const DATA_SOURCE_PRODUCTS: DataSourceProduct[] = [
  "Prometheus", "VictoriaMetrics", "Mimir",
  "Loki", "Elasticsearch",
  "Jaeger", "Tempo",
  "Alertmanager", "PagerDuty",
  "Kubernetes", "ArgoCD",
  "GitHub", "GitLab",
];

export const DATA_SOURCE_STATUSES: DataSourceStatus[] = [
  "Registered", "Connected", "Disconnected", "Error",
];

export const AUTH_TYPES: AuthType[] = ["None", "ApiKey", "Bearer", "OAuth2"];

// Category → Product mapping
export const CATEGORY_PRODUCTS: Record<DataSourceCategory, DataSourceProduct[]> = {
  Metrics: ["Prometheus", "VictoriaMetrics", "Mimir"],
  Logs: ["Loki", "Elasticsearch"],
  Tracing: ["Jaeger", "Tempo"],
  Alerting: ["Alertmanager", "PagerDuty"],
  Deployment: ["Kubernetes", "ArgoCD"],
  Git: ["GitHub", "GitLab"],
};

// ── DTOs ──

export interface DataSourceRegistration {
  id: string;
  name: string;
  description?: string;
  category: string;
  product: string;
  status: string;
  connectionConfig: DataSourceConnectionConfig;
  defaultQueryConfig?: DataSourceQueryConfig;
  healthCheck?: DataSourceHealthCheck;
  metadata?: DataSourceMetadata;
  createdAt: string;
  updatedAt?: string;
}

export interface DataSourceConnectionConfig {
  baseUrl: string;
  authType: string;
  hasCredential: boolean;
  maskedCredential?: string;
  authHeaderName?: string;
  tlsSkipVerify: boolean;
  timeoutSeconds: number;
  customHeaders?: Record<string, string>;
  namespace?: string;
  organization?: string;
}

export interface DataSourceQueryConfig {
  defaultLabels?: Record<string, string>;
  defaultNamespace?: string;
  maxResults?: number;
  defaultStep?: string;
  defaultIndex?: string;
}

export interface DataSourceHealthCheck {
  lastCheckAt?: string;
  isHealthy: boolean;
  errorMessage?: string;
  version?: string;
  responseTimeMs?: number;
}

export interface DataSourceMetadata {
  discoveredAt?: string;
  labels?: string[];
  indices?: string[];
  services?: string[];
  namespaces?: string[];
  availableFunctions?: string[];
}

export interface DataSourceListResponse {
  items: DataSourceRegistration[];
  totalCount: number;
}

// ── Create / Update request types ──

export interface CreateDataSourceRequest {
  name: string;
  description?: string;
  category: string;
  product: string;
  connectionConfig: CreateDataSourceConnectionConfig;
  defaultQueryConfig?: CreateDataSourceQueryConfig;
}

export interface CreateDataSourceConnectionConfig {
  baseUrl: string;
  authType: string;
  credential?: string;
  authHeaderName?: string;
  tlsSkipVerify: boolean;
  timeoutSeconds: number;
  customHeaders?: Record<string, string>;
  namespace?: string;
  organization?: string;
  kubeConfig?: string;
}

export interface CreateDataSourceQueryConfig {
  defaultLabels?: Record<string, string>;
  defaultNamespace?: string;
  maxResults?: number;
  defaultStep?: string;
  defaultIndex?: string;
}

export interface UpdateDataSourceRequest {
  name: string;
  description?: string;
  connectionConfig: CreateDataSourceConnectionConfig;
  defaultQueryConfig?: CreateDataSourceQueryConfig;
}

// ── Query request/response types (maps to backend DataSourceQueryVO / DataSourceResultVO) ──

export interface DataSourceTimeRange {
  start: string;   // ISO 8601
  end: string;     // ISO 8601
  step?: string;   // e.g. "15s", "1m"
}

export interface DataSourceLabelFilter {
  key: string;
  operator: "Eq" | "Neq" | "Regex" | "NotRegex";
  value: string;
}

export interface DataSourceQueryRequest {
  expression?: string;
  timeRange?: DataSourceTimeRange;
  filters?: DataSourceLabelFilter[];
  pagination?: { offset: number; limit: number };
  additionalParams?: Record<string, string>;
}

export interface DataSourceQueryResult {
  resultType: number | string;  // 0=TimeSeries,1=LogEntries,2=Spans,3=Alerts,4=Resources
  timeSeries?: DataSourceTimeSeries[];
  logEntries?: DataSourceLogEntry[];
  spans?: DataSourceSpan[];
  alerts?: DataSourceAlert[];
  resources?: DataSourceResource[];
  totalCount?: number;
  truncated?: boolean;
}

export interface DataSourceTimeSeries {
  metricName: string;
  labels: Record<string, string>;
  dataPoints: { timestamp: string; value: number }[];
}

export interface DataSourceLogEntry {
  timestamp: string;
  level?: string;
  message: string;
  labels?: Record<string, string>;
  source?: string;
  traceId?: string;
}

export interface DataSourceSpan {
  traceId: string;
  spanId: string;
  parentSpanId?: string;
  operationName: string;
  serviceName: string;
  durationMicros: number;
  status?: string;
  tags?: Record<string, string>;
  startTime: string;
}

export interface DataSourceAlert {
  alertId?: string;
  alertName: string;
  severity?: string;
  status: string;
  startsAt: string;
  endsAt?: string;
  labels?: Record<string, string>;
  annotations?: Record<string, string>;
  fingerprint?: string;
}

export interface DataSourceResource {
  kind: string;
  name: string;
  namespace?: string;
  status?: string;
  labels?: Record<string, string>;
  properties?: Record<string, unknown>;
  updatedAt?: string;
}

// ── Test connection / Discover response types ──

export interface DataSourceTestResult {
  isHealthy: boolean;
  version?: string;
  responseTimeMs?: number;
  errorMessage?: string;
}

export interface DataSourceDiscoverResult {
  labels?: string[];
  indices?: string[];
  services?: string[];
  namespaces?: string[];
}

// ── Display labels ──

export const categoryLabel: Record<DataSourceCategory, string> = {
  Metrics: "指标 (Metrics)",
  Logs: "日志 (Logs)",
  Tracing: "链路追踪 (Tracing)",
  Alerting: "告警 (Alerting)",
  Deployment: "部署 (Deployment)",
  Git: "Git",
};

export const statusLabel: Record<DataSourceStatus, string> = {
  Registered: "已注册",
  Connected: "已连接",
  Disconnected: "已断开",
  Error: "错误",
};

export const statusVariant: Record<DataSourceStatus, "default" | "secondary" | "destructive" | "outline"> = {
  Registered: "outline",
  Connected: "default",
  Disconnected: "secondary",
  Error: "destructive",
};

export const authLabel: Record<AuthType, string> = {
  None: "无认证",
  ApiKey: "API Key",
  Bearer: "Bearer Token",
  OAuth2: "OAuth 2.0",
};
