# Quickstart: Team Mode Agent Chat

**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-02-17

## Prerequisites

1. Existing CoreSRE dev environment running (`.\dev.ps1`)
2. At least 2 ChatClient or A2A agents created and Active
3. At least 1 LLM Provider configured (for participant agents + Selector/MagneticOne orchestrators)

## 1. Create a Team Agent

Use the existing Agent CRUD UI (`/agents/new`) or API:

```http
POST /api/agents
Content-Type: application/json

{
  "name": "SRE Team",
  "description": "Sequential SRE analysis pipeline",
  "agentType": "Team",
  "teamConfig": {
    "mode": "Sequential",
    "participantIds": [
      "{{metricsAnalyzer-agent-id}}",
      "{{logAnalyzer-agent-id}}",
      "{{remediationAdvisor-agent-id}}"
    ],
    "maxIterations": 10
  }
}
```

## 2. Chat with Team Agent

1. Navigate to `/chat`
2. Open the Agent Selector — Team agents now appear with a team icon badge
3. Select "SRE Team"
4. Type a message: "Analyze high CPU usage on node-3"
5. Observe:
   - **Progress indicator** shows "Agent 1/3: MetricsAnalyzer is thinking..."
   - **Message bubbles** labeled with each participant's name
   - Sequential run: MetricsAnalyzer → LogAnalyzer → RemediationAdvisor

## 3. Try Different Modes

### Concurrent

```json
{
  "mode": "Concurrent",
  "participantIds": ["{{agent-a}}", "{{agent-b}}", "{{agent-c}}"],
  "maxIterations": 5,
  "aggregationStrategy": "Merge"
}
```
Observe: All 3 agents process in parallel. Responses appear as separate bubbles in completion order.

### Handoffs

```json
{
  "mode": "Handoffs",
  "participantIds": ["{{triage}}", "{{sales}}", "{{support}}"],
  "initialAgentId": "{{triage}}",
  "handoffRoutes": {
    "{{triage}}": [
      { "targetAgentId": "{{sales}}", "reason": "Sales inquiry" },
      { "targetAgentId": "{{support}}", "reason": "Support issue" }
    ],
    "{{sales}}": [
      { "targetAgentId": "{{triage}}", "reason": "Reclassify" }
    ]
  },
  "maxIterations": 10
}
```
Observe: Triage agent decides where to route. "🔀 TriageAgent handed off to SalesAgent" notification appears.

### RoundRobin

```json
{
  "mode": "RoundRobin",
  "participantIds": ["{{agent-a}}", "{{agent-b}}"],
  "maxIterations": 6
}
```
Observe: Agents take turns speaking. Progress shows current round.

### Selector

```json
{
  "mode": "Selector",
  "participantIds": ["{{agent-a}}", "{{agent-b}}", "{{agent-c}}"],
  "selectorProviderId": "{{llm-provider-id}}",
  "selectorModelId": "gpt-4o",
  "selectorPrompt": "Choose the best agent for the current conversation context.",
  "allowRepeatedSpeaker": false,
  "maxIterations": 10
}
```
Observe: An LLM dynamically selects which agent speaks next based on conversation context.

### MagneticOne

```json
{
  "mode": "MagneticOne",
  "participantIds": ["{{metrics-agent}}", "{{logs-agent}}", "{{remediation-agent}}"],
  "orchestratorProviderId": "{{llm-provider-id}}",
  "orchestratorModelId": "gpt-4o",
  "maxStalls": 3,
  "finalAnswerPrompt": "Summarize all findings and provide a recommended action.",
  "maxIterations": 20
}
```
Observe: Collapsible side panel shows outer ledger (plan/progress) and inner ledger (agent task log). Auto-updates in real time.

## 4. Verify History Persistence

1. Complete a Team conversation
2. Refresh the page (F5)
3. Select the conversation from the sidebar
4. Verify all messages retain participant agent name labels

## 5. Edge Cases to Test

| Scenario | Expected |
|---|---|
| Participant agent is Inactive | Error before streaming: "Participant 'X' is not active" |
| Participant agent deleted | Error before streaming: "Participant 'X' not found" |
| MaxIterations reached | Stream stops with user notification |
| Single participant | Works as single-agent pass-through |
| LLM provider timeout | Error with participant name attribution |

## Architecture Overview

```
Frontend                          Backend
─────────                        ────────
AgentSelector (Team visible) ──→ POST /api/chat/stream
                                   ↓
                              AgentResolverService
                                   ↓ (Team case)
                              ITeamOrchestrator.BuildTeamAgent()
                                   ↓
                              AgentWorkflowBuilder.Build{Mode}(...)
                                   ↓
                              Workflow.AsAgent() → AIAgent
                                   ↓
                              HandleTeamStreamAsync()
                                   ↓
                              RunStreamingAsync() → SSE events
                                   ↓
use-agent-chat.ts ←────────── TEXT_MESSAGE_*, TEAM_PROGRESS,
MessageBubble (attributed)        TEAM_HANDOFF, TEAM_LEDGER_UPDATE
MagneticOneLedger
HandoffNotification
TeamProgressIndicator
```
