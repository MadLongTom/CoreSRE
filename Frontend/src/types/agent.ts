// =============================================================================
// Agent Type Definitions — maps to backend C# DTOs
// See: specs/005-frontend-pages/data-model.md
// =============================================================================

// ---------------------------------------------------------------------------
// Enums (string literal unions)
// ---------------------------------------------------------------------------

/** Maps to backend AgentType enum: A2A, ChatClient, Workflow, Team */
export type AgentType = "A2A" | "ChatClient" | "Workflow" | "Team";

/** Maps to backend AgentStatus enum: Registered, Active, Inactive, Error */
export type AgentStatus = "Registered" | "Active" | "Inactive" | "Error";

/** All valid agent types for iteration */
export const AGENT_TYPES: AgentType[] = ["A2A", "ChatClient", "Workflow", "Team"];

/** Maps to backend TeamMode enum */
export type TeamMode =
  | "Sequential"
  | "Concurrent"
  | "RoundRobin"
  | "Handoffs"
  | "Selector"
  | "MagneticOne";

/** All valid team modes for iteration */
export const TEAM_MODES: TeamMode[] = [
  "Sequential",
  "Concurrent",
  "RoundRobin",
  "Handoffs",
  "Selector",
  "MagneticOne",
];

/** Team mode display labels */
export const TEAM_MODE_LABELS: Record<TeamMode, string> = {
  Sequential: "顺序管道",
  Concurrent: "并发聚合",
  RoundRobin: "轮询",
  Handoffs: "交接/Swarm",
  Selector: "LLM 选择",
  MagneticOne: "MagneticOne",
};

/** Team mode descriptions */
export const TEAM_MODE_DESCRIPTIONS: Record<TeamMode, string> = {
  Sequential: "Agent 按顺序依次处理，前一个的输出作为后一个的输入",
  Concurrent: "所有 Agent 同时处理相同输入，结果聚合",
  RoundRobin: "Agent 按固定顺序循环发言",
  Handoffs: "Agent 根据路由规则自主决定交接给下一个 Agent",
  Selector: "由 LLM 动态选择下一个发言的 Agent",
  MagneticOne: "双循环账本编排，Orchestrator 管理 Agent 协作",
};

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
  teamConfig?: TeamConfig;
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

/** Maps to backend DataSourceRefVO — per-function datasource binding */
export interface DataSourceRef {
  dataSourceId: string;
  /** Enabled function names. null/undefined = all functions */
  enabledFunctions?: string[] | null;
  /** Spec 026: enable mutation tools (restart, scale, rollback) */
  enableMutations?: boolean;
}

/** Maps to backend LlmConfigDto */
export interface LlmConfig {
  providerId?: string | null;
  providerName?: string | null;
  modelId: string;
  instructions?: string;
  toolRefs: string[];
  dataSourceRefs?: DataSourceRef[];

  // ChatOptions extended configuration
  temperature?: number | null;
  maxOutputTokens?: number | null;
  topP?: number | null;
  topK?: number | null;
  frequencyPenalty?: number | null;
  presencePenalty?: number | null;
  seed?: number | null;
  stopSequences?: string[] | null;
  responseFormat?: string | null;
  responseFormatSchema?: string | null;
  toolMode?: string | null;
  allowMultipleToolCalls?: boolean | null;

  // Sandbox configuration (Kubernetes Pod container isolation)
  enableSandbox?: boolean | null;
  sandboxType?: string | null;
  sandboxImage?: string | null;
  sandboxCpus?: number | null;
  sandboxMemoryMib?: number | null;
  sandboxK8sNamespace?: string | null;
  sandboxMode?: string | null;
  sandboxInstanceId?: string | null;

  // Skill bindings
  skillRefs?: string[];

  // History & Memory configuration
  enableChatHistory?: boolean | null;
  maxHistoryMessages?: number | null;
  enableSemanticMemory?: boolean | null;
  embeddingProviderId?: string | null;
  embeddingProviderName?: string | null;
  embeddingModelId?: string | null;
  embeddingDimensions?: number | null;
  memorySearchMode?: string | null;
  memoryMaxResults?: number | null;
  memoryMinRelevanceScore?: number | null;
}

/** Maps to backend TeamConfigDto */
export interface TeamConfig {
  mode: string;
  participantIds: string[];
  maxIterations: number;
  // Handoffs
  handoffRoutes?: Record<string, HandoffTarget[]>;
  initialAgentId?: string | null;
  // Selector
  selectorProviderId?: string | null;
  selectorModelId?: string | null;
  selectorPrompt?: string | null;
  allowRepeatedSpeaker: boolean;
  // MagneticOne
  orchestratorProviderId?: string | null;
  orchestratorModelId?: string | null;
  maxStalls: number;
  finalAnswerPrompt?: string | null;
  // Concurrent
  aggregationStrategy?: string | null;
}

/** Maps to backend HandoffTargetDto */
export interface HandoffTarget {
  targetAgentId: string;
  reason?: string | null;
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
  teamConfig?: TeamConfig;
}

/** Maps to backend UpdateAgentCommand — PUT /api/agents/{id} body (no agentType) */
export interface UpdateAgentRequest {
  name: string;
  description?: string;
  endpoint?: string;
  agentCard?: AgentCard;
  llmConfig?: LlmConfig;
  workflowRef?: string;
  teamConfig?: TeamConfig;
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
