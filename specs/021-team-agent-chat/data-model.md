# Data Model: Team Mode Agent Chat

**Spec**: [spec.md](spec.md) | **Research**: [research.md](research.md) | **Date**: 2026-02-17

## Existing Entities (No Changes)

These entities already exist and require **no schema/field modifications**:

### AgentRegistration (aggregate root)

| Field | Type | Notes |
|---|---|---|
| Id | Guid | PK (BaseEntity) |
| Name | string | Human-readable agent name |
| Description | string? | Agent description |
| AgentType | AgentType | Enum: ChatClient, A2A, Team, Workflow |
| Status | AgentStatus | Enum: Active, Inactive, Error |
| TeamConfig | TeamConfigVO? | JSONB — only populated when AgentType == Team |
| LlmConfig | LlmConfigVO? | null for Team agents |
| ... | ... | Other fields (ToolRefs, DataSourceRefs, etc.) |

Factory: `AgentRegistration.CreateTeam(name, description, teamConfig)`

### TeamConfigVO (value object, JSONB)

| Field | Type | Mode | Notes |
|---|---|---|---|
| Mode | TeamMode | All | Enum: Sequential, Concurrent, RoundRobin, Handoffs, Selector, MagneticOne |
| ParticipantIds | List\<Guid\> | All | References to non-Team AgentRegistration IDs (min 1) |
| MaxIterations | int | All | Default 40; prevents infinite loops |
| HandoffRoutes | Dict\<Guid, List\<HandoffTargetVO\>\>? | Handoffs | Source → targets routing table |
| InitialAgentId | Guid? | Handoffs | First agent to receive the message |
| SelectorProviderId | Guid? | Selector | LLM provider for next-speaker selection |
| SelectorModelId | string? | Selector | LLM model ID for selection |
| SelectorPrompt | string? | Selector | Custom selection prompt |
| AllowRepeatedSpeaker | bool | Selector | Default true |
| OrchestratorProviderId | Guid? | MagneticOne | LLM provider for orchestrator |
| OrchestratorModelId | string? | MagneticOne | LLM model for orchestrator |
| MaxStalls | int | MagneticOne | Default 3; force final answer threshold |
| FinalAnswerPrompt | string? | MagneticOne | Prompt for final answer generation |
| AggregationStrategy | string? | Concurrent | "Merge" / "Vote" |

Validation: enforced in `TeamConfigVO.Create()` factory method.

### HandoffTargetVO (value object, nested JSONB)

| Field | Type | Notes |
|---|---|---|
| TargetAgentId | Guid | Must be in ParticipantIds |
| Reason | string? | Optional handoff description |

### TeamMode (enum)

Values: `Sequential`, `Concurrent`, `RoundRobin`, `Handoffs`, `Selector`, `MagneticOne`

### Conversation (aggregate root)

| Field | Type | Notes |
|---|---|---|
| Id | Guid | PK (BaseEntity) |
| AgentId | Guid | FK to AgentRegistration — can be a **Team** agent ID |
| Title | string? | Auto-generated from first message |

No structural changes. Team conversations bind to the Team agent's `AgentId`.

### AgentSessionRecord (entity)

| Field | Type | Notes |
|---|---|---|
| AgentId | string | Composite PK part 1 |
| ConversationId | string | Composite PK part 2 |
| SessionData | JsonElement | Opaque JSONB — framework serializes session state |
| SessionType | string | e.g., "ChatClientAgentSession" |

No structural changes. Session data format is controlled by the framework.

---

## Modified Data Structures (No DB Migration)

### ChatMessage Projection (frontend DTO, from SessionData JSONB)

The `ChatMessageDto` projected from `AgentSessionRecord.SessionData` gains optional participant attribution fields:

| Field | Type | Change | Notes |
|---|---|---|---|
| Index | int | Existing | Message sequence number |
| Role | string | Existing | "user" / "assistant" / "tool" |
| Content | string | Existing | Message text |
| ToolCalls | ToolCallDto[]? | Existing | Tool invocations |
| MemoryContext | string? | Existing | Injected semantic memory |
| **ParticipantAgentId** | **string?** | **NEW** | Originating participant agent GUID (null for non-Team) |
| **ParticipantAgentName** | **string?** | **NEW** | Originating participant agent name (null for non-Team) |

This is a **DTO-only change** — the JSONB data written by the framework's `AgentResponseUpdate.AgentId`/`AgentName` is read during session projection. No schema migration required.

### Frontend ChatMessage Type (TypeScript)

```typescript
// In Frontend/src/types/chat.ts — additions to existing interface
export interface ChatMessage {
  index: number;
  role: "user" | "assistant" | "tool";
  content: string;
  toolCalls?: ToolCall[];
  memoryContext?: string | null;
  participantAgentId?: string;   // NEW — Team mode attribution
  participantAgentName?: string; // NEW — Team mode attribution
}
```

