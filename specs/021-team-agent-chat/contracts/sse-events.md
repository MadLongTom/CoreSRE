# SSE Event Protocol: Team Mode Extensions

**Spec**: [../spec.md](../spec.md) | **Date**: 2026-02-17

## Overview

Extends the existing AG-UI SSE protocol (`POST /api/chat/stream`) with participant attribution on existing events and new team-specific event types. Fully backward-compatible — single-agent (ChatClient/A2A) conversations are unaffected.

## Base Protocol (Unchanged)

```
RUN_STARTED → TEXT_MESSAGE_START → TEXT_MESSAGE_CONTENT* → TEXT_MESSAGE_END → RUN_FINISHED
                                                                            → RUN_ERROR
              TOOL_CALL_START → TOOL_CALL_ARGS → TOOL_CALL_END
```

## Modified Events

### TEXT_MESSAGE_START

```jsonc
{
  "type": "TEXT_MESSAGE_START",
  "messageId": "uuid",
  "role": "assistant",
  // NEW — present only in Team conversations
  "participantAgentId": "guid-of-participant",     // optional
  "participantAgentName": "MetricsAnalyzer"         // optional
}
```

**Breaking change**: None. New fields are optional — absent for non-Team conversations.

### TEXT_MESSAGE_CONTENT

```jsonc
{
  "type": "TEXT_MESSAGE_CONTENT",
  "messageId": "uuid",
  "delta": "partial text...",
  // NEW — ties chunk to its message for concurrent multi-agent streaming
  "participantAgentId": "guid-of-participant"       // optional
}
```

### TOOL_CALL_START

```jsonc
{
  "type": "TOOL_CALL_START",
  "toolCallId": "uuid",
  "toolName": "query_metrics_prometheus",
  "parentMessageId": "uuid",
  // NEW
  "participantAgentId": "guid-of-participant",     // optional
  "participantAgentName": "MetricsAnalyzer"         // optional
}
```

## New Events (Team-only)

### TEAM_PROGRESS

Emitted when the orchestrator transitions to a new participant agent. Drives the `TeamProgressIndicator.tsx` component.

```jsonc
{
  "type": "TEAM_PROGRESS",
  "threadId": "uuid",
  "runId": "uuid",
  "currentAgentId": "guid-of-active-agent",
  "currentAgentName": "MetricsAnalyzer",
  "step": 2,                    // 1-based, null for non-sequential modes
  "totalSteps": 3,              // null for non-sequential modes
  "mode": "Sequential"          // TeamMode enum string
}
```

**Emit timing**:
- **Sequential**: Before each participant starts (step 1/N, 2/N, ...)
- **Concurrent**: Once at start listing all agents as active
- **RoundRobin**: Before each round's agent starts
- **Handoffs**: After each handoff decision
- **Selector**: After each LLM selection
- **MagneticOne**: After orchestrator selects next agent

### TEAM_HANDOFF

Emitted in Handoffs mode when an agent triggers a handoff. Drives `HandoffNotification.tsx`.

```jsonc
{
  "type": "TEAM_HANDOFF",
  "threadId": "uuid",
  "runId": "uuid",
  "fromAgentId": "guid-of-source-agent",
  "fromAgentName": "SalesAgent",
  "toAgentId": "guid-of-target-agent",
  "toAgentName": "SupportAgent"
}
```

**Emit timing**: After the handoff tool function is called, before the target agent starts processing.

### TEAM_LEDGER_UPDATE

Emitted in MagneticOne mode for dual-loop ledger updates. Drives `MagneticOneLedger.tsx`.

```jsonc
// Outer ledger (orchestrator plan/progress)
{
  "type": "TEAM_LEDGER_UPDATE",
  "threadId": "uuid",
  "runId": "uuid",
  "ledgerType": "outer",
  "agentName": null,
  "content": {
    "facts": "CPU is at 92% on node-3. Memory is normal.",
    "plan": "1. Query Prometheus metrics\n2. Analyze logs\n3. Recommend fix",
    "nextStep": "Execute Step 2 using LogAnalyzer",
    "progress": "Step 1 completed. CPU spike confirmed.",
    "isComplete": false
  }
}

// Inner ledger (per-agent task log)
{
  "type": "TEAM_LEDGER_UPDATE",
  "threadId": "uuid",
  "runId": "uuid",
  "ledgerType": "inner",
  "agentName": "MetricsAnalyzer",
  "content": {
    "task": "Query Prometheus for CPU metrics on node-3",
    "status": "completed",
    "summary": "CPU usage peaked at 92% at 14:30 UTC"
  }
}
```

