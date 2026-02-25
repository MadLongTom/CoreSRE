# Feature Specification: Team Mode Agent Chat

**Feature Branch**: `021-team-agent-chat`  
**Created**: 2026-02-17  
**Status**: Draft  
**Input**: User description: "实现在前端选择Team模式Agent对话"

## Clarifications

### Session 2026-02-17

- Q: How should MagneticOne's dual-loop ledgers (outer orchestrator plan + inner per-agent task logs) be displayed? → A: Collapsible side panel beside the chat — outer ledger (plan/progress) at top, inner ledger entries per agent below.
- Q: Should all 6 orchestration modes be mandatory for initial release, or phased? → A: All 6 modes (Sequential, Concurrent, RoundRobin, Handoffs, Selector, MagneticOne) mandatory from day one — no phasing.
- Q: How should Concurrent mode display parallel agent responses? → A: Stream each agent's response as a separate message bubble as it completes (first-finished appears first), each labeled with agent name.
- Q: Should there be a per-participant response timeout for team orchestration? → A: No custom timeout — rely solely on `maxIterations` to prevent infinite loops and the LLM provider's own timeout to handle individual agent stalls.
- Q: Should handoff transitions between agents be visible to the user? → A: Yes — show a system-style notification between message bubbles (e.g., "🔀 Agent A handed off to Agent B") before the new agent starts responding.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Select a Team Agent and Start Conversation (Priority: P1)

As a platform user, I want to select a Team-type agent from the chat agent selector and start a conversation, so that I can leverage multi-agent collaboration without needing to interact with each participant agent individually.

**Why this priority**: This is the foundational capability — without it, no Team conversation can happen. It unblocks all downstream features.

**Independent Test**: Can be tested by creating a Team agent (via existing CRUD), then opening the Chat page, selecting the Team agent from the dropdown, and sending a message. The system should accept the message and produce a response.

**Acceptance Scenarios**:

1. **Given** a Team agent (e.g., "SRE Team" with Sequential mode, 2 participants) exists and is Active, **When** the user opens the Chat page and clicks the agent selector, **Then** the Team agent appears in the dropdown list alongside ChatClient and A2A agents.
2. **Given** the user has selected a Team agent, **When** the user types a message and sends it, **Then** a new conversation is created and bound to the Team agent, and the system begins processing the message through the team orchestration pipeline.
3. **Given** no Team agents exist in the system, **When** the user opens the agent selector, **Then** the selector only shows ChatClient and A2A agents as before, with no errors.

---

### User Story 2 — See Which Participant Agent Is Speaking (Priority: P1)

As a user in a Team agent conversation, I want to see which participant agent authored each response message, so that I can understand the multi-agent collaboration flow and know who is "speaking."

**Why this priority**: Without participant attribution, multi-agent responses are indistinguishable from single-agent responses, losing the primary value of team mode.

**Independent Test**: Start a conversation with a Sequential team (Agent A → Agent B). Send a message. Verify that each response segment is labeled with the originating agent's name.

**Acceptance Scenarios**:

1. **Given** a Team agent with participants [Agent A, Agent B] in Sequential mode, **When** Agent A produces its response, **Then** the message bubble displays Agent A's name as the speaker.
2. **Given** Agent A finishes and Agent B begins responding, **When** Agent B's output starts streaming, **Then** a new message bubble appears with Agent B's name as the speaker.
3. **Given** a Concurrent team with participants [Agent A, Agent B, Agent C], **When** Agent B finishes first, **Then** Agent B's response appears as the first message bubble (labeled with Agent B's name), followed by the others as they complete.
4. **Given** a Handoffs team where Agent A decides to hand off to Agent B, **When** the handoff occurs, **Then** a system notification ("🔀 Agent A handed off to Agent B") appears between Agent A's message and Agent B's message.
5. **Given** a Team conversation with multiple participant responses, **When** the user scrolls through the history, **Then** each assistant message clearly shows which participant agent authored it.

---

### User Story 3 — Team Agent Visual Differentiation (Priority: P2)

As a user, I want Team agents to be visually distinct in the agent selector and conversation list, so that I can easily tell them apart from single agents.

**Why this priority**: Important for usability but not blocking core functionality.

**Independent Test**: Open the agent selector with a mix of ChatClient, A2A, and Team agents. Verify Team agents have a distinct visual indicator (icon or badge).

**Acceptance Scenarios**:

1. **Given** agents of types ChatClient, A2A, and Team exist, **When** the user opens the agent selector, **Then** Team agents display a "team" icon or badge that differentiates them from single agents.
2. **Given** a conversation list with both single-agent and Team conversations, **When** the user views the sidebar, **Then** Team conversations show the Team agent's name and a team mode indicator.

---

### User Story 4 — Team Orchestration Progress Indicator (Priority: P2)

As a user watching a Team agent conversation, I want to see an indicator of which step/agent the team is currently executing, so that I understand the progress of multi-agent processing.

**Why this priority**: Enhances user confidence during longer multi-agent processing but is not required for core functionality.

**Independent Test**: Start a conversation with a 3-participant Sequential team. Observe that a progress indicator shows which participant is currently active.

**Acceptance Scenarios**:

1. **Given** a Sequential team with 3 participants, **When** the second participant is processing, **Then** the UI shows a progress indicator (e.g., "Agent 2/3: Agent B is thinking...").
2. **Given** a Concurrent team with 3 participants, **When** all participants are processing in parallel, **Then** the UI shows that multiple agents are working simultaneously.
3. **Given** a MagneticOne team is executing, **When** the orchestrator updates its plan or an agent completes a task, **Then** a collapsible side panel beside the chat displays the outer ledger (high-level plan/progress) at the top and inner ledger entries (per-agent task details) below, updating in real time.

