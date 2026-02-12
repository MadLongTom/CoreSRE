# Feature Specification: Agent Memory & History Management

**Feature Branch**: `014-agent-memory-history`  
**Created**: 2026-02-11  
**Status**: Draft  
**Input**: User description: "Agent chat history management via framework ChatHistoryProvider, token window control via IChatReducer, and cross-session semantic memory via ChatHistoryMemoryProvider + pgvector"

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Framework-Managed Chat History (Priority: P1)

As a user chatting with an Agent, I want the system to automatically store and restore my conversation history so that I can resume a conversation across browser refreshes or devices without losing context, and the backend no longer relies on the frontend sending the full message list every time.

**Why this priority**: Eliminates the biggest architectural debt — manual SQL persistence in the chat endpoint — and unlocks all downstream framework capabilities (reducers, memory providers). This is the foundation all other stories depend on.

**Independent Test**: Start a conversation with an Agent. Close the browser tab. Reopen the same conversation URL. The full prior conversation is displayed and the Agent responds with awareness of earlier messages, even though the frontend did not resend them.

**Acceptance Scenarios**:

1. **Given** a user has sent 5 messages in a conversation, **When** the user closes the browser and reopens the same conversation, **Then** all 5 messages and their replies are visible and the Agent can reference earlier messages in its response.
2. **Given** a conversation exists in the database, **When** the user sends a new message in that conversation, **Then** the system loads persisted history from the store, appends the new message, and sends the combined context to the LLM.
3. **Given** the session store is configured, **When** the Agent produces a response, **Then** the updated session (including the new exchange) is persisted via the store — no raw SQL is executed in the endpoint.
4. **Given** a brand-new conversation with no prior history, **When** the user sends the first message, **Then** a new session record is created through the session store and the Agent responds normally.

---

### User Story 2 — Token Window Management (Priority: P2)

As a user having a long conversation (50+ exchanges), I want the system to automatically manage the context window so that the conversation never fails due to token-limit errors and the Agent continues to respond coherently.

**Why this priority**: Long conversations currently have no safety net — they will eventually exceed the model's context window and produce errors or truncated output. This story prevents that failure mode and is a natural extension of Story 1 (requires framework history management to be in place).

**Independent Test**: Send 100+ short messages in a single conversation. Observe that the Agent never returns a token-limit error, that recent messages are always included in context, and that the system applies the configured truncation strategy transparently.

**Acceptance Scenarios**:

1. **Given** a conversation with message count exceeding the configured `MaxHistoryMessages` threshold, **When** the user sends a new message, **Then** the system truncates older messages before invoking the LLM, keeping at least the system prompt and the N most recent exchanges.
2. **Given** an Agent is configured with `MaxHistoryMessages = 20`, **When** the conversation reaches 30 messages, **Then** only the 20 most recent messages (plus system instructions) are sent to the LLM.
3. **Given** an Agent has no explicit `MaxHistoryMessages` set, **When** the user chats, **Then** a sensible platform default is applied (e.g., 50 messages) to prevent unbounded context growth.
4. **Given** a truncation event occurs, **When** the user reviews the conversation in the UI, **Then** all messages remain visible in the chat history (truncation is server-side only, not deleting stored messages).

---

### User Story 3 — Per-Agent Memory Configuration UI (Priority: P2)

As a platform administrator configuring an Agent, I want to control history and memory behavior per Agent through the existing configuration interface so that I can tune each Agent's context strategy without code changes.

**Why this priority**: Enables non-developer users to manage memory behavior. Shares priority with Story 2 because the configuration UI is needed to expose the token window settings.

**Independent Test**: Open the Agent detail page. Toggle "Enable Chat History" on, set "Max History Messages" to 30. Save. Start a conversation and verify that the Agent respects the configured threshold.

**Acceptance Scenarios**:

1. **Given** the Agent configuration page, **When** the administrator views the LLM Config section, **Then** a "History & Memory" subsection displays controls for `EnableChatHistory`, `MaxHistoryMessages`, `EnableSemanticMemory`, `MemorySearchMode`, and `MemoryMaxResults`.
2. **Given** `EnableChatHistory` is toggled off, **When** the administrator saves, **Then** the Agent operates in stateless mode (frontend sends full history, matching current behavior) as a fallback.
3. **Given** the administrator sets `MaxHistoryMessages` to 10 and saves, **When** a user chats past 10 messages, **Then** the system truncates according to this setting.
4. **Given** `EnableSemanticMemory` is off (default), **When** a user chats, **Then** no vector embeddings are created and no semantic retrieval occurs.

---

### User Story 4 — Cross-Session Semantic Memory (Priority: P3)

As a user who interacts with the same Agent across multiple conversations, I want the Agent to recall relevant information from past conversations so that I don't have to repeat context (e.g., "use the deployment plan we discussed last week").

**Why this priority**: This is the most advanced capability — a differentiator for the platform — but also the highest complexity. Requires Stories 1 and 2 as prerequisites and introduces a dependency on vector storage.

**Independent Test**: In Conversation A, tell the Agent "My preferred deployment strategy is blue-green with canary at 10%." End the conversation. Start a new Conversation B and ask "What deployment strategy did I mention?" The Agent recalls the blue-green canary strategy from Conversation A.

**Acceptance Scenarios**:

