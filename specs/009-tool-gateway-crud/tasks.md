# Tasks: Tool Gateway — 工具注册、管理与统一调用

**Input**: Design documents from `/specs/009-tool-gateway-crud/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/tools-api.yaml ✅, quickstart.md ✅

**Tests**: Not explicitly requested in the feature specification. Test tasks are omitted.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Backend**: `Backend/CoreSRE.Domain/`, `Backend/CoreSRE.Application/`, `Backend/CoreSRE.Infrastructure/`, `Backend/CoreSRE/`
- Based on existing DDD 4-layer structure in plan.md

---

## Phase 1: Setup

**Purpose**: Create folder structure and install new NuGet packages for this feature

- [X] T001 Create directory structure for Tool Gateway feature: `Backend/CoreSRE.Domain/Entities/`, `Backend/CoreSRE.Domain/Enums/`, `Backend/CoreSRE.Domain/ValueObjects/`, `Backend/CoreSRE.Domain/Interfaces/`, `Backend/CoreSRE.Application/Interfaces/`, `Backend/CoreSRE.Application/Tools/DTOs/`, `Backend/CoreSRE.Application/Tools/Commands/RegisterTool/`, `Backend/CoreSRE.Application/Tools/Commands/UpdateTool/`, `Backend/CoreSRE.Application/Tools/Commands/DeleteTool/`, `Backend/CoreSRE.Application/Tools/Commands/ImportOpenApi/`, `Backend/CoreSRE.Application/Tools/Commands/InvokeTool/`, `Backend/CoreSRE.Application/Tools/Queries/GetTools/`, `Backend/CoreSRE.Application/Tools/Queries/GetToolById/`, `Backend/CoreSRE.Application/Tools/Queries/GetMcpTools/`, `Backend/CoreSRE.Infrastructure/Persistence/Configurations/`, `Backend/CoreSRE.Infrastructure/Services/`, `Backend/CoreSRE/Endpoints/`
- [X] T002 Add NuGet package references: `ModelContextProtocol` 0.8.0-preview.1 to `Backend/CoreSRE.Infrastructure/CoreSRE.Infrastructure.csproj`, `Microsoft.OpenApi` 3.3.1 and `Microsoft.OpenApi.YamlReader` 3.3.1 to `Backend/CoreSRE.Infrastructure/CoreSRE.Infrastructure.csproj`. Verify `Microsoft.AspNetCore.DataProtection` is available from shared framework (no explicit package needed).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain model, shared infrastructure, and cross-cutting concerns that ALL user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

### Domain Layer — Enums

- [X] T003 [P] Create `ToolType` enum (RestApi, McpServer) in `Backend/CoreSRE.Domain/Enums/ToolType.cs`
- [X] T004 [P] Create `ToolStatus` enum (Active, Inactive, CircuitOpen) in `Backend/CoreSRE.Domain/Enums/ToolStatus.cs`
- [X] T005 [P] Create `AuthType` enum (None, ApiKey, Bearer, OAuth2) in `Backend/CoreSRE.Domain/Enums/AuthType.cs`
- [X] T006 [P] Create `TransportType` enum (Rest, StreamableHttp, Stdio) in `Backend/CoreSRE.Domain/Enums/TransportType.cs`

### Domain Layer — Value Objects

- [X] T007 [P] Create `ConnectionConfigVO` sealed record (Endpoint: string, TransportType: TransportType) with init-only properties in `Backend/CoreSRE.Domain/ValueObjects/ConnectionConfigVO.cs`
- [X] T008 [P] Create `AuthConfigVO` sealed record (AuthType: AuthType, EncryptedCredential: string?, ApiKeyHeaderName: string?, TokenEndpoint: string?, ClientId: string?, EncryptedClientSecret: string?) with init-only properties in `Backend/CoreSRE.Domain/ValueObjects/AuthConfigVO.cs`
- [X] T009 [P] Create `ToolAnnotationsVO` sealed record (ReadOnly: bool, Destructive: bool, Idempotent: bool, OpenWorldHint: bool) with default false values in `Backend/CoreSRE.Domain/ValueObjects/ToolAnnotationsVO.cs`
- [X] T010 [P] Create `ToolSchemaVO` sealed record (InputSchema: JsonElement?, OutputSchema: JsonElement?, Annotations: ToolAnnotationsVO?) with init-only properties in `Backend/CoreSRE.Domain/ValueObjects/ToolSchemaVO.cs`

### Domain Layer — Entities & Repository Interfaces

- [X] T011 Create `ToolRegistration` aggregate root entity inheriting `BaseEntity` with private setters, factory methods (`CreateRestApi`, `CreateMcpServer`, `CreateFromOpenApi`), `Update` method, domain methods (`MarkActive`, `MarkInactive`, `SetToolSchema`), and invariant guards (Name ≤ 200 chars non-empty, ToolType immutable, TransportType-ToolType consistency) in `Backend/CoreSRE.Domain/Entities/ToolRegistration.cs`
- [X] T012 Create `McpToolItem` entity inheriting `BaseEntity` with factory method `Create(toolRegistrationId, toolName, description?, inputSchema?, outputSchema?, annotations?)`, invariant guards (ToolRegistrationId non-empty, ToolName ≤ 200 chars non-empty) in `Backend/CoreSRE.Domain/Entities/McpToolItem.cs`
- [X] T013 [P] Create `IToolRegistrationRepository` interface extending `IRepository<ToolRegistration>` with `GetByTypeAsync(ToolType? type)`, `GetByNameAsync(string name)`, `GetPagedAsync(ToolType?, ToolStatus?, string? search, int page, int pageSize)` methods in `Backend/CoreSRE.Domain/Interfaces/IToolRegistrationRepository.cs`
- [X] T014 [P] Create `IMcpToolItemRepository` interface extending `IRepository<McpToolItem>` with `GetByToolRegistrationIdAsync(Guid toolRegistrationId)`, `DeleteByToolRegistrationIdAsync(Guid toolRegistrationId)` methods in `Backend/CoreSRE.Domain/Interfaces/IMcpToolItemRepository.cs`

### Application Layer — Service Interfaces

- [X] T015 [P] Create `ICredentialEncryptionService` interface with `Encrypt(string plaintext)`, `Decrypt(string ciphertext)`, `Mask(string ciphertext, int visibleChars = 4)` methods in `Backend/CoreSRE.Application/Interfaces/ICredentialEncryptionService.cs`
- [X] T016 [P] Create `IMcpToolDiscoveryService` interface with `DiscoverToolsAsync(ToolRegistration registration, CancellationToken ct)` returning `Result<IReadOnlyList<McpToolItem>>` in `Backend/CoreSRE.Application/Interfaces/IMcpToolDiscoveryService.cs`
- [X] T017 [P] Create `IToolInvoker` interface with `InvokeAsync(ToolRegistration tool, string? mcpToolName, IDictionary<string, object?> parameters, CancellationToken ct)` returning `ToolInvocationResultDto` and `bool CanHandle(ToolType toolType)` method in `Backend/CoreSRE.Application/Interfaces/IToolInvoker.cs`
- [X] T018 [P] Create `IOpenApiParserService` interface with `ParseAsync(Stream document, string? baseUrl, CancellationToken ct)` returning `Result<IReadOnlyList<ParsedToolDefinition>>` in `Backend/CoreSRE.Application/Interfaces/IOpenApiParserService.cs`

### Application Layer — DTOs & Mapping

- [X] T019 [P] Create `ToolRegistrationDto` (full detail: Id, Name, Description, ToolType, Status, ConnectionConfig, AuthConfig with hasCredential instead of encrypted values, ToolSchema, DiscoveryError, ImportSource, McpToolCount, CreatedAt, UpdatedAt) in `Backend/CoreSRE.Application/Tools/DTOs/ToolRegistrationDto.cs`
- [X] T020 [P] Create `McpToolItemDto` (Id, ToolRegistrationId, ToolName, Description, InputSchema, OutputSchema, Annotations, CreatedAt) in `Backend/CoreSRE.Application/Tools/DTOs/McpToolItemDto.cs`
- [X] T021 [P] Create `ToolInvocationResultDto` (Success: bool, Data: JsonElement?, Error: string?, DurationMs: long, ToolRegistrationId: Guid, InvokedAt: DateTime) in `Backend/CoreSRE.Application/Tools/DTOs/ToolInvocationResultDto.cs`
- [X] T022 [P] Create `OpenApiImportResultDto` (TotalOperations: int, ImportedCount: int, SkippedCount: int, Tools: List<ToolRegistrationDto>, Errors: List<string>) in `Backend/CoreSRE.Application/Tools/DTOs/OpenApiImportResultDto.cs`
- [X] T023 Create `ToolMappingProfile` AutoMapper profile mapping ToolRegistration → ToolRegistrationDto (with credential masking via ICredentialEncryptionService), McpToolItem → McpToolItemDto, VOs → nested DTOs in `Backend/CoreSRE.Application/Tools/DTOs/ToolMappingProfile.cs`

### Infrastructure Layer — Persistence

- [X] T024 Create `ToolRegistrationConfiguration` EF Core entity configuration with `ToJson()` for ConnectionConfigVO, AuthConfigVO, ToolSchemaVO JSONB columns, unique index on Name, index on ToolType, enum-to-string conversion for ToolType/ToolStatus, table name `tool_registrations` with snake_case columns in `Backend/CoreSRE.Infrastructure/Persistence/Configurations/ToolRegistrationConfiguration.cs`
- [X] T025 Create `McpToolItemConfiguration` EF Core entity configuration with FK to ToolRegistration (CASCADE DELETE), `ToJson()` for ToolAnnotationsVO, unique composite index on (ToolRegistrationId, ToolName), table name `mcp_tool_items` with snake_case columns in `Backend/CoreSRE.Infrastructure/Persistence/Configurations/McpToolItemConfiguration.cs`
- [X] T026 Create `ToolRegistrationRepository` implementing `IToolRegistrationRepository` with `GetByTypeAsync`, `GetByNameAsync`, `GetPagedAsync` (IQueryable filter + pagination) in `Backend/CoreSRE.Infrastructure/Persistence/ToolRegistrationRepository.cs`
- [X] T027 Create `McpToolItemRepository` implementing `IMcpToolItemRepository` with `GetByToolRegistrationIdAsync`, `DeleteByToolRegistrationIdAsync` in `Backend/CoreSRE.Infrastructure/Persistence/McpToolItemRepository.cs`
- [X] T028 Add `DbSet<ToolRegistration>` and `DbSet<McpToolItem>` to `AppDbContext` in `Backend/CoreSRE.Infrastructure/Persistence/AppDbContext.cs`

### Infrastructure Layer — Core Services

- [X] T029 Create `CredentialEncryptionService` implementing `ICredentialEncryptionService` using `IDataProtector` with purpose string `"CoreSRE.Infrastructure.CredentialEncryption.v1"`, `Encrypt` → `protector.Protect()`, `Decrypt` → `protector.Unprotect()`, `Mask` → show last N chars in `Backend/CoreSRE.Infrastructure/Services/CredentialEncryptionService.cs`

### Infrastructure Layer — DI Registration

- [X] T030 Register all Tool Gateway services in DI: `IToolRegistrationRepository`, `IMcpToolItemRepository`, `ICredentialEncryptionService`, `IOpenApiParserService`, `IMcpToolDiscoveryService`, `RestApiToolInvoker`, `McpToolInvoker`, `ToolInvokerFactory` (as keyed/enumerable IToolInvoker), `McpDiscoveryBackgroundService` as IHostedService in `Backend/CoreSRE.Infrastructure/DependencyInjection.cs`

### API Layer — Endpoint Scaffold

- [X] T031 Create `ToolEndpoints` static class with `MapToolEndpoints()` extension method using `MapGroup("/api/tools")`, `.WithTags("Tools")`, `.WithOpenApi()` in `Backend/CoreSRE/Endpoints/ToolEndpoints.cs` and register `app.MapToolEndpoints()` in `Backend/CoreSRE/Program.cs`

**Checkpoint**: Foundation ready — domain model, persistence, encryption, DI, and endpoint scaffold in place. User story implementation can now begin.

---

## Phase 3: User Story 1 — 注册 REST API 工具 (Priority: P1) 🎯 MVP

**Goal**: Register an external REST API as a managed tool via `POST /api/tools` with toolType=RestApi, including credential encryption

**Independent Test**: Send `POST /api/tools` with toolType: RestApi, verify 201 + Active status. Then `GET /api/tools/{id}` confirms credential is masked. Verify 400 for missing endpoint, 409 for duplicate name.

### Implementation for User Story 1

- [X] T032 [P] [US1] Create `RegisterToolCommand` record (Name, Description, ToolType, ConnectionConfig: {Endpoint, TransportType}, AuthConfig: {AuthType, Credential?, ApiKeyHeaderName?, TokenEndpoint?, ClientId?, ClientSecret?}) in `Backend/CoreSRE.Application/Tools/Commands/RegisterTool/RegisterToolCommand.cs`
- [X] T033 [P] [US1] Create `RegisterToolCommandValidator` with FluentValidation rules: Name required/max 200, ToolType valid enum, Endpoint required/max 2048, type-conditional rules (RestApi → TransportType=Rest; McpServer → TransportType ∈ {StreamableHttp, Stdio}), AuthType-conditional rules (ApiKey/Bearer → Credential required; OAuth2 → ClientId+ClientSecret+TokenEndpoint required) in `Backend/CoreSRE.Application/Tools/Commands/RegisterTool/RegisterToolCommandValidator.cs`
- [X] T034 [US1] Create `RegisterToolCommandHandler` that encrypts credential via `ICredentialEncryptionService`, maps command → domain factory method (`CreateRestApi` / `CreateMcpServer`), saves via repository, checks unique name conflict → Result.Conflict(), returns Result<ToolRegistrationDto>. For McpServer type, publishes ToolRegistrationId to MCP discovery channel. in `Backend/CoreSRE.Application/Tools/Commands/RegisterTool/RegisterToolCommandHandler.cs`
- [X] T035 [US1] Add `POST /api/tools` endpoint handler in `ToolEndpoints` that sends `RegisterToolCommand` via MediatR, maps Result to 201/400/409 HTTP responses with Location header in `Backend/CoreSRE/Endpoints/ToolEndpoints.cs`

**Checkpoint**: REST API tool registration works end-to-end — POST creates with encrypted credentials, returns 201 with Active status. Validation errors return 400. Duplicate names return 409.

---

## Phase 4: User Story 4 — 查询工具列表与详情 (Priority: P1)

**Goal**: List all tools with optional type/status/search filters and pagination, get individual tool details by ID

**Independent Test**: After registering tools, `GET /api/tools` returns paginated list; `GET /api/tools?toolType=RestApi` returns filtered; `GET /api/tools/{id}` returns full detail with masked credentials. Non-existent ID returns 404.

### Implementation for User Story 4

- [X] T036 [P] [US4] Create `GetToolsQuery` record (ToolType?, ToolStatus?, Search: string?, Page: int, PageSize: int) and `GetToolsQueryHandler` that calls `IToolRegistrationRepository.GetPagedAsync()`, maps to `PagedResult<ToolRegistrationDto>` in `Backend/CoreSRE.Application/Tools/Queries/GetTools/GetToolsQuery.cs` and `Backend/CoreSRE.Application/Tools/Queries/GetTools/GetToolsQueryHandler.cs`
- [X] T037 [P] [US4] Create `GetToolByIdQuery` record (Guid Id) and `GetToolByIdQueryHandler` that calls repository.GetByIdAsync(), maps to Result<ToolRegistrationDto> or Result.NotFound() in `Backend/CoreSRE.Application/Tools/Queries/GetToolById/GetToolByIdQuery.cs` and `Backend/CoreSRE.Application/Tools/Queries/GetToolById/GetToolByIdQueryHandler.cs`
- [X] T038 [US4] Add `GET /api/tools` (with optional `?toolType=`, `?status=`, `?search=`, `?page=`, `?pageSize=` query params) and `GET /api/tools/{id}` endpoint handlers in `ToolEndpoints`, map Result to 200/404 HTTP responses in `Backend/CoreSRE/Endpoints/ToolEndpoints.cs`

**Checkpoint**: Query endpoints functional — paginated list with filters works, detail by ID works with masked credentials, 404 for missing tools.

---

## Phase 5: User Story 5 — 更新与注销工具 (Priority: P1)

**Goal**: Update tool configuration (name, description, connectionConfig, authConfig) via PUT, delete tool via DELETE with cascade

**Independent Test**: Register a tool, PUT to update endpoint and credentials, verify 200 with new config. DELETE returns 204, subsequent GET returns 404. PUT with toolType change returns 400.

### Implementation for User Story 5

- [X] T039 [P] [US5] Create `UpdateToolCommand` record (Guid Id, Name, Description, ConnectionConfig, AuthConfig) and `UpdateToolCommandValidator` with same field rules as RegisterToolCommandValidator (no toolType in request — loaded from existing entity) in `Backend/CoreSRE.Application/Tools/Commands/UpdateTool/UpdateToolCommand.cs` and `Backend/CoreSRE.Application/Tools/Commands/UpdateTool/UpdateToolCommandValidator.cs`
- [X] T040 [US5] Create `UpdateToolCommandHandler` that loads entity by ID (→ NotFound), re-encrypts credential if changed, calls entity.Update(), saves, catches unique name conflict → Conflict(), returns Result<ToolRegistrationDto>. If McpServer connection config changed, re-publishes to MCP discovery channel. in `Backend/CoreSRE.Application/Tools/Commands/UpdateTool/UpdateToolCommandHandler.cs`
- [X] T041 [P] [US5] Create `DeleteToolCommand` record (Guid Id) and `DeleteToolCommandHandler` that loads entity by ID (→ NotFound), calls repository.DeleteAsync(), saves via UnitOfWork, returns Result in `Backend/CoreSRE.Application/Tools/Commands/DeleteTool/DeleteToolCommand.cs` and `Backend/CoreSRE.Application/Tools/Commands/DeleteTool/DeleteToolCommandHandler.cs`
- [X] T042 [US5] Add `PUT /api/tools/{id}` and `DELETE /api/tools/{id}` endpoint handlers in `ToolEndpoints` that send commands via MediatR, map Result to 200/204/400/404/409 HTTP responses in `Backend/CoreSRE/Endpoints/ToolEndpoints.cs`

**Checkpoint**: Update and delete work — config changes persist with re-encrypted credentials, toolType immutable, cascade delete removes McpToolItems. Full CRUD lifecycle complete for RestApi tools.

---

## Phase 6: User Story 2 — 注册 MCP Server 工具源 (Priority: P1)

**Goal**: Register MCP Server tool source, async MCP handshake + tools/list discovery, store discovered McpToolItems

**Independent Test**: POST McpServer registration returns 201 with Inactive status. After background discovery completes, GET shows Active status and mcpToolCount > 0. GET `/api/tools/{id}/mcp-tools` returns discovered tool items.

### Implementation for User Story 2

- [X] T043 [P] [US2] Create `McpToolDiscoveryService` implementing `IMcpToolDiscoveryService` using `McpClient.CreateAsync()` for handshake, `client.ListToolsAsync()` for tool discovery, maps each discovered tool to `McpToolItem.Create()`, returns list. Handles connection failures gracefully (returns Result.Failure with error message). In `Backend/CoreSRE.Infrastructure/Services/McpToolDiscoveryService.cs`
- [X] T044 [P] [US2] Create `McpDiscoveryBackgroundService` as `BackgroundService` with `Channel<Guid>` consumer — reads ToolRegistrationIds, calls `IMcpToolDiscoveryService.DiscoverToolsAsync()`, on success: saves McpToolItems via repository + calls `tool.MarkActive()`, on failure: calls `tool.MarkInactive(error)` + sets DiscoveryError. Provide `IMcpDiscoveryChannel` or public `Channel<Guid>` for producers. In `Backend/CoreSRE.Infrastructure/Services/McpDiscoveryBackgroundService.cs`
- [X] T045 [US2] Create `GetMcpToolsQuery` record (Guid ToolRegistrationId) and `GetMcpToolsQueryHandler` that validates the tool is McpServer type (→ 400 if not), calls `IMcpToolItemRepository.GetByToolRegistrationIdAsync()`, maps to `List<McpToolItemDto>` in `Backend/CoreSRE.Application/Tools/Queries/GetMcpTools/GetMcpToolsQuery.cs` and `Backend/CoreSRE.Application/Tools/Queries/GetMcpTools/GetMcpToolsQueryHandler.cs`
- [X] T046 [US2] Add `GET /api/tools/{id}/mcp-tools` endpoint handler in `ToolEndpoints` that sends `GetMcpToolsQuery` via MediatR, maps Result to 200/400/404 HTTP responses in `Backend/CoreSRE/Endpoints/ToolEndpoints.cs`

**Checkpoint**: MCP Server registration works — registers with Inactive status, background service discovers tools asynchronously, GET mcp-tools returns discovered items. Failed handshake records DiscoveryError.

---

## Phase 7: User Story 3 — 通过 OpenAPI 文档批量导入工具 (Priority: P1)

**Goal**: Upload OpenAPI JSON/YAML document, parse operations, batch-create RestApi tool registrations with extracted schemas

**Independent Test**: Upload a multi-operation OpenAPI doc via `POST /api/tools/import-openapi`, verify importedCount matches operations. Each tool has toolSchema populated. Missing operationId falls back to `{method}_{path}`. Invalid doc returns 400.

### Implementation for User Story 3

- [X] T047 [P] [US3] Create `OpenApiParserService` implementing `IOpenApiParserService` using `Microsoft.OpenApi`: load document via `OpenApiDocument.LoadAsync()`, register YAML reader via `settings.AddYamlReader()`, iterate `document.Paths` → `operations`, extract per-operation: name (operationId or `{method}_{path}`), description (summary), inputSchema (parameters + requestBody merged), outputSchema (responses 200 content schema), endpoint (servers[0].url + path). Return `List<ParsedToolDefinition>`. Handle parse errors via Diagnostic reporting. In `Backend/CoreSRE.Infrastructure/Services/OpenApiParserService.cs`
- [X] T048 [P] [US3] Create `ImportOpenApiCommand` record (Stream Document, string? BaseUrl, AuthConfig?) and `ImportOpenApiCommandValidator` (document stream required, not empty) in `Backend/CoreSRE.Application/Tools/Commands/ImportOpenApi/ImportOpenApiCommand.cs` and `Backend/CoreSRE.Application/Tools/Commands/ImportOpenApi/ImportOpenApiCommandValidator.cs`
- [X] T049 [US3] Create `ImportOpenApiCommandHandler` that calls `IOpenApiParserService.ParseAsync()`, for each parsed tool definition: creates `ToolRegistration.CreateFromOpenApi()` with encrypted authConfig (if provided), saves batch, returns `OpenApiImportResultDto` with imported/skipped counts in `Backend/CoreSRE.Application/Tools/Commands/ImportOpenApi/ImportOpenApiCommandHandler.cs`
- [X] T050 [US3] Add `POST /api/tools/import-openapi` endpoint handler in `ToolEndpoints` accepting `IFormFile` (multipart/form-data), size limit 10MB, sends `ImportOpenApiCommand`, maps Result to 200/400 HTTP responses in `Backend/CoreSRE/Endpoints/ToolEndpoints.cs`

**Checkpoint**: OpenAPI import works — upload JSON or YAML doc, system parses and batch-creates tools with schemas. Invalid format returns structured errors. All imported tools are RestApi type with Active status.

---

## Phase 8: User Story 6 — 统一调用工具 (Priority: P1)

**Goal**: Unified tool invocation via `POST /api/tools/{id}/invoke` with automatic auth injection and protocol adaptation

**Independent Test**: Invoke a RestApi tool — verify Gateway injects auth header and returns standardized result. Invoke a McpServer tool with mcpToolName — verify MCP protocol call and standardized result. Non-existent tool returns 404, unreachable endpoint returns 502.

### Implementation for User Story 6

- [X] T051 [P] [US6] Create `RestApiToolInvoker` implementing `IToolInvoker` (CanHandle: ToolType.RestApi): decrypt credential via `ICredentialEncryptionService`, inject auth header by AuthType (None/ApiKey/Bearer/OAuth2), build HttpRequestMessage with parameters, send via `IHttpClientFactory`, parse JSON response, measure duration, return `ToolInvocationResultDto` in `Backend/CoreSRE.Infrastructure/Services/RestApiToolInvoker.cs`
- [X] T052 [P] [US6] Create `McpToolInvoker` implementing `IToolInvoker` (CanHandle: ToolType.McpServer): create per-invocation `McpClient` via `McpClient.CreateAsync()`, call `client.CallToolAsync(mcpToolName, parameters)`, parse Content result, dispose client, measure duration, return `ToolInvocationResultDto` in `Backend/CoreSRE.Infrastructure/Services/McpToolInvoker.cs`
- [X] T053 [US6] Create `ToolInvokerFactory` that takes `IEnumerable<IToolInvoker>` via DI, selects invoker by `CanHandle(toolType)`. Expose `GetInvoker(ToolType)` method. Register as singleton in `Backend/CoreSRE.Infrastructure/Services/ToolInvokerFactory.cs`
- [X] T054 [P] [US6] Create `InvokeToolCommand` record (Guid ToolRegistrationId, string? McpToolName, IDictionary<string, object?> Parameters, string? HttpMethod) and `InvokeToolCommandValidator` (ToolRegistrationId required; McpToolName required-if McpServer type at handler level) in `Backend/CoreSRE.Application/Tools/Commands/InvokeTool/InvokeToolCommand.cs` and `Backend/CoreSRE.Application/Tools/Commands/InvokeTool/InvokeToolCommandValidator.cs`
- [X] T055 [US6] Create `InvokeToolCommandHandler` that loads ToolRegistration by ID (→ NotFound), validates status is Active (→ 503), resolves invoker via `ToolInvokerFactory.GetInvoker(toolType)`, calls `InvokeAsync()`, wraps upstream failures as 502, returns `ToolInvocationResultDto` in `Backend/CoreSRE.Application/Tools/Commands/InvokeTool/InvokeToolCommandHandler.cs`
- [X] T056 [US6] Add `POST /api/tools/{id}/invoke` endpoint handler in `ToolEndpoints` that sends `InvokeToolCommand` via MediatR, maps Result to 200/400/404/502 HTTP responses in `Backend/CoreSRE/Endpoints/ToolEndpoints.cs`

**Checkpoint**: Unified invocation works — RestApi tools called via HTTP with auto-injected auth, McpServer tools called via MCP protocol. Standardized result returned for both. Error cases (404, 502, inactive) handled correctly.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: EF Core migration, build verification, and quickstart validation

- [X] T057 [P] Generate EF Core migration for `tool_registrations` and `mcp_tool_items` tables by running `dotnet ef migrations add AddToolRegistration` from `Backend/CoreSRE.Infrastructure/`
- [X] T058 Verify solution builds cleanly with `dotnet build Backend/CoreSRE/CoreSRE.slnx`
- [X] T059 Run quickstart.md validation: start Aspire AppHost, execute all curl commands from `specs/009-tool-gateway-crud/quickstart.md`, verify expected HTTP status codes and response shapes

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 — BLOCKS all user stories
- **User Story 1 (Phase 3)**: Depends on Phase 2 — delivers MVP registration for RestApi
- **User Story 4 (Phase 4)**: Depends on Phase 2 — can run in parallel with US1
- **User Story 5 (Phase 5)**: Depends on Phase 2 — can run in parallel with US1/US4
- **User Story 2 (Phase 6)**: Depends on Phase 2 + T034 (RegisterToolCommandHandler publishes to MCP discovery channel) — best after US1
- **User Story 3 (Phase 7)**: Depends on Phase 2 — can run in parallel with US1/US4/US5
- **User Story 6 (Phase 8)**: Depends on Phase 2 + T029 (CredentialEncryptionService) — can start after Foundational but best after US1+US2 for end-to-end testing
- **Polish (Phase 9)**: Depends on ALL user stories being complete

### User Story Dependencies

- **US1 (Register RestApi)**: Phase 2 only — MVP, establishes the registration flow
- **US4 (Query list/detail)**: Phase 2 only — independent of US1 (reads same entity)
- **US5 (Update/Delete)**: Phase 2 only — independent of US1/US4
- **US2 (Register McpServer)**: Phase 2 + registration handler pattern from US1 (shared RegisterToolCommand)
- **US3 (OpenAPI Import)**: Phase 2 only — independent OpenAPI parsing path
- **US6 (Unified Invoke)**: Phase 2 + CredentialEncryptionService — needs registered tools to test

### Within Each User Story

- Command/Query definitions before handlers
- Validators in parallel with commands
- Handlers before endpoint wiring
- Endpoint wiring as final step

### Parallel Opportunities

- **Phase 2**: T003-T006 (enums) in parallel; T007-T010 (VOs) in parallel; T013-T018 (interfaces) in parallel; T019-T022 (DTOs) in parallel; T024-T027 (EF configs + repos) partially parallel
- **Phase 3-8**: US1, US4, US5 can start in parallel after Phase 2; US3 can also parallelize; US2 best after US1; US6 best last
- **Within stories**: Command + Validator marked [P] can be written in parallel

---

## Parallel Example: User Story 1

```bash
# Launch command + validator in parallel (different files):
Task T032: "Create RegisterToolCommand in .../RegisterTool/RegisterToolCommand.cs"
Task T033: "Create RegisterToolCommandValidator in .../RegisterTool/RegisterToolCommandValidator.cs"

