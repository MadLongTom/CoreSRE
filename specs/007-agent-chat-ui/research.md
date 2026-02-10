# Research: 007 Agent Chat UI

**Created**: 2026-02-10  
**Feature**: [spec.md](spec.md) | [plan.md](plan.md)

## Research Topics

### 1. Agent Invocation API (Microsoft.Agents.AI + AG-UI)

**Decision**: Use `ChatClientAgent` from `Microsoft.Agents.AI.OpenAI` via the `.CreateAIAgent()` extension method on `IChatClient`. Use `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` (`AddAGUI` / `MapAGUI`) to expose agents as AG-UI SSE endpoints.

**Rationale**: 
- The AG-UI reference implementation in `.reference/codes/ag-ui/integrations/microsoft-agent-framework/dotnet/` demonstrates the exact pattern: `OpenAIClient.GetChatClient(model).AsIChatClient().CreateAIAgent(name, description)` creates a `ChatClientAgent`.
- `MapAGUI("/path", agent)` automatically handles: accepting `RunAgentInput` JSON POST, invoking `agent.RunStreamingAsync()`, translating streaming responses to AG-UI events (`TEXT_MESSAGE_START/CONTENT/END`, `TOOL_CALL_*`, `STATE_*`, `RUN_STARTED/FINISHED`), and writing SSE format to response.
- This eliminates the need to manually write SSE formatting, event serialization, or cancellation handling — the AG-UI middleware handles all of it.
- Required NuGet packages: `Microsoft.Agents.AI.OpenAI`, `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`, `System.Net.ServerSentEvents`.

**Alternatives considered**:
- Raw `IChatClient` + manual SSE writing: Works but requires reimplementing what `MapAGUI` already provides. More code, more bugs.
- Custom event protocol: Unnecessary when AG-UI is an industry standard with client SDKs.

### 2. Streaming Architecture (AG-UI Protocol)

**Decision**: Use AG-UI protocol (`@ag-ui/client` `HttpAgent` + subscriber pattern) on frontend, `MapAGUI` on backend.

**Rationale**:
- AG-UI is an event-driven SSE protocol that standardizes agent-UI communication. The event flow is: `RUN_STARTED → TEXT_MESSAGE_START → TEXT_MESSAGE_CONTENT* → TEXT_MESSAGE_END → RUN_FINISHED`.
- Backend: `MapAGUI` handles all SSE formatting. The endpoint receives a POST with `RunAgentInput` (containing `threadId`, `runId`, `messages[]`, `tools[]`, `context[]`, `state`) and returns `text/event-stream`.
- Frontend: `HttpAgent` from `@ag-ui/client` wraps `fetch` POST + SSE parsing into an RxJS Observable. The subscriber pattern provides granular hooks (`onTextMessageContentEvent`, `onMessagesChanged`, etc.) for progressive UI updates.
- CopilotKit is NOT required — the `@ag-ui/client` SDK is self-contained. Custom React hooks + subscriber can drive any UI.
- `HttpAgent.abortRun()` cancels the SSE stream; backend detects disconnect via `CancellationToken`.

**Alternatives considered**:
- CopilotKit UI: Full-featured but adds large dependency (`@copilotkit/react-core`, `@copilotkit/react-ui`, `@copilotkit/runtime`). Our custom shadcn UI is lighter and consistent with existing codebase.
- Raw `fetch` + `ReadableStream`: Works but requires manual SSE parsing, event validation, message buffer management. `@ag-ui/client` does all of this.
- `EventSource` (GET): Cannot send POST body. Not compatible with AG-UI protocol which uses POST.

### 3. Conversation Persistence Strategy

**Decision**: Create a lightweight `Conversation` metadata entity (no `ChatMessage` entity). Reuse existing `AgentSessionRecord` (SPEC-004) for chat history — messages are stored in `SessionData` JSONB via `ChatHistoryProviderState.Messages[]`, automatically managed by `ChatClientAgent`'s `ChatHistoryProvider` + `PostgresAgentSessionStore`.

**Rationale**:
- `AgentSessionRecord.SessionData` already contains the full chat history inside `ChatHistoryProviderState.Messages[]`, automatically appended by `ChatHistoryProvider.InvokedAsync()` after each agent invocation — no manual persistence needed.
- `Conversation` is a thin aggregate root holding only metadata: `AgentId` (FK, immutable), `Title`, timestamps. It references `AgentSessionRecord` via a logical composite key (`AgentRegistration.Name` + `Conversation.Id.ToString()`).
- The spec requires listing conversations with summaries (last message preview, timestamps). The summary is extracted by deserializing the last entry from `SessionData.ChatHistoryProviderState.Messages[]` at query time.
- Agent binding immutability (FR-003) is enforced at the domain level: `Conversation.AgentId` is set on creation and has no setter.
- Only the `conversations` table is added by migration. The `agent_sessions` table already exists (SPEC-004).