1. **Given** semantic memory is enabled for an Agent, **When** a conversation concludes or after each exchange, **Then** the system generates embeddings for the conversation content and stores them in the vector store scoped to the Agent and user.
2. **Given** stored memories exist from previous conversations, **When** the user starts a new conversation, **Then** the system retrieves the top-N most relevant memories (based on semantic similarity to the current message) and injects them as context before invoking the LLM.
3. **Given** `MemorySearchMode` is set to `BeforeAIInvoke`, **When** the user sends a message, **Then** relevant memories are injected automatically before every LLM call.
4. **Given** `MemoryMaxResults` is set to 5, **When** semantic retrieval runs, **Then** at most 5 memory snippets are returned and injected into context.
5. **Given** semantic memory is enabled, **When** two different users interact with the same Agent, **Then** each user's memories are isolated — User A cannot see User B's memories.

---

### Edge Cases

- What happens when the database is temporarily unavailable during session save? The system should log the error and return the Agent's response to the user (best-effort persistence, not blocking the chat flow).
- What happens when a conversation's stored session data is corrupted or cannot be deserialized? The system should start a fresh session for that conversation and log a warning, rather than failing the request.
- What happens when the vector store is not yet provisioned or its required extension is not enabled? Semantic memory features should degrade gracefully — the Agent operates without memory and logs an informational message.
- What happens when `MaxHistoryMessages` is set to 0 or a negative value? The system should treat it as "no limit" (platform default applies).
- What happens when the embedding model configured for memory is unavailable? Memory retrieval should be skipped with a warning, and the Agent should still respond using direct conversation history.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST integrate the framework's chat history provider into the agent resolution pipeline, replacing the current stateless pattern where the frontend sends full message history.
- **FR-002**: System MUST use the existing session store for session persistence, eliminating all raw SQL in the chat endpoint.
- **FR-003**: System MUST load persisted conversation history from the session store when a returning conversation is detected (matching `conversationId`).
- **FR-004**: System MUST support a chat-reducer-based token window management strategy that truncates older messages when the conversation exceeds a configurable threshold.
- **FR-005**: System MUST apply a platform default for `MaxHistoryMessages` when no per-Agent value is configured, preventing unbounded context growth.
- **FR-006**: System MUST extend `LlmConfigVO` with memory and history configuration fields: `EnableChatHistory`, `MaxHistoryMessages`, `EnableSemanticMemory`, `MemorySearchMode`, `MemoryMaxResults`.
- **FR-007**: System MUST expose memory and history configuration controls in the Agent configuration UI (LLM Config section).
- **FR-008**: System MUST support cross-session semantic memory using vector embeddings, scoped per Agent and per user, so that an Agent can recall information from prior conversations.
- **FR-009**: System MUST store vector embeddings in the existing database infrastructure using vector extension support.
- **FR-010**: System MUST allow administrators to enable or disable each memory capability independently per Agent (chat history management, token truncation, semantic memory).
- **FR-011**: System MUST preserve backward compatibility — Agents with `EnableChatHistory = false` continue operating in the current stateless mode where the frontend sends full history.
- **FR-012**: Truncation MUST be server-side only; all messages remain stored in the session and visible in the frontend chat history.
- **FR-013**: Semantic memory MUST be user-scoped — memories from one user's conversations are not accessible to another user interacting with the same Agent.
- **FR-014**: System MUST degrade gracefully when optional infrastructure (vector store, embedding model) is unavailable, falling back to non-memory operation with appropriate logging.

### Key Entities

- **AgentSessionRecord**: Existing entity storing opaque session data keyed by AgentId + ConversationId. Will be used via the framework session store instead of manual SQL.
- **LlmConfigVO**: Value object on AgentRegistration storing LLM parameters. Extended with history and memory configuration fields.
- **Conversation**: Existing metadata entity linking AgentId to a conversation title and timestamps. No structural changes needed.
- **Agent Memory Embedding** (new concept): Vector representation of conversation content stored with vector indexing, scoped by Agent ID and user identifier, used for cross-session semantic retrieval.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can close and reopen a conversation and see full history restored without any message loss, with the page loading in under 2 seconds for conversations with up to 200 messages.
- **SC-002**: Conversations exceeding 100 exchanges complete without token-limit errors, and the Agent continues to provide coherent, contextually relevant responses.
- **SC-003**: Zero raw SQL statements remain in the chat endpoint code — all persistence flows through the framework session store.
- **SC-004**: Administrators can configure history and memory settings for an Agent in under 1 minute through the UI without requiring code changes or redeployment.
- **SC-005**: When semantic memory is enabled, the Agent correctly recalls information from a prior conversation at least 80% of the time when the user references it within 30 days.
- **SC-006**: Memory isolation is complete — in testing, no cross-user memory leakage is observed across 100 test conversations.
- **SC-007**: All memory features degrade gracefully — disabling vector storage or the embedding model does not prevent basic chat functionality from working.

## Assumptions

- The existing session store implementation is functionally correct and only needs to be wired into the pipeline — no major refactoring of the store itself is required.
- The AG-UI frontend protocol can continue to send messages but the backend will prefer stored server-side history when `EnableChatHistory` is true, using the frontend-provided messages only as a fallback or for the initial request.
- The database supports vector storage (via a vector extension) or this capability can be enabled — this is an infrastructure prerequisite for semantic memory.
- A suitable embedding model is accessible through the configured LLM providers for generating vector embeddings.
- The chat reducer implementation uses message-count-based truncation as the initial strategy. More sophisticated strategies (token-counting, summarization) are deferred to future iterations.
- The platform default for `MaxHistoryMessages` is 50 messages, which can be adjusted via application configuration.
- User identity for memory scoping is derived from the existing conversation/session context — no new authentication mechanisms are needed.