# Then sequentially:
Task T034: "Create RegisterToolCommandHandler" (depends on T032, T033)
Task T035: "Add POST endpoint handler" (depends on T034)
```

## Parallel Example: Foundational Phase

```bash
# All enums in parallel:
Task T003-T006: ToolType, ToolStatus, AuthType, TransportType

# All VOs in parallel (after enums):
Task T007-T010: ConnectionConfigVO, AuthConfigVO, ToolAnnotationsVO, ToolSchemaVO

# Then entities (depend on enums + VOs):
Task T011: ToolRegistration entity
Task T012: McpToolItem entity

# Repository interfaces in parallel (after entities):
Task T013-T014: IToolRegistrationRepository, IMcpToolItemRepository

# Application interfaces in parallel:
Task T015-T018: ICredentialEncryptionService, IMcpToolDiscoveryService, IToolInvoker, IOpenApiParserService

# DTOs in parallel:
Task T019-T022: All 4 DTOs in parallel

# Mapping profile (depends on DTOs + entities):
Task T023: ToolMappingProfile

# EF configs + repos in parallel:
Task T024-T025: EF configurations
Task T026-T027: Repository implementations

# Core services + DI + endpoint scaffold:
Task T028-T031: sequential
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001-T002)
2. Complete Phase 2: Foundational (T003-T031)
3. Complete Phase 3: User Story 1 — Register RestApi (T032-T035)
4. **STOP and VALIDATE**: POST RestApi tool, verify 201 + Active status + encrypted credential
5. Ready for demo after 35 tasks

### Incremental Delivery

1. Phase 1 + 2 → Foundation ready
2. Add US1 (Phase 3) → RestApi registration works → **MVP!**
3. Add US4 (Phase 4) → Can query/list/filter tools
4. Add US5 (Phase 5) → Can update and delete tools → Full CRUD for RestApi
5. Add US2 (Phase 6) → MCP Server registration + auto-discovery
6. Add US3 (Phase 7) → OpenAPI bulk import
7. Add US6 (Phase 8) → Unified invocation for both protocols
8. Phase 9 → Migration, build, quickstart validation
9. Each story adds value without breaking previous stories

### Parallel Team Strategy

With multiple developers after Phase 2 completes:

- Developer A: User Story 1 (Register RestApi) → User Story 2 (MCP Server)
- Developer B: User Story 4 (Query) + User Story 5 (Update/Delete)  
- Developer C: User Story 3 (OpenAPI Import) → User Story 6 (Unified Invoke)

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story for traceability
- Each user story is independently testable after Phase 2 foundation
- No test tasks generated (tests not explicitly requested in spec)
- US1 and US2 share `RegisterToolCommand` — US1 establishes the command, US2 adds MCP discovery integration
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Total: 59 tasks across 9 phases
