# A2A (Agent-to-Agent) Protocol — Comprehensive Analysis Report

> Source: `a2a-protocol` repository (RC v1.0, specification v0.3.0 latest released)  
> Analysis Date: 2026-02-09

---

## Table of Contents

1. [Protocol Overview](#1-protocol-overview)
2. [Core Concepts](#2-core-concepts)
3. [Key Data Models / Schemas](#3-key-data-models--schemas)
4. [Communication Flow](#4-communication-flow)
5. [Transport & Protocol Bindings](#5-transport--protocol-bindings)
6. [Authentication & Security](#6-authentication--security)
7. [Push Notifications](#7-push-notifications)
8. [Enterprise Considerations](#8-enterprise-considerations)
9. [.NET Integration Points](#9-net-integration-points)
10. [A2A vs MCP](#10-a2a-vs-mcp)

---

## 1. Protocol Overview

### What Is A2A?

A2A (Agent2Agent) is an **open protocol** (under the Linux Foundation, contributed by Google) enabling communication and interoperability between **opaque agentic applications**. It is designed for AI agents built on different frameworks, by different vendors, running on separate servers to collaborate — **as agents, not as tools**.

### What Problem Does It Solve?

| Problem | A2A Solution |
|---------|-------------|
| Agents built on different frameworks cannot talk to each other | Standardized JSON-RPC 2.0 / gRPC / HTTP+JSON communication |
| No standard way to discover what an agent can do | **Agent Cards** — self-describing JSON metadata documents |
| Agents must expose internal state to collaborate | **Opaque execution** — agents collaborate without sharing internal memory, tools, or plans |
| Long-running tasks have no standard lifecycle | **Task** objects with defined state machines and async delivery mechanisms |
| No standard for rich data exchange between agents | **Parts** (text, files, structured data) in messages and artifacts |

### Key Design Principles

- **Simple**: Reuses HTTP, JSON-RPC 2.0, SSE, Protocol Buffers
- **Enterprise Ready**: Auth, authz, security, tracing, monitoring via standard practices
- **Async First**: Native support for long-running tasks and human-in-the-loop
- **Modality Agnostic**: Text, audio/video (file refs), structured data, forms
- **Opaque Execution**: No internal state sharing required

### Specification Architecture (Three Layers)

| Layer | Purpose | Examples |
|-------|---------|---------|
| **Layer 1: Canonical Data Model** | Core data structures (protocol-agnostic, defined as Protocol Buffer messages) | Task, Message, AgentCard, Part, Artifact, Extension |
| **Layer 2: Abstract Operations** | Fundamental capabilities agents must support | SendMessage, StreamMessage, GetTask, ListTasks, CancelTask |
| **Layer 3: Protocol Bindings** | Concrete mappings to specific transport protocols | JSON-RPC methods, gRPC RPCs, HTTP/REST endpoints |

The normative source of truth is `specification/a2a.proto`.

---

## 2. Core Concepts

### 2.1 Actors

| Actor | Role |
|-------|------|
| **User** | End user (human or automated service) initiating the request |
| **A2A Client (Client Agent)** | Application/service/agent acting on behalf of the user, initiates A2A requests |
| **A2A Server (Remote Agent)** | Agent system exposing an A2A-compliant HTTP endpoint — operates as an **opaque black box** |

### 2.2 Fundamental Communication Elements

| Element | Description |
|---------|-------------|
| **Agent Card** | JSON metadata document: identity, capabilities, endpoint, skills, auth requirements |
| **Task** | Stateful unit of work with unique ID and defined lifecycle (state machine) |
| **Message** | Single communication turn (role: `user` or `agent`), contains one or more Parts |
| **Part** | Smallest content unit: `TextPart`, `FilePart`, `DataPart` |
| **Artifact** | Tangible output of a task (document, image, structured data), composed of Parts |
| **Context (`contextId`)** | Server-generated identifier grouping related Tasks and Messages |
| **Extension** | Mechanism for additional functionality beyond core spec |

### 2.3 Interaction Mechanisms

| Mechanism | Description | Best For |
|-----------|-------------|----------|
| **Request/Response (Polling)** | Client sends request, polls for updates via `GetTask` | Simple integrations, clients behind firewalls |
| **Streaming (SSE)** | Real-time incremental updates over open HTTP connection | Interactive apps, low-latency dashboards |
| **Push Notifications (Webhooks)** | Server POSTs updates to client-provided webhook URL | Long-running tasks (minutes/hours/days), serverless clients |

### 2.4 Agent Response Model

An agent receiving a message responds with **either**:

- **`Message`** — for immediate, stateless, trivial interactions (no task tracking)
- **`Task`** — for long-running operations requiring state tracking

Agent types:

| Type | Behavior |
|------|----------|
| **Message-only Agents** | Always respond with Message objects (e.g., thin LLM wrappers) |
| **Task-generating Agents** | Always respond with Task objects (even for simple responses) |
| **Hybrid Agents** | Use Messages to negotiate scope, then generate Tasks for execution |

---

## 3. Key Data Models / Schemas

> All models are defined authoritatively in `specification/a2a.proto` with C# namespace `A2a.V1`.

### 3.1 Task

```
message Task {
  string id              // REQUIRED - unique UUID, server-generated
  string context_id      // REQUIRED - groups related tasks/messages
  TaskStatus status      // REQUIRED - current status
  repeated Artifact artifacts  // output artifacts
  repeated Message history     // interaction history
  Struct metadata              // custom key/value metadata
}
```

### 3.2 TaskState (Enum)

| State | Description | Terminal? |
|-------|-------------|-----------|
| `TASK_STATE_UNSPECIFIED` | Unknown / indeterminate | — |
| `TASK_STATE_SUBMITTED` | Task created, acknowledged | No |
| `TASK_STATE_WORKING` | Actively being processed | No |
| `TASK_STATE_COMPLETED` | Finished successfully | **Yes** |
| `TASK_STATE_FAILED` | Done but failed | **Yes** |
| `TASK_STATE_CANCELED` | Canceled before finishing | **Yes** |
| `TASK_STATE_INPUT_REQUIRED` | Needs additional info from client | Interrupted |
| `TASK_STATE_REJECTED` | Agent refuses to perform task | **Yes** |
| `TASK_STATE_AUTH_REQUIRED` | Needs out-of-band authentication | Interrupted |

### 3.3 TaskStatus

```
message TaskStatus {
  TaskState state            // REQUIRED
  Message message            // optional status message
  Timestamp timestamp        // ISO 8601 UTC
}
```

### 3.4 Message

```
message Message {
  string message_id          // REQUIRED - UUID, created by sender
  string context_id          // optional - associates with context
  string task_id             // optional - associates with task
  Role role                  // REQUIRED - ROLE_USER or ROLE_AGENT
  repeated Part parts        // REQUIRED - content container (≥1)
  Struct metadata            // optional
  repeated string extensions // extension URIs
  repeated string reference_task_ids  // referenced tasks for context
}
```

### 3.5 Role (Enum)

| Value | Meaning |
|-------|---------|
| `ROLE_UNSPECIFIED` | — |
| `ROLE_USER` | Client → Server |
| `ROLE_AGENT` | Server → Client |

### 3.6 Part

```
message Part {
  oneof content {
    string text                     // plain text content
    bytes raw                       // raw bytes (base64 in JSON)
    string url                      // URL pointing to file content
    google.protobuf.Value data      // structured JSON data
  }
  Struct metadata                   // optional part metadata
  string filename                   // optional filename
  string media_type                 // MIME type (e.g. "image/png")
}
```

### 3.7 Artifact

```
message Artifact {
  string artifact_id         // REQUIRED - unique within task
  string name                // human-readable name
  string description         // optional
  repeated Part parts        // REQUIRED (≥1)
  Struct metadata            // optional
  repeated string extensions // extension URIs
}
```

### 3.8 Streaming Events

**TaskStatusUpdateEvent:**
```
message TaskStatusUpdateEvent {
  string task_id       // REQUIRED
  string context_id    // REQUIRED
  TaskStatus status    // REQUIRED
  Struct metadata      // optional
}
```

**TaskArtifactUpdateEvent:**
```
message TaskArtifactUpdateEvent {
  string task_id       // REQUIRED
  string context_id    // REQUIRED
  Artifact artifact    // REQUIRED
  bool append          // append to previous artifact with same ID
  bool last_chunk      // final chunk indicator
  Struct metadata      // optional
}
```

### 3.9 StreamResponse (Union)

```
message StreamResponse {
  oneof payload {
    Task task
    Message message
    TaskStatusUpdateEvent status_update
    TaskArtifactUpdateEvent artifact_update
  }
}
```

### 3.10 AgentCard

```
message AgentCard {
  string name                                    // REQUIRED
  string description                             // REQUIRED
  repeated AgentInterface supported_interfaces   // REQUIRED (ordered, first = preferred)
  AgentProvider provider                         // optional
  string version                                 // REQUIRED
  optional string documentation_url              // optional
  AgentCapabilities capabilities                 // REQUIRED
  map<string, SecurityScheme> security_schemes   // auth scheme definitions
  repeated SecurityRequirement security_requirements  // required auth
  repeated string default_input_modes            // REQUIRED - MIME types
  repeated string default_output_modes           // REQUIRED - MIME types
  repeated AgentSkill skills                     // REQUIRED (≥1)
  repeated AgentCardSignature signatures         // JWS signatures
  optional string icon_url                       // optional
}
```

### 3.11 AgentInterface

```
message AgentInterface {
  string url               // REQUIRED - absolute HTTPS URL
  string protocol_binding  // REQUIRED - "JSONRPC" | "GRPC" | "HTTP+JSON" | custom
  string tenant            // optional
  string protocol_version  // REQUIRED - e.g. "0.3", "1.0"
}
```

### 3.12 AgentCapabilities

```
message AgentCapabilities {
  optional bool streaming             // supports streaming responses
  optional bool push_notifications    // supports push notification webhooks
  repeated AgentExtension extensions  // protocol extensions
  optional bool extended_agent_card   // supports authenticated extended card
}
```

### 3.13 AgentSkill

```
message AgentSkill {
  string id                // REQUIRED - unique identifier
  string name              // REQUIRED
  string description       // REQUIRED
  repeated string tags     // REQUIRED - keyword descriptors
  repeated string examples // example prompts/scenarios
  repeated string input_modes   // overrides agent defaults (MIME types)
  repeated string output_modes  // overrides agent defaults (MIME types)
  repeated SecurityRequirement security_requirements  // skill-specific auth
}
```

### 3.14 AgentExtension

```
message AgentExtension {
  string uri          // unique extension URI
  string description  // human-readable
  bool required       // client must understand/comply
  Struct params       // optional config parameters
}
```

### 3.15 Security Schemes

```
message SecurityScheme {
  oneof scheme {
    APIKeySecurityScheme api_key_security_scheme
    HTTPAuthSecurityScheme http_auth_security_scheme
    OAuth2SecurityScheme oauth2_security_scheme
    OpenIdConnectSecurityScheme open_id_connect_security_scheme
    MutualTlsSecurityScheme mtls_security_scheme
  }
}
```

**APIKeySecurityScheme:** `{ description, location ("query"|"header"|"cookie"), name }`

**HTTPAuthSecurityScheme:** `{ description, scheme (e.g. "Bearer"), bearer_format (e.g. "JWT") }`

**OAuth2SecurityScheme:** `{ description, OAuthFlows flows, oauth2_metadata_url }`

**OpenIdConnectSecurityScheme:** `{ description, open_id_connect_url }`

**MutualTlsSecurityScheme:** `{ description }`

**OAuthFlows (oneof):**
- `AuthorizationCodeOAuthFlow` — `{ authorization_url, token_url, refresh_url, scopes, pkce_required }`
- `ClientCredentialsOAuthFlow` — `{ token_url, refresh_url, scopes }`
- `DeviceCodeOAuthFlow` — `{ device_authorization_url, token_url, refresh_url, scopes }`

### 3.16 Push Notification Config

```
message PushNotificationConfig {
  string id              // unique ID
  string url             // REQUIRED - webhook URL
  string token           // optional verification token
  AuthenticationInfo authentication  // optional
}

message AuthenticationInfo {
  string scheme      // REQUIRED - e.g. "Bearer", "Basic"
  string credentials // auth credentials
}
```

### 3.17 Request/Response Messages

| Request | Fields |
|---------|--------|
| **SendMessageRequest** | `tenant?`, `message` (REQUIRED), `configuration?`, `metadata?` |
| **SendMessageConfiguration** | `accepted_output_modes[]`, `push_notification_config?`, `history_length?`, `blocking` |
| **GetTaskRequest** | `tenant?`, `id` (REQUIRED), `history_length?` |
| **ListTasksRequest** | `tenant?`, `context_id?`, `status?`, `page_size?`, `page_token?`, `history_length?`, `status_timestamp_after?`, `include_artifacts?` |
| **CancelTaskRequest** | `tenant?`, `id` (REQUIRED) |
| **SubscribeToTaskRequest** | `tenant?`, `id` (REQUIRED) |
| **GetExtendedAgentCardRequest** | `tenant?` |

| Response | Fields |
|----------|--------|
| **SendMessageResponse** | `oneof { Task task, Message message }` |
| **StreamResponse** | `oneof { Task, Message, TaskStatusUpdateEvent, TaskArtifactUpdateEvent }` |
| **ListTasksResponse** | `tasks[]` (REQUIRED), `next_page_token` (REQUIRED), `page_size` (REQUIRED), `total_size` (REQUIRED) |

---

## 4. Communication Flow

### 4.1 Agent Discovery

```
  Client                          Server
    │                               │
    │  GET /.well-known/agent-card.json
    │──────────────────────────────>│
    │                               │
    │  200 OK { AgentCard JSON }    │
    │<──────────────────────────────│
```

**Discovery strategies:**

1. **Well-Known URI**: `https://{domain}/.well-known/agent-card.json` (RFC 8615)
2. **Curated Registries**: Central catalog service, query by skill/tag/capability
3. **Direct Configuration**: Hardcoded URLs, config files, environment variables

### 4.2 Sending a Task (Basic Flow)

```
  Client                          Server
    │                               │
    │  POST /message:send           │
    │  { message, configuration }   │
    │──────────────────────────────>│
    │                               │
    │  200 OK                       │
    │  { task: { id, contextId,     │
    │    status: { state:           │
    │    "TASK_STATE_COMPLETED" },  │
    │    artifacts: [...] }}        │
    │<──────────────────────────────│
```

### 4.3 Task Lifecycle (State Machine)

```
                    ┌─────────────┐
                    │  SUBMITTED  │
                    └──────┬──────┘
                           │
                    ┌──────▼──────┐
               ┌───>│   WORKING   │<───┐
               │    └──────┬──────┘    │
               │           │           │
        ┌──────▼──────┐    │    ┌──────▼──────────┐
        │INPUT_REQUIRED│    │    │  AUTH_REQUIRED   │
        │ (interrupted)│    │    │  (interrupted)   │
        └──────┬──────┘    │    └──────┬──────────┘
               │           │           │
               └───────────┤───────────┘
                           │
              ┌────────────┼────────────┐
              │            │            │
       ┌──────▼───┐ ┌──────▼───┐ ┌─────▼────┐
       │COMPLETED │ │  FAILED  │ │ CANCELED │
       │(terminal)│ │(terminal)│ │(terminal)│
       └──────────┘ └──────────┘ └──────────┘
                                 ┌──────────┐
                                 │ REJECTED │
                                 │(terminal)│
                                 └──────────┘
```

**Key rules:**
- Once a task reaches a **terminal state**, it is **immutable** — no restart
- Follow-up work creates a **new task** within the same `contextId`
- Interrupted states (`input_required`, `auth_required`) accept additional messages to continue

### 4.4 Multi-Turn Interaction Flow

```
  Client                            Server
    │                                  │
    │  POST /message:send              │
    │  "Book me a flight"              │
    │─────────────────────────────────>│
    │                                  │
    │  200 { task.status.state =       │
    │    "TASK_STATE_INPUT_REQUIRED",  │
    │    message: "Where from/to?" }   │
    │<─────────────────────────────────│
    │                                  │
    │  POST /message:send              │
    │  { taskId, "SF to NY" }          │
    │─────────────────────────────────>│
    │                                  │
    │  200 { task.status.state =       │
    │    "TASK_STATE_COMPLETED",       │
    │    artifacts: [booking info] }   │
    │<─────────────────────────────────│
```

### 4.5 Streaming Flow (SSE)

```
  Client                            Server
    │                                  │
    │  POST /message:stream            │
    │─────────────────────────────────>│
    │                                  │
    │  200 text/event-stream           │
    │  data: { task: {...WORKING} }    │
    │<─────────────────────────────────│
    │  data: { artifactUpdate: {...} } │
    │<─────────────────────────────────│
    │  data: { artifactUpdate: {...} } │
    │<─────────────────────────────────│
    │  data: { statusUpdate:           │
    │    {...COMPLETED, final:true} }  │
    │<─────────────────────────────────│
    │  (stream closes)                 │
```

### 4.6 Context & Task ID Semantics

| Identifier | Generated By | Purpose |
|------------|-------------|---------|
| `contextId` | **Server** (auto-generated if not provided by client) | Groups related tasks/messages in a conversation |
| `taskId` | **Server** (always server-generated) | Identifies a single unit of work |
| `messageId` | **Client** (for user messages) or **Server** (for agent messages) | Identifies individual messages |

---

## 5. Transport & Protocol Bindings

### 5.1 Supported Bindings

| Binding | Protocol | Content-Type | Streaming |
|---------|----------|-------------|-----------|
| **JSON-RPC** | JSON-RPC 2.0 over HTTP(S) | `application/json` | SSE (`text/event-stream`) |
| **gRPC** | gRPC over HTTP/2 + TLS | Protocol Buffers v3 | Server streaming RPCs |
| **HTTP+JSON/REST** | Standard HTTP(S) | `application/json` | SSE (`text/event-stream`) |
| **Custom** | Implementer-defined | Must document | Must document |

### 5.2 Method Mapping (All Three Bindings)

| Functionality | JSON-RPC Method | gRPC Method | REST Endpoint |
|---------------|----------------|-------------|---------------|
| Send message | `SendMessage` | `SendMessage` | `POST /message:send` |
| Stream message | `SendStreamingMessage` | `SendStreamingMessage` | `POST /message:stream` |
| Get task | `GetTask` | `GetTask` | `GET /tasks/{id}` |
| List tasks | `ListTasks` | `ListTasks` | `GET /tasks` |
| Cancel task | `CancelTask` | `CancelTask` | `POST /tasks/{id}:cancel` |
| Subscribe to task | `SubscribeToTask` | `SubscribeToTask` | `POST /tasks/{id}:subscribe` |
| Create push notif config | `CreateTaskPushNotificationConfig` | `CreateTaskPushNotificationConfig` | `POST /tasks/{id}/pushNotificationConfigs` |
| Get push notif config | `GetTaskPushNotificationConfig` | `GetTaskPushNotificationConfig` | `GET /tasks/{id}/pushNotificationConfigs/{configId}` |
| List push notif configs | `ListTaskPushNotificationConfig` | `ListTaskPushNotificationConfig` | `GET /tasks/{id}/pushNotificationConfigs` |
| Delete push notif config | `DeleteTaskPushNotificationConfig` | `DeleteTaskPushNotificationConfig` | `DELETE /tasks/{id}/pushNotificationConfigs/{configId}` |
| Get extended Agent Card | `GetExtendedAgentCard` | `GetExtendedAgentCard` | `GET /extendedAgentCard` |

### 5.3 Multi-Tenancy URL Patterns

Every endpoint has an additional binding with `/{tenant}/` prefix:
- `POST /message:send` → `POST /{tenant}/message:send`
- `GET /tasks/{id}` → `GET /{tenant}/tasks/{id}`

### 5.4 Error Code Mappings

| A2A Error | JSON-RPC Code | gRPC Status | HTTP Status |
|-----------|--------------|-------------|-------------|
| `TaskNotFoundError` | `-32001` | `NOT_FOUND` | `404` |
| `TaskNotCancelableError` | `-32002` | `FAILED_PRECONDITION` | `409` |
| `PushNotificationNotSupportedError` | `-32003` | `UNIMPLEMENTED` | `400` |
| `UnsupportedOperationError` | `-32004` | `UNIMPLEMENTED` | `400` |
| `ContentTypeNotSupportedError` | `-32005` | `INVALID_ARGUMENT` | `415` |
| `InvalidAgentResponseError` | `-32006` | `INTERNAL` | `502` |
| `ExtendedAgentCardNotConfiguredError` | `-32007` | `FAILED_PRECONDITION` | `400` |
| `ExtensionSupportRequiredError` | `-32008` | `FAILED_PRECONDITION` | `400` |
| `VersionNotSupportedError` | `-32009` | `UNIMPLEMENTED` | `400` |

### 5.5 Service Parameters (Headers)

| Header | Purpose | Example |
|--------|---------|---------|
| `A2A-Version` | Protocol version (Major.Minor) | `1.0` |
| `A2A-Extensions` | Comma-separated extension URIs to activate | `https://example.com/ext/geo/v1` |

### 5.6 JSON Conventions

- **Field naming**: camelCase (proto `context_id` → JSON `contextId`)
- **Enums**: String names as in proto (e.g. `"TASK_STATE_COMPLETED"`, `"ROLE_USER"`)
- **Timestamps**: ISO 8601 UTC (`"2025-10-28T10:30:00.000Z"`)
- **Media type**: `application/a2a+json` (registered)

---

## 6. Authentication & Security

### 6.1 Design Philosophy

A2A **delegates** authentication to standard web mechanisms. Identity info is carried at the **HTTP transport layer**, never inside A2A payloads.

### 6.2 Authentication Flow

```
1. Client discovers auth requirements via AgentCard.securitySchemes
2. Client obtains credentials out-of-band (OAuth flow, API key distribution, etc.)
3. Client sends credentials in HTTP headers with every request
4. Server validates credentials on every request
```

### 6.3 Supported Security Schemes

| Scheme | Proto Type | Example |
|--------|-----------|---------|
| **API Key** | `APIKeySecurityScheme` | API key in header/query/cookie |
| **HTTP Auth** | `HTTPAuthSecurityScheme` | `Authorization: Bearer <JWT>` |
| **OAuth 2.0** | `OAuth2SecurityScheme` | AuthCode, ClientCredentials, DeviceCode flows |
| **OpenID Connect** | `OpenIdConnectSecurityScheme` | OIDC discovery URL |
| **Mutual TLS** | `MutualTlsSecurityScheme` | Client certificate authentication |

### 6.4 In-Task Authentication (Secondary Credentials)

If an agent needs additional credentials during execution:

1. Task transitions to `TASK_STATE_AUTH_REQUIRED`
2. Status message describes what's needed
3. Client obtains secondary credentials out-of-band (e.g., OAuth flow for third-party service)
4. Client provides credentials in subsequent message

### 6.5 Transport Security

- **MUST** use HTTPS in production
- TLS 1.2+ (TLS 1.3 recommended)
- Server TLS certificate validation required
- HSTS headers recommended

### 6.6 Agent Card Signing (JWS)

Agent Cards may be digitally signed using **JSON Web Signature (RFC 7515)**:

```json
{
  "signatures": [{
    "protected": "eyJhbGciOiJFUzI1NiIsInR5cCI6IkpPU0UiLCJraWQiOiJrZXktMSJ9",
    "signature": "QFdkNLNszlGj3z3u0YQGt_T9LixY3..."
  }]
}
```

Canonicalization uses **RFC 8785 (JSON Canonicalization Scheme)** before signing.

---

## 7. Push Notifications

### 7.1 How It Works

```
  Client                    Server                  Client Webhook
    │                         │                          │
    │ POST /message:send      │                          │
    │ { pushNotificationConfig:                          │
    │   { url, token, auth }} │                          │
    │────────────────────────>│                          │
    │                         │                          │
    │ 200 { task: SUBMITTED } │                          │
    │<────────────────────────│                          │
    │                         │                          │
    │                         │  (task completes)        │
    │                         │                          │
    │                         │  POST {webhook_url}      │
    │                         │  Authorization: Bearer.. │
    │                         │  { statusUpdate: {       │
    │                         │    COMPLETED } }         │
    │                         │─────────────────────────>│
    │                         │                          │
    │                         │  200 OK                  │
    │                         │<─────────────────────────│
```

### 7.2 Configuration

```json
{
  "url": "https://client.example.com/webhook/a2a",
  "token": "secure-client-token-for-task",
  "authentication": {
    "scheme": "Bearer",
    "credentials": "..."
  }
}
```

### 7.3 Webhook Payload

The payload is a `StreamResponse` object — same format as streaming:
- `task` — full Task object
- `message` — Message object
- `statusUpdate` — TaskStatusUpdateEvent
- `artifactUpdate` — TaskArtifactUpdateEvent

### 7.4 CRUD Operations

| Operation | Endpoint |
|-----------|----------|
| Create | `POST /tasks/{id}/pushNotificationConfigs` |
| Get | `GET /tasks/{id}/pushNotificationConfigs/{configId}` |
| List | `GET /tasks/{id}/pushNotificationConfigs` |
| Delete | `DELETE /tasks/{id}/pushNotificationConfigs/{configId}` |

### 7.5 Security Requirements

**Server (webhook caller):**
- Must authenticate to webhook using configured scheme
- Must validate webhook URLs to prevent SSRF (reject private IPs, localhost)
- Should implement retry with exponential backoff
- Recommended timeout: 10-30 seconds

**Client (webhook receiver):**
- Must validate webhook authenticity (verify signatures/tokens)
- Must respond with HTTP 2xx
- Should process notifications idempotently (duplicates possible)
- Should implement rate limiting

---

## 8. Enterprise Considerations

### 8.1 Multi-Tenancy

Built into the protocol at the URL level:

```
POST /message:send            → single-tenant
POST /{tenant}/message:send   → multi-tenant
```

Every gRPC/REST endpoint has a `tenant` field in request messages and an `additional_bindings` URL pattern with `/{tenant}/` prefix.

### 8.2 Authorization Scoping

- Authorization is **implementation-specific** (not prescribed by protocol)
- **Must** scope all operations to authenticated caller's boundaries
- Support models: user-based, role-based, project-based, multi-tenant, custom
- Per-skill authorization via OAuth scopes
- Tasks from `ListTasks` must be filtered by caller's authorized scope

### 8.3 Observability & Tracing

- **Distributed Tracing**: OpenTelemetry, W3C Trace Context headers recommended
- **Logging**: taskId, contextId, correlation IDs, trace context
- **Metrics**: Request rates, error rates, latency, resource utilization
- **Auditing**: Task creation, state changes, agent actions

### 8.4 API Management

Recommended for external agents:
- Centralized policy enforcement (auth, rate limiting, quotas)
- Traffic management (load balancing, routing)
- Analytics and reporting
- Developer portals with Agent Card documentation

### 8.5 Rate Limiting

- Agents **should** implement rate limiting on all operations
- Different limits for different operations or user tiers
- Appropriate error responses when limits exceeded

### 8.6 Data Privacy

- Comply with GDPR, CCPA, HIPAA as applicable
- Data minimization in A2A exchanges
- Protect data in transit (TLS) and at rest
- Provide data deletion mechanisms
- Appropriate retention policies

### 8.7 Versioning Strategy

- Version format: `Major.Minor` (patch not considered for protocol compatibility)
- Client sends `A2A-Version` header with every request
- Server processes using requested version's semantics
- Returns `VersionNotSupportedError` if unsupported
- Empty version header treated as `0.3` (backward compat)

---

## 9. .NET Integration Points

### 9.1 Official .NET SDK

The A2A protocol has an official **.NET SDK**:

- **Package**: `A2A` on NuGet
- **Repository**: [github.com/a2aproject/a2a-dotnet](https://github.com/a2aproject/a2a-dotnet)
- **Install**: `dotnet add package A2A`

### 9.2 Proto C# Namespace

The `a2a.proto` file declares:

```protobuf
option csharp_namespace = "A2a.V1";
```

This means all generated types (from `protoc` / gRPC codegen) land in the `A2a.V1` namespace:

| Proto Message | C# Class |
|--------------|----------|
| `Task` | `A2a.V1.Task` |
| `Message` | `A2a.V1.Message` |
| `AgentCard` | `A2a.V1.AgentCard` |
| `Part` | `A2a.V1.Part` |
| `Artifact` | `A2a.V1.Artifact` |
| `SendMessageRequest` | `A2a.V1.SendMessageRequest` |
| `StreamResponse` | `A2a.V1.StreamResponse` |
| `A2AService` | `A2a.V1.A2AService.A2AServiceClient` (gRPC) |

### 9.3 gRPC Service for .NET

The proto defines the full gRPC service:

```csharp
// Server implementation inherits:
public class MyA2AServer : A2a.V1.A2AService.A2AServiceBase
{
    public override Task<SendMessageResponse> SendMessage(SendMessageRequest request, ServerCallContext context) { ... }
    public override async Task SendStreamingMessage(SendMessageRequest request, IServerStreamWriter<StreamResponse> responseStream, ServerCallContext context) { ... }
    public override Task<Task> GetTask(GetTaskRequest request, ServerCallContext context) { ... }
    public override Task<Task> CancelTask(CancelTaskRequest request, ServerCallContext context) { ... }
    // ...etc
}

// Client usage:
var client = new A2a.V1.A2AService.A2AServiceClient(channel);
var response = await client.SendMessageAsync(request);
```

### 9.4 Integration with ASP.NET Core

For the **HTTP+JSON/REST** binding, the REST endpoints map cleanly to ASP.NET Core Minimal APIs or Controllers:

```
POST /message:send                              → app.MapPost("/message:send", ...)
POST /message:stream                            → SSE endpoint
GET  /tasks/{id}                                → app.MapGet("/tasks/{id}", ...)
GET  /tasks                                     → app.MapGet("/tasks", ...)
POST /tasks/{id}:cancel                         → app.MapPost("/tasks/{id}:cancel", ...)
POST /tasks/{id}:subscribe                      → SSE endpoint
POST /tasks/{id}/pushNotificationConfigs        → app.MapPost(...)
GET  /tasks/{id}/pushNotificationConfigs/{cid}  → app.MapGet(...)
GET  /.well-known/agent-card.json               → Static file or MapGet
```

### 9.5 Multi-Tenant Routing

The proto `additional_bindings` with `/{tenant}/` prefix translates to ASP.NET route parameters:

```csharp
app.MapPost("/{tenant}/message:send", (string tenant, SendMessageRequest req) => ...);
```

---

## 10. A2A vs MCP

| Aspect | A2A | MCP (Model Context Protocol) |
|--------|-----|-----|
| **Focus** | Agent-to-agent collaboration | Agent-to-tool/resource connection |
| **Interaction** | Agents communicate as opaque peers | Agents call structured tool functions |
| **State** | Stateful tasks with lifecycle | Typically stateless function calls |
| **Discovery** | Agent Cards (capabilities, skills) | Tool descriptions (function signatures) |
| **Use Case** | Customer agent delegates to billing agent | Agent calls database query API |

**They are complementary**: An A2A Client asks an A2A Server to perform a complex task. The Server agent internally uses MCP to interact with its tools, APIs, and data sources.

```
User ──A2A──> Agent A ──A2A──> Agent B ──MCP──> Tool 1
                                       ──MCP──> Tool 2
```

---

## Summary

A2A is a comprehensive, production-ready protocol for inter-agent communication. Key takeaways for implementation:

1. **Start with the Agent Card** — define your agent's identity, skills, capabilities, and auth requirements
2. **Choose your binding** — JSON-RPC (simple), gRPC (high-performance), or HTTP+JSON/REST (familiar)
3. **Implement the core operations** — at minimum: `SendMessage`, `GetTask`, Agent Card endpoint
4. **Add streaming/push** as needed for long-running tasks
5. **Use the .NET SDK** (`dotnet add package A2A`) or generate from `a2a.proto` (C# namespace `A2a.V1`)
6. **The proto file is the source of truth** — all other representations are derived from `specification/a2a.proto`