---

## New Data Structures

### TeamChatEventDto (backend SSE event DTOs)

Location: `Backend/CoreSRE.Application/Chat/DTOs/TeamChatEventDto.cs`

```csharp
/// Base for team-specific SSE event data
public abstract record TeamChatEventDto(string EventType);

/// Handoff notification: "🔀 Agent A → Agent B"
public sealed record TeamHandoffEventDto(
    string FromAgentId,
    string FromAgentName,
    string ToAgentId,
    string ToAgentName
) : TeamChatEventDto("TEAM_HANDOFF");

/// Ledger update for MagneticOne mode
public sealed record TeamLedgerUpdateEventDto(
    string LedgerType,      // "outer" | "inner"
    string? AgentName,       // null for outer ledger
    string Content           // JSON string of ledger data
) : TeamChatEventDto("TEAM_LEDGER_UPDATE");

/// Progress indicator for all team modes
public sealed record TeamProgressEventDto(
    string CurrentAgentId,
    string CurrentAgentName,
    int? Step,
    int? TotalSteps,
    string Mode              // TeamMode as string
) : TeamChatEventDto("TEAM_PROGRESS");
```

### MagneticOne Ledger State (in-memory, not persisted)

```csharp
/// Outer ledger maintained by MagneticOneGroupChatManager
public sealed record OuterLedger
{
    public string Facts { get; set; } = "";
    public string Plan { get; set; } = "";
    public string NextStep { get; set; } = "";
    public string Progress { get; set; } = "";
    public bool IsComplete { get; set; }
}

/// Inner ledger entry
public sealed record InnerLedgerEntry(
    string AgentName,
    string Task,
    string Status,     // "running" | "completed" | "failed"
    string? Summary,
    DateTime Timestamp
);
```

### Frontend Ledger Types (TypeScript)

Location: `Frontend/src/types/chat.ts`

```typescript
export interface OuterLedger {
  facts: string;
  plan: string;
  nextStep: string;
  progress: string;
  isComplete: boolean;
}

export interface InnerLedgerEntry {
  agentName: string;
  task: string;
  status: "running" | "completed" | "failed";
  summary?: string;
  timestamp: string;
}

export interface TeamProgress {
  currentAgentId: string;
  currentAgentName: string;
  step?: number;
  totalSteps?: number;
  mode: string;
}
```

---

## Entity Relationships

```
AgentRegistration (Team)
  ├── TeamConfigVO (JSONB)
  │     ├── ParticipantIds → AgentRegistration[] (ChatClient/A2A only)
  │     ├── HandoffRoutes → HandoffTargetVO[] (Handoffs mode)
  │     ├── SelectorProviderId → LlmProvider (Selector mode)
  │     └── OrchestratorProviderId → LlmProvider (MagneticOne mode)
  │
  └── Conversation (bound by AgentId)
        └── AgentSessionRecord (SessionData JSONB)
              └── messages[] (projected as ChatMessageDto with participant attribution)
```

---

## State Transitions

### Team Orchestration Lifecycle

```
IDLE → RESOLVING → ORCHESTRATING → STREAMING → COMPLETED
                                      ↓
                                    ERROR
```

| State | Description |
|---|---|
| IDLE | No active team conversation |
| RESOLVING | Loading participant agents, validating all active (FR-008) |
| ORCHESTRATING | Workflow executing (mode-specific agent selection/execution) |
| STREAMING | SSE events being sent to frontend |
| COMPLETED | `RUN_FINISHED` sent, session persisted |
| ERROR | `RUN_ERROR` sent with participant agent name (FR-011) |

### MagneticOne Dual-Loop State

```
OUTER_PLAN → INNER_EXECUTE → OUTER_REVIEW → (loop or FINAL_ANSWER)
                                   ↓
                              STALLED (maxStalls hit → FINAL_ANSWER)
```

---

## Validation Rules

| Rule | Source | Layer |
|---|---|---|
| ParticipantIds not empty | TeamConfigVO.Create() | Domain |
| No Team-type participants (no nesting) | AgentResolverService | Infrastructure |
| All participants Active status | AgentResolverService (FR-008) | Infrastructure |
| All participants exist in DB | AgentResolverService (FR-008) | Infrastructure |
| Handoffs: InitialAgentId in ParticipantIds | TeamConfigVO.Create() | Domain |
| Handoffs: route sources/targets in ParticipantIds | TeamConfigVO.Create() | Domain |
| Selector: SelectorProviderId not null | TeamConfigVO.Create() | Domain |
| MagneticOne: OrchestratorProviderId not null | TeamConfigVO.Create() | Domain |
| MaxIterations > 0 | TeamConfigVO.Create() | Domain |
| Participant name uniqueness (for Handoffs tool naming) | TeamOrchestratorService | Infrastructure |