---

### User Story 5 — Reload and Continue Team Conversation (Priority: P3)

As a user, I want to reload the page or return later and see the full history of a Team conversation, so that I can review the multi-agent dialogue.

**Why this priority**: History persistence is important for production use but can be deferred after core chat works.

**Independent Test**: Start and complete a Team conversation. Refresh the page. Select the conversation from history. Verify all messages with participant attribution are restored.

**Acceptance Scenarios**:

1. **Given** a completed Team conversation with messages from participants A and B, **When** the user navigates away and returns, **Then** the conversation appears in the history list.
2. **Given** the user clicks on a past Team conversation, **When** the messages load, **Then** each message retains its participant agent name attribution.

---

### Edge Cases

- What happens when a participant agent within a Team is in Error/Inactive status at conversation time? The system should report a clear error indicating which participant is unavailable.
- What happens if a participant agent's LLM provider returns an error mid-conversation? The system should surface the error with the participant agent's name and stop the pipeline gracefully.
- What happens when a Team agent has only 1 participant configured? The system should still work, treating it as a single-agent pass-through.
- What happens if the team reaches `maxIterations` during a conversation? The system should stop and inform the user that the iteration limit was reached.
- What happens if a participant agent's LLM provider times out? The system should surface the LLM provider's timeout error with the participant agent's name and stop the pipeline gracefully (no additional custom timeout layer).
- What happens when a Team agent references a participant that has been deleted? The system should report a configuration error at conversation start, not mid-processing.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The agent selector in the Chat page MUST include Team-type agents alongside ChatClient and A2A agents.
- **FR-002**: The system MUST allow creating a conversation bound to a Team agent via the existing conversation creation flow.
- **FR-003**: The system MUST process user messages through the Team's orchestration pipeline (Sequential, Concurrent, RoundRobin, Handoffs, Selector, MagneticOne) according to the Team agent's `TeamConfig`.
- **FR-004**: Each response message from a participant agent MUST include attribution metadata (participant agent name and ID).
- **FR-005**: The streaming output MUST emit attribution information so the frontend can determine which participant agent is currently producing output.
- **FR-006**: The frontend MUST render participant agent names on each assistant message bubble in Team conversations.
- **FR-007**: The agent selector MUST visually differentiate Team agents from single agents (e.g., with a team icon or badge).
- **FR-008**: The system MUST validate all participant agents are resolvable and active before starting Team orchestration.
- **FR-009**: The system MUST respect the `maxIterations` limit defined in `TeamConfig` and stop with a user-visible notification when reached.
- **FR-010**: Conversation history for Team conversations MUST persist participant attribution so it is available on reload.
- **FR-011**: The system MUST surface clear error messages when a participant agent fails, including the participant's name.
- **FR-012**: The frontend MUST show an indicator of which participant agent is currently active during streaming.
- **FR-013**: When the Team is in MagneticOne mode, the frontend MUST display a collapsible side panel showing the outer ledger (orchestrator plan/progress) at the top and inner ledger entries (per-agent task execution details) below, updated in real time as the orchestration progresses.
- **FR-014**: When the Team is in Concurrent mode, the frontend MUST display each participant agent's response as a separate message bubble, appearing in completion order (first-finished first), each labeled with the agent's name.
- **FR-015**: When the Team is in Handoffs mode and a handoff occurs, the frontend MUST display a visible system notification between message bubbles indicating which agent handed off to which (e.g., "🔀 Agent A handed off to Agent B").

### Key Entities

- **AgentRegistration (Team type)**: The top-level Team agent that owns the orchestration configuration. It is the entity bound to the conversation. Already exists in the domain model.
- **TeamConfig**: Configuration containing orchestration mode, participant IDs, and mode-specific settings (handoff routes, selector prompts, etc.). Already exists as a value object.
- **Participant Agent**: Any non-Team AgentRegistration referenced in TeamConfig's participant list. Resolved at conversation time.
- **Conversation**: Binds to the Team agent's ID. No structural changes needed to the entity itself.
- **ChatMessage**: Needs an additional participant agent name (and optionally participant agent ID) attribute to store speaker attribution for Team conversations. For single-agent conversations this attribute is empty/null.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can select a Team agent and send a message with the same ease as selecting a single agent — no additional steps required beyond agent selection.
- **SC-002**: 100% of assistant messages in a Team conversation display the originating participant agent's name.
- **SC-003**: Users can identify a Team agent in the selector within 2 seconds due to clear visual differentiation.
- **SC-004**: Team conversations with up to 5 participants complete without UI errors or lost messages.
- **SC-005**: After page reload, Team conversation history loads with full participant attribution intact.
- **SC-006**: When a participant agent fails, the user sees a meaningful error within 3 seconds identifying the failing agent.
- **SC-007**: All 6 team orchestration modes (Sequential, Concurrent, RoundRobin, Handoffs, Selector, MagneticOne) are selectable and produce expected multi-agent behavior.

## Assumptions

- The domain model for Team agents (AgentRegistration with Team type, TeamConfig value object, TeamMode enum) is already fully implemented and available (per SPEC-018).
- The existing agent CRUD UI already supports creating Team agents with TeamConfig (mode, participants, etc.).
- Participant agents are always of type ChatClient or A2A (team nesting is forbidden by domain validation).
- The streaming protocol can be extended with custom metadata fields (e.g., agent name on message start events) without breaking existing single-agent clients.
- Team orchestration execution (the actual Sequential/RoundRobin/Handoffs/Selector/MagneticOne pipelines) will be implemented as part of this feature's backend work.
- The agent resolver will be extended to support Team-type resolution, returning participant agents and orchestration metadata.
