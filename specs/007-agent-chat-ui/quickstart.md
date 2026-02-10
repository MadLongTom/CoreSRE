# Quickstart: 007 Agent Chat UI

**Feature**: Single Agent Chat Interface  
**Branch**: `007-agent-chat-ui`  
**Protocol**: AG-UI (Agent-UI Protocol) over SSE

## What This Feature Does

Provides a chat page where users can select a registered Agent, start a conversation, and exchange messages. Agent responses are streamed in real-time via the AG-UI protocol (SSE). Conversations are persisted and can be resumed later.

## Prerequisites

- At least one Agent registered (via Agent management page)
- For ChatClient agents: a configured LLM Provider with valid API key and base URL
- PostgreSQL running (via Aspire AppHost)
- Frontend packages: `@ag-ui/client`, `@ag-ui/core`, `rxjs`
- Backend packages: `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`, `Microsoft.Agents.AI.OpenAI`

## Quick Verification

### 1. Start the application

```bash
dotnet run --project Backend/CoreSRE
# Frontend: cd Frontend && npm run dev
```

### 2. Create a conversation (REST)

```bash
# List available agents
curl http://localhost:5000/api/agents

# Create a conversation with an agent
curl -X POST http://localhost:5000/api/chat/conversations \
  -H "Content-Type: application/json" \
  -d '{"agentId": "<agent-id-from-above>"}'
```

### 3. Send a message via AG-UI stream

```bash
# AG-UI streaming endpoint (POST with RunAgentInput body → SSE response)
curl -N -X POST http://localhost:5000/api/chat/stream \
  -H "Content-Type: application/json" \
  -d '{
    "threadId": "<conversation-id>",
    "runId": "run-001",
    "messages": [
      {"id": "msg-1", "role": "user", "content": "Hello, tell me about yourself"}
    ],
    "context": [
      {"description": "agentId", "value": "<agent-id>"}
    ]
  }'

# Expected AG-UI SSE output:
# data: {"type":"RUN_STARTED","threadId":"...","runId":"..."}
# data: {"type":"TEXT_MESSAGE_START","messageId":"...","role":"assistant"}
# data: {"type":"TEXT_MESSAGE_CONTENT","messageId":"...","delta":"I"}
# data: {"type":"TEXT_MESSAGE_CONTENT","messageId":"...","delta":"'m"}
# data: {"type":"TEXT_MESSAGE_CONTENT","messageId":"...","delta":" an AI"}
# ...
# data: {"type":"TEXT_MESSAGE_END","messageId":"..."}
# data: {"type":"RUN_FINISHED","threadId":"...","runId":"..."}
```

### 4. List conversations

```bash
curl http://localhost:5000/api/chat/conversations
```

### 5. Get conversation with history

```bash
curl http://localhost:5000/api/chat/conversations/<conversation-id>
```

### 6. Delete a conversation

```bash
curl -X DELETE http://localhost:5000/api/chat/conversations/<conversation-id>
```

## Frontend Usage

1. Navigate to **对话** in the sidebar
2. Click **新建对话** button
3. Select an Agent from the dropdown
4. Type a message and press Enter or click Send
5. Watch the Agent's response stream in real-time (via AG-UI `HttpAgent` subscriber)
6. Switch between conversations using the sidebar list

## Key Behaviors

- **Agent locking**: Once the first message is sent, the Agent selector is disabled
- **Streaming**: Agent responses appear token-by-token via AG-UI `TEXT_MESSAGE_CONTENT` events
- **Persistence**: All conversations and messages are saved to PostgreSQL
- **Auto-title**: Conversation title is auto-generated from the first user message
- **Cancellation**: `HttpAgent.abortRun()` cleanly cancels the SSE stream

## API Endpoints

| Method | Endpoint | Description | Protocol |
|--------|----------|-------------|----------|
| GET | `/api/chat/conversations` | List all conversations (summaries) | REST |
| POST | `/api/chat/conversations` | Create new conversation | REST |
| GET | `/api/chat/conversations/{id}` | Get conversation with messages | REST |
| DELETE | `/api/chat/conversations/{id}` | Delete conversation | REST |
| POST | `/api/chat/stream` | AG-UI agent streaming (MapAGUI) | AG-UI SSE |

## Architecture

```
Frontend (React)                         Backend (ASP.NET)
┌───────────────────────┐               ┌─────────────────────────┐
│ ChatPage              │               │ ChatEndpoints (REST)    │
│ ├─AgentSelector       │   REST API    │ ├─GET conversations     │
│ ├─ConversationList    │──────────────→│ ├─POST conversations    │
│ └─MessageArea         │               │ ├─GET conversations/:id │
│   └─MessageInput      │               │ │  (messages from       │
│                       │               │ │   AgentSessionRecord) │
│ useAgentChat hook     │               │ └─DELETE conversations  │
│ ├─HttpAgent           │   AG-UI SSE   │                         │
│ │ (@ag-ui/client)     │──POST────────→│ AgentChatEndpoints      │
│ │                     │←─SSE events───│ ├─MapAGUI("/stream")   │
│ └─subscriber callbacks│               │ └─ChatClientAgent       │
│   ├─onTextMessageContent              │   ├─ChatHistoryProvider │
│   ├─onMessagesChanged │               │   │ (auto-appends msgs) │
│   ├─onRunStarted      │               │   └─PostgresAgentSession│
│   └─onRunFinished     │               │     Store (persists     │
│     → touch conv.     │               │      SessionData JSONB) │
└───────────────────────┘               │                         │
                                        │ PostgreSQL              │
                                        │ ├─conversations (meta)  │
                                        │ └─agent_sessions (hist) │
                                        └─────────────────────────┘
```

## AG-UI Event Flow Detail

```
Frontend                             Backend
   │                                    │
   │ POST /api/chat/stream              │
   │ { threadId, runId, messages,       │
   │   context: [{agentId}] }           │
   │───────────────────────────────────→│
   │                                    │ IAgentResolver.Resolve(agentId)
   │                                    │ → ChatClientAgent (with
   │                                    │   ChatHistoryProvider +
   │                                    │   PostgresAgentSessionStore)
   │                                    │
   │ SSE: RUN_STARTED                   │
   │←───────────────────────────────────│
   │ SSE: TEXT_MESSAGE_START            │
   │←───────────────────────────────────│ agent.RunStreamingAsync()
   │ SSE: TEXT_MESSAGE_CONTENT (×N)     │ → MapAGUI translates to events
   │←───────────────────────────────────│
   │ SSE: TEXT_MESSAGE_END              │ ChatHistoryProvider auto-appends
   │←───────────────────────────────────│ user + assistant messages to session
   │ SSE: RUN_FINISHED                  │ PostgresAgentSessionStore persists
   │←───────────────────────────────────│ updated SessionData to agent_sessions
   │                                    │
   │ onRunFinished callback fires       │
   │ → PATCH conversation (touch        │
   │   UpdatedAt + SetTitle)            │
   │                                    │
```
