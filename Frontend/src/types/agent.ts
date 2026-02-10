// =============================================================================
// Agent Type Definitions — maps to backend C# DTOs
// See: specs/005-frontend-pages/data-model.md
// =============================================================================

// ---------------------------------------------------------------------------
// Enums (string literal unions)
// ---------------------------------------------------------------------------

/** Maps to backend AgentType enum: A2A, ChatClient, Workflow */
export type AgentType = "A2A" | "ChatClient" | "Workflow";

/** Maps to backend AgentStatus enum: Registered, Active, Inactive, Error */
export type AgentStatus = "Registered" | "Active" | "Inactive" | "Error";

/** All valid agent types for iteration */
export const AGENT_TYPES: AgentType[] = ["A2A", "ChatClient", "Workflow"];

/** All valid agent statuses for iteration */
export const AGENT_STATUSES: AgentStatus[] = [
  "Registered",
  "Active",
  "Inactive",
  "Error",
];

// ---------------------------------------------------------------------------
// API Response Wrapper
// ---------------------------------------------------------------------------

/** Maps to backend Result<T> */
export interface ApiResult<T> {
  success: boolean;
  data?: T;
  message?: string;
  errors?: string[];
  errorCode?: number;
}

// ---------------------------------------------------------------------------
// Agent Summary (list item)
// ---------------------------------------------------------------------------

/** Maps to backend AgentSummaryDto — used in GET /api/agents list */
export interface AgentSummary {
  id: string;
  name: string;
  agentType: string;
  status: string;
  createdAt: string;
}

// ---------------------------------------------------------------------------
// Agent Registration (full detail)
// ---------------------------------------------------------------------------

/** Maps to backend AgentRegistrationDto — used in GET/POST/PUT /api/agents/{id} */
export interface AgentRegistration {
  id: string;
  name: string;
  description?: string;
  agentType: string;
  status: string;
  endpoint?: string;
  agentCard?: AgentCard;
  llmConfig?: LlmConfig;
  workflowRef?: string;
  createdAt: string;
  updatedAt?: string;
}

// ---------------------------------------------------------------------------
// Nested Types
// ---------------------------------------------------------------------------

/** Maps to backend AgentCardDto */
export interface AgentCard {
  skills: AgentSkill[];
  interfaces: AgentInterface[];
  securitySchemes: SecurityScheme[];
}

/** Maps to backend AgentSkillDto */
export interface AgentSkill {
  name: string;
  description?: string;
}

/** Maps to backend AgentInterfaceDto */
export interface AgentInterface {
  protocol: string;
  path?: string;
}

/** Maps to backend SecuritySchemeDto */
export interface SecurityScheme {
  type: string;
  parameters?: string;
}

/** Maps to backend LlmConfigDto */
export interface LlmConfig {
  providerId?: string | null;
  providerName?: string | null;
  modelId: string;
  instructions?: string;
  toolRefs: string[];
}

// ---------------------------------------------------------------------------
// Search Types
// ---------------------------------------------------------------------------

/** Maps to backend AgentSearchResponse — returned by GET /api/agents/search */
export interface AgentSearchResponse {
  results: AgentSearchResult[];
  searchMode: string;
  query: string;
  totalCount: number;
}

/** Maps to backend AgentSearchResultDto */
export interface AgentSearchResult {
  id: string;
  name: string;
  agentType: string;
  status: string;
  createdAt: string;
  matchedSkills: MatchedSkill[];
  similarityScore?: number;
}

/** Maps to backend MatchedSkillDto */
export interface MatchedSkill {
  name: string;
  description?: string;
}

// ---------------------------------------------------------------------------
// Command Types (Request Bodies)
// ---------------------------------------------------------------------------

/** Maps to backend RegisterAgentCommand — POST /api/agents body */
export interface CreateAgentRequest {
  name: string;
  description?: string;
  agentType: string;
  endpoint?: string;
  agentCard?: AgentCard;
  llmConfig?: LlmConfig;
  workflowRef?: string;
}

/** Maps to backend UpdateAgentCommand — PUT /api/agents/{id} body (no agentType) */
export interface UpdateAgentRequest {
  name: string;
  description?: string;
  endpoint?: string;
  agentCard?: AgentCard;
  llmConfig?: LlmConfig;
  workflowRef?: string;
}

// ---------------------------------------------------------------------------
// Resolved AgentCard (from remote A2A endpoint)
// ---------------------------------------------------------------------------

/** Maps to backend ResolvedAgentCardDto — returned by POST /api/agents/resolve-card */
export interface ResolvedAgentCard {
  name: string;
  description: string;
  url: string;
  version: string;
  skills: AgentSkill[];
  interfaces: AgentInterface[];
  securitySchemes: SecurityScheme[];
}
