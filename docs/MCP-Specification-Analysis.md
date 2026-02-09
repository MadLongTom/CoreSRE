# MCP (Model Context Protocol) — Comprehensive Specification Analysis

> **Source**: `mcp-specification` (draft revision, protocol version `DRAFT-2026-v1`)
> **Analysis Date**: 2026-02-09

---

## Table of Contents

1. [Protocol Overview](#1-protocol-overview)
2. [Architecture](#2-architecture)
3. [Core Primitives](#3-core-primitives)
4. [Message Types (JSON-RPC)](#4-message-types-json-rpc)
5. [Transport Layer](#5-transport-layer)
6. [Tool Definition](#6-tool-definition)
7. [Resource System](#7-resource-system)
8. [Capability Negotiation](#8-capability-negotiation)
9. [Lifecycle](#9-lifecycle)
10. [Sampling](#10-sampling)
11. [Appendix — Complete Method Catalog](#appendix--complete-method-catalog)

---

## 1. Protocol Overview

### What is MCP?

The **Model Context Protocol (MCP)** is an open, standardized protocol that enables seamless integration between LLM applications and external data sources/tools. It provides a universal contract for:

- **Sharing contextual information** with language models (files, DB schemas, application state)
- **Exposing tools and capabilities** to AI systems (API calls, computations, file operations)
- **Building composable integrations** and workflows across AI applications

### What Problem Does It Solve?

Before MCP, every AI application had to build bespoke integrations with each data source, tool, or service it wanted to connect to. This led to an **N×M integration problem** — N applications each needing M custom connectors.

MCP reduces this to **N+M** by defining a single, universal protocol. It is analogous to what the **Language Server Protocol (LSP)** did for programming language tooling: one protocol, infinite interoperability.

### Key Design Goals

| Goal | Description |
|------|-------------|
| **Servers should be extremely easy to build** | Hosts handle complex orchestration; servers focus on specific capabilities |
| **Servers should be highly composable** | Multiple servers combine seamlessly via a shared protocol |
| **Isolation** | Servers cannot see the full conversation or into other servers |
| **Progressive capability** | Features can be added incrementally via capability negotiation |
| **Backwards compatibility** | Protocol designed for future extensibility without breaking changes |

---

## 2. Architecture

MCP follows a **Client-Host-Server** architecture built on JSON-RPC 2.0.

### Core Components

```
┌──────────────────────────────────────┐
│         Application Host Process     │
│                                      │
│   ┌──────┐                           │
│   │ Host │──┬── Client 1 ──── Server 1 (Files & Git) ──── Local Resource A
│   └──────┘  ├── Client 2 ──── Server 2 (Database)    ──── Local Resource B
│             └── Client 3 ──── Server 3 (External API) ─── Remote Resource C
└──────────────────────────────────────┘
```

#### Host

The **host** process is the container and coordinator (e.g., an AI-powered IDE, chat application):

- Creates and manages multiple **Client** instances
- Controls connection permissions and lifecycle
- Enforces security policies and user consent
- Handles user authorization decisions
- Coordinates AI/LLM integration and sampling
- Manages context aggregation across clients
- **Full conversation history stays with the host**

#### Client

Each **client** maintains an isolated 1:1 session with a single server:

- Establishes one stateful session per server
- Handles protocol negotiation and capability exchange
- Routes protocol messages bidirectionally
- Manages subscriptions and notifications
- Maintains security boundaries between servers

#### Server

**Servers** provide specialized context and capabilities:

- Expose resources, tools, and prompts via MCP primitives
- Operate independently with focused responsibilities
- Request sampling through client interfaces
- Can be local processes or remote services
- Must respect security constraints

### Relationship Rules

| Relationship | Cardinality | Notes |
|---|---|---|
| Host → Client | 1:N | A host creates and manages many clients |
| Client → Server | 1:1 | Each client connects to exactly one server |
| Server → Resources | 1:N | A server can expose multiple resources/tools/prompts |

---

## 3. Core Primitives

MCP defines three server-side primitives and three client-side features:

### Server Primitives

| Primitive | Control Model | Description | Example |
|-----------|--------------|-------------|---------|
| **Prompts** | User-controlled | Pre-defined templates/instructions invoked by user choice | Slash commands, menu options |
| **Resources** | Application-controlled | Contextual data attached and managed by the client | File contents, git history, DB schemas |
| **Tools** | Model-controlled | Functions exposed for the LLM to execute autonomously | API POST requests, file writing, computations |

### Client Features

| Feature | Direction | Description |
|---------|-----------|-------------|
| **Sampling** | Server → Client | Server requests LLM completions from the client (enables agentic nesting) |
| **Roots** | Server → Client | Server queries filesystem boundaries it may operate in |
| **Elicitation** | Server → Client | Server requests additional information from the user (form or URL mode) |

### Cross-Cutting Utilities

- **Ping**: Liveness check (either direction)
- **Progress**: Out-of-band progress notifications for long-running requests
- **Cancellation**: Cancel an in-flight request
- **Logging**: Server sends structured log messages to client
- **Completion**: Argument autocompletion for prompts and resource templates
- **Pagination**: Cursor-based pagination for list operations
- **Tasks**: Long-running task management with polling

---

## 4. Message Types (JSON-RPC)

All messages follow **JSON-RPC 2.0**, must be **UTF-8 encoded**.

### Base Message Types

#### Request

```typescript
interface JSONRPCRequest {
  jsonrpc: "2.0";
  id: string | number;     // MUST NOT be null; MUST be unique per session
  method: string;
  params?: { [key: string]: unknown };
}
```

#### Result Response (Success)

```typescript
interface JSONRPCResultResponse {
  jsonrpc: "2.0";
  id: string | number;     // Same ID as corresponding request
  result: { [key: string]: unknown };
}
```

#### Error Response (Failure)

```typescript
interface JSONRPCErrorResponse {
  jsonrpc: "2.0";
  id?: string | number;    // May be absent if request ID unreadable
  error: {
    code: number;
    message: string;
    data?: unknown;
  };
}
```

#### Notification (One-Way, No Response Expected)

```typescript
interface JSONRPCNotification {
  jsonrpc: "2.0";
  method: string;          // MUST NOT include an id field
  params?: { [key: string]: unknown };
}
```

### Standard JSON-RPC Error Codes

| Code | Constant | Meaning |
|------|----------|---------|
| `-32700` | `PARSE_ERROR` | Invalid JSON received |
| `-32600` | `INVALID_REQUEST` | Request not a valid JSON-RPC object |
| `-32601` | `METHOD_NOT_FOUND` | Method does not exist or capability not declared |
| `-32602` | `INVALID_PARAMS` | Invalid method parameters (unknown tool, missing args, bad cursor, etc.) |
| `-32603` | `INTERNAL_ERROR` | Unexpected internal error |
| `-32002` | *(custom)* | Resource not found |
| `-1` | *(custom)* | User rejected sampling/elicitation request |

### Complete Method Catalog

#### Requests (Client → Server)

| Method | Purpose | Paginated? |
|--------|---------|------------|
| `initialize` | Begin session, negotiate capabilities | No |
| `ping` | Liveness check | No |
| `resources/list` | List available resources | Yes |
| `resources/templates/list` | List resource templates | Yes |
| `resources/read` | Read a resource's contents | No |
| `resources/subscribe` | Subscribe to resource changes | No |
| `resources/unsubscribe` | Unsubscribe from resource changes | No |
| `prompts/list` | List available prompts | Yes |
| `prompts/get` | Get a specific prompt with arguments | No |
| `tools/list` | List available tools | Yes |
| `tools/call` | Invoke a tool | No |
| `completion/complete` | Request argument autocompletion | No |
| `logging/setLevel` | Set the server's log level | No |

#### Requests (Server → Client)

| Method | Purpose |
|--------|---------|
| `sampling/createMessage` | Request an LLM completion |
| `roots/list` | Query filesystem roots |
| `elicitation/create` | Request user input (form or URL mode) |
| `ping` | Liveness check |

#### Notifications (Client → Server)

| Method | Purpose |
|--------|---------|
| `notifications/initialized` | Client ready after initialization |
| `notifications/cancelled` | Cancel an in-flight request |
| `notifications/progress` | Report progress on a request |
| `notifications/roots/list_changed` | Roots list has changed |

#### Notifications (Server → Client)

| Method | Purpose |
|--------|---------|
| `notifications/cancelled` | Cancel an in-flight request |
| `notifications/progress` | Report progress on a request |
| `notifications/resources/list_changed` | Available resources changed |
| `notifications/resources/updated` | A subscribed resource changed |
| `notifications/prompts/list_changed` | Available prompts changed |
| `notifications/tools/list_changed` | Available tools changed |
| `notifications/message` | Structured log message |

---

## 5. Transport Layer

### Supported Transports

#### 1. stdio (Standard I/O)

The simplest transport — the client launches the server as a subprocess.

| Aspect | Detail |
|--------|--------|
| **Channel** | Client writes to server's `stdin`, reads from server's `stdout` |
| **Delimiter** | Messages delimited by newlines; **MUST NOT** contain embedded newlines |
| **Logging** | Server MAY write UTF-8 strings to `stderr` for logging |
| **Encoding** | UTF-8 |
| **Shutdown** | Client closes stdin → waits → SIGTERM → SIGKILL |

```
Client ──stdin──► Server Process
Client ◄──stdout── Server Process
Client ◄──stderr── Server Process (optional logs)
```

#### 2. Streamable HTTP (replaces HTTP+SSE from 2024-11-05)

For remote/multi-client scenarios. The server provides a single HTTP endpoint (the "MCP endpoint") supporting both POST and GET.

| Aspect | Detail |
|--------|--------|
| **Client → Server** | `HTTP POST` with JSON-RPC body to MCP endpoint |
| **Server → Client** | Either `Content-Type: application/json` (single response) or `Content-Type: text/event-stream` (SSE stream) |
| **Server push** | Client opens SSE via `HTTP GET` to receive server-initiated messages |
| **Session ID** | Server MAY return `MCP-Session-Id` header at initialization |
| **Protocol version header** | Client MUST send `MCP-Protocol-Version: <version>` on all subsequent requests |
| **Resumability** | Servers MAY attach `id` to SSE events; clients reconnect with `Last-Event-ID` |
| **Session termination** | Client sends `HTTP DELETE` with session ID |
| **Security** | Server MUST validate `Origin` header; SHOULD bind to localhost when local |

**SSE Stream Behavior:**
- Server SHOULD send initial SSE event with event ID + empty data (primes reconnection)
- Server MAY close connection without terminating stream (client polls to reconnect)
- Server SHOULD include `retry` field before closing
- Server MAY send requests/notifications before the JSON-RPC response
- After JSON-RPC response is sent, server SHOULD terminate the stream

**Backwards Compatibility** (with old HTTP+SSE transport):
- Clients: POST `InitializeRequest` to server URL; if 400/404/405, fall back to GET for SSE `endpoint` event
- Servers: Host both old SSE+POST endpoints alongside the new MCP endpoint

#### 3. Custom Transports

Any bidirectional channel preserving JSON-RPC format and lifecycle requirements. Must be documented.

---

## 6. Tool Definition

### Tool Schema

```typescript
interface Tool {
  name: string;                    // Unique identifier (1-128 chars, A-Za-z0-9_-.)
  title?: string;                  // Human-readable display name
  description?: string;            // LLM-facing description
  icons?: Icon[];                  // UI display icons
  inputSchema: {                   // JSON Schema (defaults to 2020-12)
    $schema?: string;
    type: "object";
    properties?: { [key: string]: object };
    required?: string[];
  };
  outputSchema?: {                 // Optional structured output schema
    $schema?: string;
    type: "object";
    properties?: { [key: string]: object };
    required?: string[];
  };
  annotations?: ToolAnnotations;   // Behavioral hints
  execution?: ToolExecution;       // Task support config
  _meta?: MetaObject;
}
```

### Tool Annotations (Behavioral Hints)

All annotations are **untrusted hints** — clients MUST NOT rely on them from untrusted servers.

| Annotation | Type | Default | Meaning |
|---|---|---|---|
| `title` | `string` | — | Human-readable display title |
| `readOnlyHint` | `boolean` | `false` | Tool does not modify its environment |
| `destructiveHint` | `boolean` | `true` | Tool may perform destructive updates |
| `idempotentHint` | `boolean` | `false` | Repeated calls with same args = no additional effect |
| `openWorldHint` | `boolean` | `true` | Tool interacts with external/open world (vs. closed domain) |

### Tool Execution Config

```typescript
interface ToolExecution {
  taskSupport?: "forbidden" | "optional" | "required";  // Default: "forbidden"
}
```

### Tool Result

Tool results can contain **unstructured content** (in `content`) and/or **structured content** (in `structuredContent`):

```typescript
interface CallToolResult {
  content?: ContentBlock[];       // Text, Image, Audio, ResourceLink, EmbeddedResource
  structuredContent?: object;     // JSON conforming to outputSchema
  isError?: boolean;              // true = tool execution error (actionable by LLM)
}
```

**Content types supported:**

| Type | Key Fields |
|------|-----------|
| `text` | `text: string` |
| `image` | `data: string (base64)`, `mimeType: string` |
| `audio` | `data: string (base64)`, `mimeType: string` |
| `resource_link` | `uri`, `name`, `mimeType` — link to a fetchable resource |
| `resource` | `resource: { uri, mimeType, text \| blob }` — inline embedded resource |

### Tool Call Flow

```
Client                          Server
  │                                │
  ├── tools/list ─────────────────►│
  │◄── [{name, inputSchema, ...}]──┤
  │                                │
  ├── tools/call ─────────────────►│
  │   {name: "get_weather",        │
  │    arguments: {location: "NY"}}│
  │◄── {content: [...],            │
  │     structuredContent: {...}}───┤
```

### Error Handling (Two Tiers)

1. **Protocol Errors**: Standard JSON-RPC errors (unknown tool → `-32602`, malformed request)
2. **Tool Execution Errors**: `isError: true` in result (API failures, validation errors) — actionable by LLM for self-correction

---

## 7. Resource System

### Resource Schema

```typescript
interface Resource {
  uri: string;                    // Unique URI (RFC 3986)
  name: string;                   // Resource name
  title?: string;                 // Human-readable display name
  description?: string;           // LLM-facing description
  icons?: Icon[];                 // UI display icons
  mimeType?: string;              // MIME type
  size?: number;                  // Raw content size in bytes
  annotations?: Annotations;      // Audience, priority, lastModified
  _meta?: MetaObject;
}
```

### Resource Annotations

```typescript
interface Annotations {
  audience?: ("user" | "assistant")[];   // Intended audience(s)
  priority?: number;                      // 0.0 (least) to 1.0 (most important)
  lastModified?: string;                  // ISO 8601 timestamp
}
```

### Resource Contents

| Type | Fields |
|------|--------|
| **Text** | `uri`, `mimeType`, `text: string` |
| **Binary** | `uri`, `mimeType`, `blob: string (base64)` |

### Resource Templates (URI Templates)

Servers expose parameterized resources using [RFC 6570 URI Templates](https://datatracker.ietf.org/doc/html/rfc6570):

```json
{
  "uriTemplate": "file:///{path}",
  "name": "Project Files",
  "description": "Access files in the project directory",
  "mimeType": "application/octet-stream"
}
```

Template arguments can be auto-completed via the `completion/complete` API.

### Common URI Schemes

| Scheme | Usage |
|--------|-------|
| `https://` | Web-fetchable resources (client can fetch directly) |
| `file://` | Filesystem-like resources (need not map to physical FS) |
| `git://` | Git version control integration |
| Custom | Must conform to RFC 3986 |

### Resource Operations

| Method | Direction | Purpose |
|--------|-----------|---------|
| `resources/list` | Client → Server | Discover available resources (paginated) |
| `resources/templates/list` | Client → Server | Discover resource templates (paginated) |
| `resources/read` | Client → Server | Read resource contents by URI |
| `resources/subscribe` | Client → Server | Subscribe to resource changes |
| `resources/unsubscribe` | Client → Server | Unsubscribe |
| `notifications/resources/list_changed` | Server → Client | Resource list changed |
| `notifications/resources/updated` | Server → Client | Subscribed resource changed |

---

## 8. Capability Negotiation

Capability negotiation happens during the `initialize` handshake. Both sides declare what they support; only negotiated features may be used during the session.

### Client Capabilities

```typescript
interface ClientCapabilities {
  roots?: {
    listChanged?: boolean;        // Client will notify on root changes
  };
  sampling?: {
    context?: object;             // Supports includeContext (soft-deprecated)
    tools?: object;               // Supports tool use in sampling
  };
  elicitation?: {
    form?: object;                // Supports form-mode elicitation
    url?: object;                 // Supports URL-mode elicitation
  };
  tasks?: {
    list?: object;                // Supports tasks/list
    cancel?: object;              // Supports tasks/cancel
    requests?: {
      sampling?: { createMessage?: object };
      elicitation?: { create?: object };
    };
  };
  extensions?: { [key: string]: object };   // Optional MCP extensions
  experimental?: { [key: string]: object }; // Non-standard features
}
```

### Server Capabilities

```typescript
interface ServerCapabilities {
  logging?: object;               // Can send log messages
  completions?: object;           // Supports argument autocompletion
  prompts?: {
    listChanged?: boolean;        // Will notify on prompt list changes
  };
  resources?: {
    subscribe?: boolean;          // Supports resource subscriptions
    listChanged?: boolean;        // Will notify on resource list changes
  };
  tools?: {
    listChanged?: boolean;        // Will notify on tool list changes
  };
  tasks?: {
    list?: object;
    cancel?: object;
    requests?: {
      tools?: { call?: object };  // Supports task-augmented tool calls
    };
  };
  extensions?: { [key: string]: object };
  experimental?: { [key: string]: object };
}
```

### Negotiation Rules

1. Both sides declare capabilities in `initialize` / `InitializeResult`
2. Both MUST respect declared capabilities throughout the session
3. Undeclared capabilities MUST NOT be used (server returns `-32601 Method Not Found`)
4. Extensions: if one party supports an extension but the other doesn't, the supporting party MUST fall back to core behavior or reject with an error

---

## 9. Lifecycle

### Phase 1: Initialization

```
Client                              Server
  │                                    │
  ├── initialize ─────────────────────►│  Contains: protocolVersion, capabilities, clientInfo
  │◄── InitializeResult ──────────────┤  Contains: protocolVersion, capabilities, serverInfo, instructions?
  │                                    │
  ├── notifications/initialized ──────►│  Client signals readiness
  │                                    │
  ▼ ─── Session Active ──────────────►▼
```

**Version Negotiation:**
- Client sends latest protocol version it supports
- Server responds with the same version (if supported) or its own latest
- If client can't support server's version → disconnect

**Key Constraints:**
- Client SHOULD NOT send requests (except `ping`) before server responds to `initialize`
- Server SHOULD NOT send requests (except `ping` and `logging`) before receiving `initialized`
- HTTP clients MUST include `MCP-Protocol-Version` header on all subsequent requests

### Phase 2: Operation

Normal bidirectional JSON-RPC communication within negotiated capabilities.

### Phase 3: Shutdown

**stdio:**
1. Client closes stdin to server subprocess
2. Wait for server to exit
3. SIGTERM if server doesn't exit in reasonable time
4. SIGKILL if still doesn't exit

**HTTP:**
1. Client sends `HTTP DELETE` to MCP endpoint with `MCP-Session-Id`
2. Server closes associated connections
3. Server returns 404 for subsequent requests with that session ID

### Timeouts

- Implementations SHOULD establish timeouts for all requests
- On timeout → send `notifications/cancelled` and stop waiting
- Progress notifications MAY reset the timeout clock
- A maximum timeout SHOULD always be enforced regardless of progress

### Implementation Metadata

```typescript
interface Implementation {
  name: string;           // Programmatic name
  title?: string;         // Human-readable display name
  version: string;        // Implementation version
  description?: string;   // What this implementation does
  icons?: Icon[];         // Display icons
  websiteUrl?: string;    // Website URL
}
```

---

## 10. Sampling

### What is Sampling?

**Sampling** allows MCP servers to request LLM completions from the client. This is the inverse of the normal flow — instead of the client using server tools, the **server asks the client to generate text** using whatever LLM the host controls.

This enables **agentic behaviors**: LLM calls nested inside MCP server features, without the server needing its own API keys.

### Sampling Flow

```
Server                Client                User                 LLM
  │                     │                    │                    │
  ├── sampling/         │                    │                    │
  │   createMessage ───►│                    │                    │
  │                     ├── Present request ─►│                    │
  │                     │◄── Approve/modify ──┤                    │
  │                     ├── Forward request ──────────────────────►│
  │                     │◄── Return generation ───────────────────┤
  │                     ├── Present response ─►│                   │
  │                     │◄── Approve/modify ───┤                   │
  │◄── Approved         │                    │                    │
  │    response ────────┤                    │                    │
```

**Human-in-the-loop is RECOMMENDED** — the client SHOULD show the request for user approval and allow editing before sending/returning.

### CreateMessage Request

```typescript
interface CreateMessageRequestParams {
  messages: SamplingMessage[];           // Conversation messages
  modelPreferences?: ModelPreferences;   // Model selection hints
  systemPrompt?: string;                 // Optional system prompt (client MAY ignore)
  includeContext?: "none" | "thisServer" | "allServers";  // Soft-deprecated
  maxTokens: number;                     // REQUIRED, client MUST respect
  temperature?: number;                  // Client MAY ignore
  stopSequences?: string[];              // Client MAY ignore
  metadata?: object;                     // Provider-specific params
  tools?: ToolDefinition[];              // Tools for LLM to use during sampling
  toolChoice?: { mode: "auto" | "required" | "none" };
}
```

### Model Preferences

```typescript
interface ModelPreferences {
  hints?: { name: string }[];       // Model name substrings, in preference order
  costPriority?: number;            // 0-1, higher = prefer cheaper
  speedPriority?: number;           // 0-1, higher = prefer faster
  intelligencePriority?: number;    // 0-1, higher = prefer more capable
}
```

Hints are advisory — clients make final model selection and MAY map hints to equivalent models from different providers.

### CreateMessage Result

```typescript
interface CreateMessageResult {
  role: "assistant";
  content: ContentBlock | ContentBlock[];    // text, image, audio, tool_use
  model: string;                              // Actual model used
  stopReason?: "endTurn" | "stopSequence" | "maxTokens" | "toolUse" | string;
}
```

### Sampling with Tools (Agentic Loop)

1. Server sends `sampling/createMessage` with `tools` array
2. Client forwards to LLM → LLM returns `tool_use` content blocks (`stopReason: "toolUse"`)
3. Client returns tool_use response to server
4. Server **executes the tools locally**, then sends a new `sampling/createMessage` with tool results appended
5. Repeat until LLM returns a final text response (`stopReason: "endTurn"`)

**Constraints:**
- Every assistant message with `ToolUseContent` MUST be followed by a user message with ONLY `ToolResultContent` blocks
- Each `tool_use.id` must be matched by a `tool_result.toolUseId`
- Tool result messages MUST NOT contain mixed content types

### Tool Choice Modes

| Mode | Behavior |
|------|----------|
| `auto` | Model decides whether to use tools (default) |
| `required` | Model MUST use at least one tool |
| `none` | Model MUST NOT use any tools |

---

## Appendix — Complete Method Catalog

### All Request Methods

| Method | Direction | Request Type | Result Type |
|--------|-----------|-------------|-------------|
| `initialize` | C→S | `InitializeRequest` | `InitializeResult` |
| `ping` | Both | `PingRequest` | `EmptyResult` |
| `resources/list` | C→S | `ListResourcesRequest` | `ListResourcesResult` |
| `resources/templates/list` | C→S | `ListResourceTemplatesRequest` | `ListResourceTemplatesResult` |
| `resources/read` | C→S | `ReadResourceRequest` | `ReadResourceResult` |
| `resources/subscribe` | C→S | `SubscribeRequest` | `EmptyResult` |
| `resources/unsubscribe` | C→S | `UnsubscribeRequest` | `EmptyResult` |
| `prompts/list` | C→S | `ListPromptsRequest` | `ListPromptsResult` |
| `prompts/get` | C→S | `GetPromptRequest` | `GetPromptResult` |
| `tools/list` | C→S | `ListToolsRequest` | `ListToolsResult` |
| `tools/call` | C→S | `CallToolRequest` | `CallToolResult` |
| `completion/complete` | C→S | `CompleteRequest` | `CompleteResult` |
| `logging/setLevel` | C→S | `SetLevelRequest` | `EmptyResult` |
| `sampling/createMessage` | S→C | `CreateMessageRequest` | `CreateMessageResult` |
| `roots/list` | S→C | `ListRootsRequest` | `ListRootsResult` |
| `elicitation/create` | S→C | `ElicitRequest` | `ElicitResult` |

### All Notification Methods

| Method | Direction | Params Type |
|--------|-----------|-------------|
| `notifications/initialized` | C→S | `NotificationParams` |
| `notifications/cancelled` | Both | `CancelledNotificationParams` |
| `notifications/progress` | Both | `ProgressNotificationParams` |
| `notifications/roots/list_changed` | C→S | `NotificationParams` |
| `notifications/resources/list_changed` | S→C | `NotificationParams` |
| `notifications/resources/updated` | S→C | `ResourceUpdatedNotificationParams` |
| `notifications/prompts/list_changed` | S→C | `NotificationParams` |
| `notifications/tools/list_changed` | S→C | `NotificationParams` |
| `notifications/message` | S→C | `LoggingMessageNotificationParams` |

### Type Hierarchy Summary

```
JSONRPCMessage
  ├── JSONRPCRequest          (has id, method, params?)
  ├── JSONRPCNotification     (has method, params?, no id)
  └── JSONRPCResponse
        ├── JSONRPCResultResponse   (has id, result)
        └── JSONRPCErrorResponse    (has id?, error{code, message, data?})

ContentBlock (union)
  ├── TextContent        { type: "text", text }
  ├── ImageContent       { type: "image", data, mimeType }
  ├── AudioContent       { type: "audio", data, mimeType }
  ├── ResourceLink       { type: "resource_link", uri, name, ... }
  ├── EmbeddedResource   { type: "resource", resource: TextResourceContents | BlobResourceContents }
  ├── ToolUseContent     { type: "tool_use", id, name, input }
  └── ToolResultContent  { type: "tool_result", toolUseId, content[] }
```

---

*End of Analysis*
