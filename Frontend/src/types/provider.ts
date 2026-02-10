// =============================================================================
// Provider Type Definitions — maps to backend C# DTOs
// See: specs/006-llm-provider-config/data-model.md
// =============================================================================

import type { ApiResult } from "@/types/agent";

// ---------------------------------------------------------------------------
// Provider Detail (full)
// ---------------------------------------------------------------------------

/** Maps to backend LlmProviderDto — GET /api/providers/{id} */
export interface LlmProvider {
  id: string;
  name: string;
  baseUrl: string;
  maskedApiKey: string;
  discoveredModels: string[];
  modelsRefreshedAt: string | null;
  createdAt: string;
  updatedAt: string | null;
}

// ---------------------------------------------------------------------------
// Provider Summary (list item)
// ---------------------------------------------------------------------------

/** Maps to backend LlmProviderSummaryDto — GET /api/providers */
export interface LlmProviderSummary {
  id: string;
  name: string;
  baseUrl: string;
  modelCount: number;
  createdAt: string;
}

// ---------------------------------------------------------------------------
// Discovered Model
// ---------------------------------------------------------------------------

/** Maps to backend DiscoveredModelDto */
export interface DiscoveredModel {
  id: string;
}

// ---------------------------------------------------------------------------
// Command Types (Request Bodies)
// ---------------------------------------------------------------------------

/** Maps to RegisterProviderCommand — POST /api/providers */
export interface CreateProviderRequest {
  name: string;
  baseUrl: string;
  apiKey: string;
}

/** Maps to UpdateProviderCommand — PUT /api/providers/{id} */
export interface UpdateProviderRequest {
  name: string;
  baseUrl: string;
  apiKey?: string;
}

export type { ApiResult };
