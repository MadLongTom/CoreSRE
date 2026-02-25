// =============================================================================
// Incident Type Definitions — maps to backend C# DTOs & Enums
// =============================================================================

import type { ApiResult } from "@/types/agent";
export type { ApiResult };

// ---------------------------------------------------------------------------
// Enums (string literal unions mirroring backend C# enums)
// ---------------------------------------------------------------------------

export const INCIDENT_STATUSES = [
  "Open",
  "Investigating",
  "Mitigated",
  "Resolved",
  "Closed",
  "Escalated",
] as const;
export type IncidentStatus = (typeof INCIDENT_STATUSES)[number];

export const INCIDENT_SEVERITIES = [
  "P1",
  "P2",
  "P3",
  "P4",
] as const;
export type IncidentSeverity = (typeof INCIDENT_SEVERITIES)[number];

export const INCIDENT_ROUTES = [
  "SopExecution",
  "RootCauseAnalysis",
  "SopGeneration",
] as const;
export type IncidentRoute = (typeof INCIDENT_ROUTES)[number];

export const TIMELINE_EVENT_TYPES = [
  "AlertReceived",
  "StatusChanged",
  "AgentMessage",
  "ToolInvoked",
  "SopStepCompleted",
  "RcaCompleted",
  "SopGenerated",
  "Resolved",
  "Escalated",
  "ManualNote",
  "Timeout",
] as const;
export type TimelineEventType = (typeof TIMELINE_EVENT_TYPES)[number];

// ---------------------------------------------------------------------------
// Display labels
// ---------------------------------------------------------------------------

export const SEVERITY_LABELS: Record<IncidentSeverity, string> = {
  P1: "P1 — Critical",
  P2: "P2 — High",
  P3: "P3 — Medium",
  P4: "P4 — Low",
};

export const STATUS_LABELS: Record<IncidentStatus, string> = {
  Open: "待处理",
  Investigating: "处理中",
  Mitigated: "已缓解",
  Resolved: "已解决",
  Closed: "已关闭",
  Escalated: "已上报",
};

export const ROUTE_LABELS: Record<IncidentRoute, string> = {
  SopExecution: "SOP 自动执行",
  RootCauseAnalysis: "根因分析",
  SopGeneration: "SOP 生成",
};

// ---------------------------------------------------------------------------
// DTOs
// ---------------------------------------------------------------------------

/** Maps to backend IncidentSummaryDto */
export interface IncidentSummary {
  id: string;
  title: string;
  status: IncidentStatus;
  severity: IncidentSeverity;
  route: IncidentRoute;
  alertName: string;
  alertFingerprint: string | null;
  alertRuleId: string;
  createdAt: string;
  updatedAt: string | null;
}

/** Maps to backend IncidentDetailDto */
export interface IncidentDetail {
  id: string;
  title: string;
  status: IncidentStatus;
  severity: IncidentSeverity;
  route: IncidentRoute;
  alertName: string;
  alertFingerprint: string | null;
  alertRuleId: string;
  conversationId: string | null;
  rootCause: string | null;
  resolution: string | null;
  generatedSopId: string | null;
  alertLabels: Record<string, string> | null;
  timeToDetect: string | null;
  timeToResolve: string | null;
  timeline: IncidentTimelineItem[];
  createdAt: string;
  updatedAt: string | null;
}

/** Maps to backend IncidentTimelineItemDto */
export interface IncidentTimelineItem {
  eventType: TimelineEventType;
  summary: string;
  timestamp: string;
  actorAgentId: string | null;
  metadata: Record<string, string> | null;
}

// ---------------------------------------------------------------------------
// SignalR Events (mirrors backend IIncidentClient)
// ---------------------------------------------------------------------------

export interface IncidentCreatedEvent {
  incidentId: string;
  title: string;
  status: string;
  severity: string;
  route: string;
  alertName: string;
  alertRuleId: string;
  createdAt: string;
}

export interface IncidentStatusChangedEvent {
  incidentId: string;
  oldStatus: string;
  newStatus: string;
  timestamp: string;
}

export interface TimelineEventAddedPayload {
  incidentId: string;
  eventType: string;
  summary: string;
  timestamp: string;
  actorAgentId: string | null;
  metadata: Record<string, string> | null;
}

export interface ChatMessagePayload {
  incidentId: string;
  role: string;
  content: string;
  agentName: string | null;
  timestamp: string | null;
}

export interface IncidentResolvedEvent {
  incidentId: string;
  resolution: string | null;
  resolvedAt: string;
}

export interface RcaCompletedEvent {
  incidentId: string;
  rootCause: string;
  timestamp: string;
}

export interface SopGeneratedEvent {
  incidentId: string;
  skillId: string;
  sopName: string;
  timestamp: string;
}
