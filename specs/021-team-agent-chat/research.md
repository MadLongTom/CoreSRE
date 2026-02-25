# Research: Team Mode Agent Chat

**Spec**: [spec.md](spec.md) | **Date**: 2026-02-17

## Table of Contents

1. [Microsoft.Agents.AI.Workflows API](#1-microsoftagentsaiworkflows-api)
2. [Team Agent Resolution Strategy](#2-team-agent-resolution-strategy)
3. [SSE Protocol Extension for Participant Attribution](#3-sse-protocol-extension-for-participant-attribution)
4. [Session Persistence for Team Conversations](#4-session-persistence-for-team-conversations)
5. [GroupChatManager Customization (Selector + MagneticOne)](#5-groupchatmanager-customization-selector--magneticone)
6. [MagneticOne Ledger Data Model](#6-magneticone-ledger-data-model)
7. [Frontend Concurrent Bubble Strategy](#7-frontend-concurrent-bubble-strategy)
8. [Handoff Tool Naming with GUID Agent IDs](#8-handoff-tool-naming-with-guid-agent-ids)

---

## 1. Microsoft.Agents.AI.Workflows API

### Decision

Use `Microsoft.Agents.AI.Workflows 1.0.0-preview.260209.1` (already referenced in `CoreSRE.Infrastructure.csproj`, zero usage) to build all 6 orchestration modes. This is the correct package — it provides native workflow builders for Sequential, Concurrent, Handoffs, and GroupChat patterns.

### Rationale

- The package is *already a project dependency* — no new NuGet references needed.
- It provides high-level `AgentWorkflowBuilder` static methods that map directly to our 6 `TeamMode` values.
- `Workflow.AsAgent()` returns an `AIAgent`, which integrates seamlessly with the existing `ResolvedAgent` pattern.

### Alternatives Considered

| Alternative | Reason Rejected |
|---|---|
| Custom orchestration from scratch | Unnecessary duplication — the framework handles DAG execution, fan-out/fan-in, and handoff routing natively. |
| `Microsoft.Agents.Builder` (Bot Framework) | Wrong framework — CoreSRE uses the AI agent framework (`AIAgent`, `ChatClientAgent`), not the Bot Framework (`AgentApplication`, `IAgent`). |
| Existing `WorkflowExecutionEngine` in CoreSRE | That engine is for user-defined node graphs (workflow CRUD), not multi-agent chat orchestration. Different concern. |

### API Surface Summary

```csharp
// Sequential: output of agent N → input of agent N+1
var workflow = AgentWorkflowBuilder.BuildSequential(agent1, agent2, agent3);

// Concurrent: fan-out to all agents, aggregate results
var workflow = AgentWorkflowBuilder.BuildConcurrent([agent1, agent2], aggregator);

// Handoffs: triage agent chooses next via tool calls
var workflow = AgentWorkflowBuilder.BuildHandoffs(triageAgent)
    .WithHandoff(triageAgent, salesAgent)
    .Build();

// GroupChat: shared history, manager selects next speaker
var workflow = AgentWorkflowBuilder
    .BuildGroupChat(agents => new RoundRobinGroupChatManager(agents))
    .AddParticipants(agent1, agent2, agent3)
    .Build();

// Convert workflow to AIAgent for uniform consumption
AIAgent teamAgent = workflow.AsAgent(name: "TeamWorkflow");
```

### Mode → API Mapping

| TeamMode | Builder API | Notes |
|---|---|---|
| Sequential | `BuildSequential(agents...)` | Direct 1:1 mapping |
| Concurrent | `BuildConcurrent(agents, aggregator)` | Need custom `AggregatingExecutor` |
| RoundRobin | `BuildGroupChat(RoundRobinGroupChatManager)` | Built-in manager |
| Handoffs | `BuildHandoffs(initial).WithHandoff(...)` | Routes from `TeamConfigVO.HandoffRoutes` |
| Selector | `BuildGroupChat(LlmSelectorGroupChatManager)` | Custom manager needed |
| MagneticOne | `BuildGroupChat(MagneticOneGroupChatManager)` | Custom manager needed (dual-loop ledger) |

### Key Types

- `AgentResponseUpdate` — streaming chunk with `AgentId`, `AgentName` for attribution
- `AgentResponse` — final response with `AgentId`, `AgentName`, `Messages`
- `ChatMessage.AuthorName` — per-message speaker identity
- `GroupChatManager` — abstract base for custom next-speaker selection
- `RoundRobinGroupChatManager` — built-in, cycles participants
- `WorkflowBuilder` — low-level DAG builder for advanced patterns

---

## 2. Team Agent Resolution Strategy

### Decision

Extend `AgentResolverService.ResolveAsync()` to handle `AgentType.Team` by:
1. Loading the `TeamConfigVO` from `AgentRegistration`.
2. Resolving each participant agent via recursive `ResolveAsync()` calls (using existing ChatClient/A2A resolution).
3. Composing the resolved `AIAgent` instances into a `Workflow` via `AgentWorkflowBuilder`.
4. Wrapping the workflow as an `AIAgent` via `Workflow.AsAgent()`.
5. Returning a `ResolvedAgent` containing the composed workflow agent.

### Rationale

- Reuses the existing per-agent resolution logic (LLM config, tools, data sources, skills, sandbox).
- The `Workflow.AsAgent()` bridge produces a standard `AIAgent` so callers (chat endpoints) need minimal changes.
- Participant validation (all active, all non-Team) happens at resolve time, satisfying FR-008.

### Alternatives Considered

| Alternative | Reason Rejected |
|---|---|
| Separate `ITeamAgentResolver` | Would duplicate shared resolution logic. Better to extend the existing service. |
| Resolve at endpoint level | Violates DDD — agent resolution is Infrastructure concern, not API layer. |
| Lazy resolution (resolve participants on first use) | Could mask configuration errors; FR-008 requires upfront validation. |

### Interface

```csharp
// In CoreSRE.Application/Interfaces/ITeamOrchestrator.cs
public interface ITeamOrchestrator
{
    /// Build a Workflow-backed AIAgent from resolved participant agents and TeamConfig.
    AIAgent BuildTeamAgent(
        AgentRegistration teamRegistration,
        IReadOnlyList<ResolvedAgent> participants,
        CancellationToken ct = default);
}
```

The `ITeamOrchestrator` is called by `AgentResolverService` after participant resolution. It encapsulates the mode→builder mapping logic.

---

## 3. SSE Protocol Extension for Participant Attribution

### Decision

Extend the existing AG-UI SSE event stream with **participant metadata fields** in `TEXT_MESSAGE_START` and `TOOL_CALL_START` events. Add new event types for handoff notifications and ledger updates.

### Rationale

- The current SSE protocol already sends `TEXT_MESSAGE_START` with a `role` field. Adding `participantAgentId` and `participantAgentName` is backward-compatible (single-agent conversations simply omit them).
- Framework-level `AgentResponseUpdate` already carries `AgentId`/`AgentName` — we just need to propagate them into SSE event payloads.

### New/Modified SSE Events

| Event Type | Change | New Fields |
|---|---|---|
| `TEXT_MESSAGE_START` | MODIFIED | `participantAgentId?: string`, `participantAgentName?: string` |
| `TOOL_CALL_START` | MODIFIED | `participantAgentId?: string`, `participantAgentName?: string` |
| `TEAM_HANDOFF` | NEW | `fromAgentId`, `fromAgentName`, `toAgentId`, `toAgentName` |
| `TEAM_LEDGER_UPDATE` | NEW | `ledgerType: "outer" \| "inner"`, `agentName?: string`, `content: string` |
| `TEAM_PROGRESS` | NEW | `currentAgentId`, `currentAgentName`, `step?: number`, `totalSteps?: number`, `mode: TeamMode` |

### Backward Compatibility

Single-agent conversations (ChatClient, A2A) continue to work unchanged — the new fields are optional and absent in non-Team responses. The frontend `use-agent-chat.ts` hook adds conditional handling for new event types.

---

## 4. Session Persistence for Team Conversations

### Decision

Continue using the existing `AgentSessionRecord` (JSONB `SessionData`) pattern for Team conversations. Extend the chat message projection to include `participantAgentId` and `participantAgentName` fields within the persisted session data.

### Rationale

- No schema migration needed — `SessionData` is opaque JSONB, so adding new fields to the serialized chat messages doesn't require ALTER TABLE.
- The existing `ChatHistoryProviderFactory` → `PostgresChatHistoryProvider` pipeline works at the per-participant level. For Team conversations, the orchestration layer manages the conversation thread, and session data is persisted at the Team agent level (keyed by Team AgentId + ConversationId).

### Alternatives Considered

| Alternative | Reason Rejected |
|---|---|
| Create a `ChatMessage` DB table with explicit columns for participant attribution | Over-engineering — the JSONB pattern is working well and adding fields to JSON is zero-migration. Would be a breaking change to existing conversations. |
| Store per-participant sessions separately | Would complicate history retrieval and break the single-conversation-thread UX. |

### Session Data Shape (extended)

```jsonc
// Inside AgentSessionRecord.SessionData JSONB
{
  "messages": [
    {
      "role": "user",
      "content": "Analyze the production incident",
      "index": 0
    },
    {
      "role": "assistant",
      "content": "Looking at the metrics...",
      "index": 1,
      "participantAgentId": "guid-of-agent-a",    // NEW
      "participantAgentName": "MetricsAnalyzer"     // NEW
    },
    // ...
  ]
}
```

---

## 5. GroupChatManager Customization (Selector + MagneticOne)

### Decision

Implement two custom `GroupChatManager` subclasses:

1. **`LlmSelectorGroupChatManager`** — Uses an LLM (configured via `SelectorProviderId`/`SelectorModelId`) to dynamically select the next speaker based on conversation context.
2. **`MagneticOneGroupChatManager`** — Implements the dual-loop ledger pattern: outer loop (plan generation/revision) + inner loop (agent task execution).

### Rationale

- The framework provides only `RoundRobinGroupChatManager` built-in.
- The `GroupChatManager` abstract class has a single override point: `SelectNextAgentAsync(IList<ChatMessage> conversationHistory)` → returns the next `AIAgent`.
- Both Selector and MagneticOne modes are fundamentally "which agent speaks next" decisions — they differ only in selection strategy.

### LlmSelectorGroupChatManager Design

```csharp
public class LlmSelectorGroupChatManager : GroupChatManager
{
    // Constructor receives the selector IChatClient + prompt + participants
    // SelectNextAgentAsync:
    //   1. Build a system prompt listing participant names/descriptions
    //   2. Append conversation history
    //   3. Ask the selector LLM: "Which agent should respond next?"
    //   4. Parse response → find matching participant → return AIAgent
    //   5. Respect AllowRepeatedSpeaker setting
}
```

### MagneticOneGroupChatManager Design

```csharp
public class MagneticOneGroupChatManager : GroupChatManager
{
    // Maintains:
    //   - OuterLedger: high-level plan (facts, plan, next_step)
    //   - InnerLedger: per-agent task execution log
    //   - StallCounter: incremented when no progress detected
    //
    // SelectNextAgentAsync:
    //   1. Outer loop: orchestrator LLM reviews plan against progress
    //   2. If plan needs revision → update OuterLedger, emit TEAM_LEDGER_UPDATE
    //   3. Select next agent based on plan's next_step
    //   4. Inner loop: selected agent executes, result logged to InnerLedger
    //   5. If stalled MaxStalls times → force final answer agent
    //   6. Emit TEAM_LEDGER_UPDATE for inner log entry
}
```

### Alternatives Considered

| Alternative | Reason Rejected |
|---|---|
| Implement Selector/MagneticOne as completely separate orchestrators (not GroupChat) | Would bypass the framework's GroupChat infrastructure (conversation history management, participant lifecycle). More code, less framework leverage. |
| Use `WorkflowBuilder` low-level API for MagneticOne | The dual-loop pattern is still fundamentally a "select next speaker" pattern with conversation history. GroupChatManager with extra state is the right abstraction. |

---

## 6. MagneticOne Ledger Data Model

### Decision

Model the MagneticOne ledger as two streaming data structures: an outer ledger (JSON object) and an inner ledger (append-only log). Both are emitted via `TEAM_LEDGER_UPDATE` SSE events and maintained in frontend state.

### Outer Ledger Structure

```jsonc
{
  "facts": "Key observations about the current state...",
  "plan": "Step 1: ...\nStep 2: ...\nStep 3: ...",
  "nextStep": "Execute Step 2 using MetricsAnalyzer",
  "progress": "Step 1 completed successfully. Moving to Step 2.",
  "isComplete": false
}
```

### Inner Ledger Structure

```jsonc
[
  {
    "agentName": "MetricsAnalyzer",
    "task": "Query Prometheus for CPU metrics",
    "status": "completed",     // "running" | "completed" | "failed"
    "summary": "CPU usage peaked at 92% on node-3",
    "timestamp": "2026-02-17T10:00:00Z"
  }
]
```

### Frontend Component

The `MagneticOneLedger.tsx` collapsible side panel displays:
- **Top section**: Outer ledger — plan text, progress, next step
- **Bottom section**: Inner ledger — scrollable list of agent task entries with status icons

### Rationale

This matches the MagneticOne paper's dual-loop architecture and aligns with the user's clarification answer: "outer ledger at top, inner ledger entries per agent below."

---

## 7. Frontend Concurrent Bubble Strategy

### Decision

In Concurrent mode, render each participant agent's response as a **separate `MessageBubble`** component. Bubbles appear in completion order (first-finished first). Each bubble is labeled with the participant agent's name.

### Rationale

- Matches the clarification answer: "Stream each agent's response as a separate bubble as it completes."
- The SSE stream will emit a `TEXT_MESSAGE_START` with `participantAgentName` for each concurrent agent. The frontend accumulates multiple in-flight assistant messages simultaneously and renders them as separate bubbles.

### Implementation Approach

1. `use-agent-chat.ts` maintains a `Map<string, ChatMessage>` for in-flight messages keyed by `participantAgentId`.
2. On `TEXT_MESSAGE_START` with a new `participantAgentId`, create a new in-flight message entry.
3. On `TEXT_MESSAGE_CONTENT`, append to the correct entry by `participantAgentId`.
4. On `TEXT_MESSAGE_END`, finalize the message entry and add to the messages array.
5. The messages array renders as individual `MessageBubble` components in arrival order.

### Alternatives Considered

| Alternative | Reason Rejected |
|---|---|
| Single merged bubble with agent labels inline | User explicitly chose separate bubbles. Merged content is harder to attribute visually. |
| Wait for all agents to complete, then show all at once | Loses the streaming UX benefit. User wants to see responses as they arrive. |

---

## 8. Handoff Tool Naming with GUID Agent IDs

### Decision

Use `AgentRegistration.Name` (human-readable) as the agent name passed to `AIAgent` construction, not the GUID `Id`. The framework's `BuildHandoffs` generates `handoff_to_{agentName}()` tool functions — using a readable name produces usable tool names like `handoff_to_SalesAgent()` instead of `handoff_to_a1b2c3d4()`.

### Rationale

- `AgentWorkflowBuilder.BuildHandoffs` auto-generates handoff tool functions named after the target agent.
- GUID-based names produce non-functional tool names that confuse LLMs.
- The `AgentRegistration.Name` is unique within a team's participants (enforced by UI/domain validation for practical use).

### Alternatives Considered

| Alternative | Reason Rejected |
|---|---|
| Create wrapper agents with sanitized names | Unnecessary indirection — just set the `AIAgent.Name` correctly at construction. |
| Use GUID tool names and add descriptions | LLMs perform poorly with unreadable tool names even with good descriptions. |

### Risk

If two participant agents happen to have the same `Name`, the handoff tool names would collide. Mitigation: validate participant name uniqueness at Team resolution time (log a warning and append a suffix if duplicated).
