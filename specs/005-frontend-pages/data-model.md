# Data Model: 005-frontend-pages

> 前端 TypeScript 类型定义，映射后端 C# DTO。
> 所有类型定义于 `Frontend/src/types/agent.ts`。

## Entity Map

```text
┌──────────────────────────────────────────────────────────┐
│ API Response Wrapper                                     │
│  Result<T> { success, data?, message?, errors? }         │
└──────────────┬───────────────────────────────────────────┘
               │
    ┌──────────┴──────────┐
    │                     │
    ▼                     ▼
┌──────────────┐  ┌───────────────────┐
│ AgentSummary │  │ AgentRegistration │
│ (list item)  │  │ (full detail)     │
│              │  │                   │
│ id           │  │ id                │
│ name         │  │ name              │
│ agentType    │  │ description?      │
│ status       │  │ agentType         │
│ createdAt    │  │ status            │
└──────────────┘  │ endpoint?         │
                  │ agentCard? ───────┼──► AgentCard
                  │ llmConfig? ───────┼──► LlmConfig
                  │ workflowRef?      │
                  │ createdAt         │
                  │ updatedAt?        │
                  └───────────────────┘

┌──────────────────────┐          ┌──────────────────────┐
│ AgentCard            │          │ LlmConfig            │
│                      │          │                      │
│ skills: AgentSkill[] │          │ modelId              │
│ interfaces: Agent-   │          │ instructions?        │
│   Interface[]        │          │ toolRefs: string[]   │
│ securitySchemes:     │          └──────────────────────┘
│   SecurityScheme[]   │
└──────────────────────┘

┌──────────────────────────────────────────────────────┐
│ AgentSearchResponse                                  │
│                                                      │
│ results: AgentSearchResult[]                         │
│ searchMode: string                                   │
│ query: string                                        │
│ totalCount: number                                   │
│                                                      │
│  AgentSearchResult                                   │
│  ├── id, name, agentType, status, createdAt          │
│  ├── matchedSkills: MatchedSkill[]                   │
│  └── similarityScore?: number                        │
└──────────────────────────────────────────────────────┘
```

## Type Definitions

### Enums (string literal unions)

```typescript
/** Maps to backend AgentType enum: A2A, ChatClient, Workflow */
type AgentType = "A2A" | "ChatClient" | "Workflow";

/** Maps to backend AgentStatus enum: Registered, Active, Inactive, Error */
type AgentStatus = "Registered" | "Active" | "Inactive" | "Error";
```

### API Response Wrapper

```typescript
/** Maps to backend Result<T> */
interface ApiResult<T> {
  success: boolean;
  data?: T;
  message?: string;
  errors?: string[];
  errorCode?: number;
}
```

### Agent Summary (列表项)

```typescript
/** Maps to backend AgentSummaryDto — used in GET /api/agents list */
interface AgentSummary {
  id: string;           // Guid → string
  name: string;
  agentType: string;    // AgentType string
  status: string;       // AgentStatus string
  createdAt: string;    // DateTime → ISO 8601 string
}
```

### Agent Registration (完整详情)

```typescript
/** Maps to backend AgentRegistrationDto — used in GET/POST/PUT /api/agents/{id} */
interface AgentRegistration {
  id: string;
  name: string;
  description?: string;
  agentType: string;
  status: string;
  endpoint?: string;
  agentCard?: AgentCard;
  llmConfig?: LlmConfig;
  workflowRef?: string;   // Guid → string
  createdAt: string;
  updatedAt?: string;
}
```

### Nested Types

```typescript
/** Maps to backend AgentCardDto */
interface AgentCard {
  skills: AgentSkill[];
  interfaces: AgentInterface[];
  securitySchemes: SecurityScheme[];
}

/** Maps to backend AgentSkillDto */
interface AgentSkill {
  name: string;
  description?: string;
}

/** Maps to backend AgentInterfaceDto */
interface AgentInterface {
  protocol: string;
  path?: string;
}

/** Maps to backend SecuritySchemeDto */
interface SecurityScheme {
  type: string;
  parameters?: string;
}

/** Maps to backend LlmConfigDto */
interface LlmConfig {
  modelId: string;
  instructions?: string;
  toolRefs: string[];   // Guid[] → string[]
}
```

### Search Types

```typescript
/** Maps to backend AgentSearchResponse — returned by GET /api/agents/search */
interface AgentSearchResponse {
  results: AgentSearchResult[];
  searchMode: string;
  query: string;
  totalCount: number;
}

/** Maps to backend AgentSearchResultDto */
interface AgentSearchResult {
  id: string;
  name: string;
  agentType: string;
  status: string;
  createdAt: string;
  matchedSkills: MatchedSkill[];
  similarityScore?: number;
}

/** Maps to backend MatchedSkillDto */
interface MatchedSkill {
  name: string;
  description?: string;
}
```

### Command Types (Request Bodies)

```typescript
/** Maps to backend RegisterAgentCommand — POST /api/agents body */
interface CreateAgentRequest {
  name: string;
  description?: string;
  agentType: string;
  endpoint?: string;
  agentCard?: AgentCard;
  llmConfig?: LlmConfig;
  workflowRef?: string;
}

/** Maps to backend UpdateAgentCommand — PUT /api/agents/{id} body (no agentType) */
interface UpdateAgentRequest {
  name: string;
  description?: string;
  endpoint?: string;
  agentCard?: AgentCard;
  llmConfig?: LlmConfig;
  workflowRef?: string;
}
```

## Validation Rules (from backend FluentValidation)

| Field | Rule | Applies to |
|-------|------|------------|
| `name` | Required, max 200 chars | Create, Update |
| `agentType` | Required, must be valid AgentType value | Create only |
| `endpoint` | Optional, must be valid absolute URI if provided | Create, Update |
| `agentCard.skills[].name` | Required if agentCard provided | Create, Update |
| `agentCard.interfaces[].protocol` | Required if agentCard provided | Create, Update |
| `llmConfig.modelId` | Required if llmConfig provided | Create, Update |
| Search `q` | Required, non-empty | Search query |

## Type Mapping Notes

| C# Type | TypeScript Type | Notes |
|---------|----------------|-------|
| `Guid` | `string` | JSON 序列化为 string |
| `DateTime` | `string` | ISO 8601 格式，前端用 `new Date(str)` 或 `Intl.DateTimeFormat` 显示 |
| `List<T>` | `T[]` | JSON 序列化为数组 |
| `int` | `number` | |
| `double?` | `number \| undefined` | similarityScore |
| `bool` | `boolean` | |
| C# `enum` → string | string literal union | AgentType, AgentStatus |
