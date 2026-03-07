// =============================================================================
// Evaluation Type Definitions — maps to backend evaluation DTOs (Spec 023/025)
// =============================================================================

// ---------------------------------------------------------------------------
// Evaluation Dashboard (Spec 023)
// ---------------------------------------------------------------------------

export interface EvaluationDashboard {
  totalIncidents: number;
  autoResolveRate: number;
  averageMttrMs: number;
  mttrBySeverity: Record<string, number>;
  sopCoverageRate: number;
  humanInterventionRate: number;
  timeoutRate: number;
  rcaAccuracyRate: number | null;
  annotatedIncidentCount: number;
}

// ---------------------------------------------------------------------------
// SOP Effectiveness (Spec 023)
// ---------------------------------------------------------------------------

export interface SopEffectiveness {
  sopId: string;
  sopName: string;
  usageCount: number;
  successRate: number;
  averageExecutionMs: number;
  humanInterventionCount: number;
}

// ---------------------------------------------------------------------------
// Feedback Summary (Spec 025)
// ---------------------------------------------------------------------------

export interface FeedbackSummary {
  totalIncidents: number;
  fallbackCount: number;
  fallbackRate: number;
  canaryResultCount: number;
  canaryConsistencyRate: number;
  promptSuggestionsTotal: number;
  promptSuggestionsApplied: number;
  promptAdoptionRate: number;
  sopsAutoDisabledCount: number;
  sopsDegradedCount: number;
}