**Emit timing**:
- Outer: After orchestrator generates/revises the plan (beginning + after each inner loop)
- Inner: After each participant agent completes its task

## Event Sequence Examples

### Sequential Mode (3 participants: A → B → C)

```
RUN_STARTED
TEAM_PROGRESS        { step: 1, totalSteps: 3, currentAgentName: "A" }
TEXT_MESSAGE_START    { participantAgentName: "A" }
TEXT_MESSAGE_CONTENT* 
TEXT_MESSAGE_END
TEAM_PROGRESS        { step: 2, totalSteps: 3, currentAgentName: "B" }
TEXT_MESSAGE_START    { participantAgentName: "B" }
TEXT_MESSAGE_CONTENT*
TEXT_MESSAGE_END
TEAM_PROGRESS        { step: 3, totalSteps: 3, currentAgentName: "C" }
TEXT_MESSAGE_START    { participantAgentName: "C" }
TEXT_MESSAGE_CONTENT*
TEXT_MESSAGE_END
RUN_FINISHED
```

### Concurrent Mode (3 participants: A ∥ B ∥ C)

```
RUN_STARTED
TEAM_PROGRESS        { mode: "Concurrent", currentAgentName: "A, B, C" }
TEXT_MESSAGE_START    { participantAgentName: "B", messageId: "m1" }   // B finishes first
TEXT_MESSAGE_CONTENT* { messageId: "m1" }
TEXT_MESSAGE_END      { messageId: "m1" }
TEXT_MESSAGE_START    { participantAgentName: "A", messageId: "m2" }
TEXT_MESSAGE_CONTENT* { messageId: "m2" }
TEXT_MESSAGE_END      { messageId: "m2" }
TEXT_MESSAGE_START    { participantAgentName: "C", messageId: "m3" }
TEXT_MESSAGE_CONTENT* { messageId: "m3" }
TEXT_MESSAGE_END      { messageId: "m3" }
RUN_FINISHED
```

### Handoffs Mode (triage → sales → support)

```
RUN_STARTED
TEAM_PROGRESS        { currentAgentName: "TriageAgent", mode: "Handoffs" }
TEXT_MESSAGE_START    { participantAgentName: "TriageAgent" }
TEXT_MESSAGE_CONTENT*
TOOL_CALL_START      { toolName: "handoff_to_SalesAgent", participantAgentName: "TriageAgent" }
TOOL_CALL_ARGS
TOOL_CALL_END
TEXT_MESSAGE_END
TEAM_HANDOFF         { fromAgentName: "TriageAgent", toAgentName: "SalesAgent" }
TEAM_PROGRESS        { currentAgentName: "SalesAgent", mode: "Handoffs" }
TEXT_MESSAGE_START    { participantAgentName: "SalesAgent" }
TEXT_MESSAGE_CONTENT*
TEXT_MESSAGE_END
RUN_FINISHED
```

### MagneticOne Mode

```
RUN_STARTED
TEAM_LEDGER_UPDATE   { ledgerType: "outer", content: { plan: "1. Query metrics...", ... } }
TEAM_PROGRESS        { currentAgentName: "MetricsAnalyzer", mode: "MagneticOne" }
TEXT_MESSAGE_START    { participantAgentName: "MetricsAnalyzer" }
TEXT_MESSAGE_CONTENT*
TEXT_MESSAGE_END
TEAM_LEDGER_UPDATE   { ledgerType: "inner", agentName: "MetricsAnalyzer", content: { status: "completed", ... } }
TEAM_LEDGER_UPDATE   { ledgerType: "outer", content: { progress: "Step 1 done", nextStep: "Step 2...", ... } }
TEAM_PROGRESS        { currentAgentName: "LogAnalyzer", mode: "MagneticOne" }
TEXT_MESSAGE_START    { participantAgentName: "LogAnalyzer" }
TEXT_MESSAGE_CONTENT*
TEXT_MESSAGE_END
TEAM_LEDGER_UPDATE   { ledgerType: "inner", agentName: "LogAnalyzer", content: { status: "completed", ... } }
TEAM_LEDGER_UPDATE   { ledgerType: "outer", content: { isComplete: true, ... } }
RUN_FINISHED
```

## Error Event (Team-specific attribution)

```jsonc
{
  "type": "RUN_ERROR",
  "message": "LLM provider timeout for participant agent 'MetricsAnalyzer'",
  "code": "ParticipantAgentError",
  // NEW
  "participantAgentId": "guid-of-failed-agent",   // optional
  "participantAgentName": "MetricsAnalyzer"         // optional
}
```