**Alternatives considered**:
- Custom `ChatMessage` entity + `chat_messages` table: Duplicates message data already stored in `AgentSessionRecord.SessionData`. Creates two sources of truth that must be kept in sync. More code, more migrations, more complexity.
- Store messages as JSONB array directly on Conversation: Reinvents what `AgentSessionRecord` already does.
- Extend `AgentSessionRecord` with metadata columns (title, etc.): Violates SRP, mixes Agent Framework concerns with chat UI concerns.

### 4. Agent Resolver Design (IAgentResolver)

**Decision**: Create `IAgentResolver` interface in Application layer that resolves an `AgentRegistration` ID to a ready-to-use `ChatClientAgent` (from `Microsoft.Agents.AI.OpenAI`). Infrastructure implementation constructs `OpenAIClient` → `.GetChatClient(model).AsIChatClient().CreateAIAgent(name, description)`.

**Rationale**:
- The resolver maps our domain's `AgentRegistration` entity (which references an `LlmProvider` for credentials and a model ID) to an AG-UI-compatible `ChatClientAgent`.
- `CreateAIAgent(name, description)` is an extension method on `IChatClient` from `Microsoft.Agents.AI.OpenAI` that wraps it in a `ChatClientAgent` supporting `RunStreamingAsync`.
- `MapAGUI("/api/chat/stream", agentResolver)` invokes the resolver per-request to get the appropriate agent for the conversation.
- For future A2A/workflow agent types, the resolver can return different `AIAgent` subclasses.

**Alternatives considered**:
- Direct `IChatClient` usage without agent wrapping: Works for raw streaming but incompatible with `MapAGUI` which requires an `AIAgent` (or subclass).
- Factory pattern per agent type: Over-engineering for current scope. The resolver pattern with type-switch is simpler.
- Custom `AIAgent` subclass (decorator): Only needed if we must intercept/augment the stream (e.g., state management, tool interception). For MVP, plain `ChatClientAgent` suffices.

### 5. Frontend State Management for Chat

**Decision**: Use a custom `useAgentChat` hook built on `@ag-ui/client` `HttpAgent` subscriber pattern. No global state management library.

**Rationale**:
- The `HttpAgent` from `@ag-ui/client` provides a subscriber-based API: `.subscribe({ onTextMessageContentEvent, onMessagesChanged, onRunStarted, onRunFinished, onError })`.
- The custom hook wraps `HttpAgent` lifecycle: creates agent instance, manages subscription, tracks streaming state, assembles messages from events.
- Chat state (messages, streaming status, error) is page-scoped, not shared across pages — local state is sufficient.
- Key subscriber callbacks:
  - `onTextMessageContentEvent({ textMessageBuffer })`: progressive text streaming for UI
  - `onMessagesChanged({ messages })`: final message list after stream completes
  - `onRunStarted`: set streaming=true
  - `onRunFinished`: set streaming=false, persist to backend
  - `onError`: handle error display
- `HttpAgent.abortRun()` cleanly cancels the SSE stream.

**Alternatives considered**:
- CopilotKit React hooks (`useCopilotChat`): Large dependency bundle, opinionated UI. Not needed since `@ag-ui/client` is sufficient.
- Raw `fetch` + `ReadableStream` + manual SSE parsing: Works but reimplements what `@ag-ui/client` already provides (SSE parsing, event validation, subscriber dispatch).
- Zustand/Redux: Not installed, and chat state doesn't need to be global.

### 6. Message History Management

**Decision**: Reuse `AgentSessionRecord.SessionData` (JSONB) as the single source of truth for chat history. No separate `ChatMessage` entity or `chat_messages` table.

**Rationale**:
- `AgentSessionRecord.SessionData` already contains the full chat history inside `ChatHistoryProviderState.Messages[]`, automatically managed by `ChatClientAgent`'s `ChatHistoryProvider` + `PostgresAgentSessionStore`.
- The `ChatHistoryProvider.InvokedAsync()` callback automatically appends request messages + AI context + response messages after each agent invocation — no manual persistence needed.
- `Conversation` is a lightweight metadata entity (title, agentId, timestamps). Messages are read by deserializing `SessionData` at query time.
- AG-UI's `MapAGUI` is stateless per-request: the frontend sends full message history in `RunAgentInput.messages`, and `ChatHistoryProvider` reconciles this with the stored session.
- For the conversation list summary (last message preview), the Application layer deserializes the last entry from `SessionData.ChatHistoryProviderState.Messages[]`.

**Alternatives considered**:
- Custom `ChatMessage` entity + `chat_messages` table: Duplicates data already in `AgentSessionRecord`. Creates two sources of truth that must be kept in sync. More code, more migrations, more complexity.
- Frontend-managed history only: Security risk, client could tamper with history. Backend session store is authoritative.
