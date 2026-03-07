// =============================================================================
// AlertRule Type Definitions — maps to backend C# DTOs & Enums
// =============================================================================

import type { ApiResult } from "@/types/agent";
export type { ApiResult };

// ---------------------------------------------------------------------------
// Enums
// ---------------------------------------------------------------------------

export const ALERT_RULE_STATUSES = ["Active", "Inactive"] as const;
export type AlertRuleStatus = (typeof ALERT_RULE_STATUSES)[number];

export const INCIDENT_SEVERITIES = ["P1", "P2", "P3", "P4"] as const;
export type IncidentSeverity = (typeof INCIDENT_SEVERITIES)[number];

export const MATCH_OPS = ["Eq", "Neq", "Regex", "NotRegex"] as const;
export type MatchOp = (typeof MATCH_OPS)[number];

export const MATCH_OP_LABELS: Record<MatchOp, string> = {
  Eq: "等于 (=)",
  Neq: "不等于 (≠)",
  Regex: "正则匹配 (~)",
  NotRegex: "正则不匹配 (!~)",
};

export const INCIDENT_ROUTES = ["SopExecution", "RootCauseAnalysis", "FallbackRca"] as const;
export type IncidentRoute = (typeof INCIDENT_ROUTES)[number];

export const ROUTE_LABELS: Record<IncidentRoute, string> = {
  SopExecution: "SOP 自动执行",
  RootCauseAnalysis: "根因分析",
  FallbackRca: "降级根因分析",
};

// ---------------------------------------------------------------------------
// DTOs (mirror backend AlertRuleDtos.cs)
// ---------------------------------------------------------------------------

export interface AlertMatcher {
  label: string;
  operator: MatchOp;
  value: string;
}

export interface AlertRuleDto {
  id: string;
  name: string;
  description: string | null;
  severity: IncidentSeverity;
  status: AlertRuleStatus;
  matchers: AlertMatcher[];
  sopId: string | null;
  responderAgentId: string | null;
  teamAgentId: string | null;
  summarizerAgentId: string | null;
  cooldownMinutes: number;
  notificationChannels: string[];
  createdAt: string;
  updatedAt: string | null;

  // Spec 025 — Canary & Health
  canaryMode: boolean;
  canarySopId: string | null;
  maxConsecutiveFailures: number;
  healthScore: number | null;
  healthDetails: AlertRuleHealth | null;

  // Spec 027 — Context Init Providers
  contextProviders: ContextInitItem[];
}

// ---------------------------------------------------------------------------
// Health & Canary DTOs (Spec 025)
// ---------------------------------------------------------------------------

export interface HealthFactor {
  name: string;
  weight: number;
  earned: number;
  detail: string;
}

export interface AlertRuleHealth {
  score: number;
  factors: HealthFactor[];
  recommendations: string[];
}

export interface CanaryResultSummary {
  incidentId: string;
  isConsistent: boolean;
  shadowRootCause: string | null;
  actualRootCause: string | null;
  shadowTokenConsumed: number;
  shadowDurationMs: number;
  createdAt: string;
}

export interface CanaryReport {
  alertRuleId: string;
  canarySopId: string | null;
  totalResults: number;
  consistencyRate: number;
  averageTokenDifference: number;
  results: CanaryResultSummary[];
}

// ---------------------------------------------------------------------------
// Context Init (Spec 027) — maps to backend ContextInitItemVO / ContextInitResultVO
// ---------------------------------------------------------------------------

export const CONTEXT_INIT_CATEGORIES = ["Metrics", "Logs", "Tracing", "Alerting", "Deployment", "Git"] as const;
export type ContextInitCategory = (typeof CONTEXT_INIT_CATEGORIES)[number];

export interface ContextInitItem {
  category: string;
  expression: string;
  label?: string;
  lookback?: string;
  extraParams?: Record<string, string>;
}

export interface ContextInitEntry {
  label: string;
  category: string;
  success: boolean;
  result?: string;
  errorMessage?: string;
  duration: string; // TimeSpan serialized
}

export interface ContextInitResult {
  entries: ContextInitEntry[];
  totalDuration: string;
  hasAnySuccess: boolean;
}

// ---------------------------------------------------------------------------
// Request DTOs (mirror backend CreateAlertRuleRequest / UpdateAlertRuleRequest)
// ---------------------------------------------------------------------------

export interface CreateAlertRuleRequest {
  name: string;
  description?: string;
  severity: string;
  matchers: AlertMatcher[];
  sopId?: string;
  responderAgentId?: string;
  teamAgentId?: string;
  summarizerAgentId?: string;
  cooldownMinutes?: number;
  notificationChannels?: string[];
  contextProviders?: ContextInitItem[];
}

export interface UpdateAlertRuleRequest {
  name?: string;
  description?: string;
  severity?: string;
  status?: string;
  matchers?: AlertMatcher[];
  sopId?: string;
  responderAgentId?: string;
  teamAgentId?: string;
  summarizerAgentId?: string;
  cooldownMinutes?: number;
  notificationChannels?: string[];
  contextProviders?: ContextInitItem[];
}
