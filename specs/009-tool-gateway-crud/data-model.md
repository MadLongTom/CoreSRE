# Data Model: Tool Gateway — 工具注册、管理与统一调用

**Feature**: 009-tool-gateway-crud  
**Date**: 2026-02-10  
**Source**: [spec.md](spec.md) Key Entities + [research.md](research.md) R1/R2/R3

---

## Entities

### ToolRegistration（聚合根）

代表一个已注册的工具或工具源，是 Tool Gateway 模块的核心实体。通过 `ToolType` 鉴别器区分 RestApi 和 McpServer 两种类型。

**Inherits from**: `BaseEntity`（提供 Id: Guid, CreatedAt: DateTime, UpdatedAt: DateTime?）

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| Name | string | ✅ | 全局唯一, max 200 chars |
| Description | string? | ❌ | 可选描述文本 |
| ToolType | ToolType | ✅ | 枚举, 注册后不可变更 |
| Status | ToolStatus | ✅ | RestApi 初始 Active, McpServer 初始 Inactive（握手后更新） |
| ConnectionConfig | ConnectionConfigVO | ✅ | 连接配置 (JSONB) |
| AuthConfig | AuthConfigVO | ✅ | 认证配置 (JSONB), AuthType=None 时凭据为空 |
| ToolSchema | ToolSchemaVO? | ❌ | 工具 Schema (JSONB), OpenAPI 导入时填充, 手动注册可空 |
| DiscoveryError | string? | ❌ | MCP 握手/发现失败时的错误信息 |
| ImportSource | string? | ❌ | OpenAPI 导入来源标识（文件名或 URL）|

**Factory Methods**:
- `CreateRestApi(name, description?, endpoint, authConfig)` → 创建 RestApi 类型工具，Status = Active
- `CreateMcpServer(name, description?, endpoint, transportType, authConfig?)` → 创建 McpServer 类型工具，Status = Inactive
- `CreateFromOpenApi(name, description?, endpoint, authConfig?, toolSchema)` → 批量创建 OpenAPI 导入的 RestApi 工具

**Update Method**:
- `Update(name, description?, connectionConfig, authConfig?)` → 按当前类型更新配置, toolType 不可变

**Domain Methods**:
- `MarkActive()` → 将状态设为 Active（MCP 握手成功后调用）
- `MarkInactive(error?)` → 将状态设为 Inactive（MCP 握手失败时调用）
- `SetToolSchema(toolSchema)` → 设置工具 Schema

**Invariants**:
- Name 非空, 非空白, ≤ 200 字符
- ToolType 注册后不可变更
- ConnectionConfig.Endpoint 必须非空
- McpServer 类型: TransportType 必须为 StreamableHttp 或 Stdio
- RestApi 类型: TransportType 必须为 Rest

---

### McpToolItem（实体）

MCP Server 工具源下发现的单个 Tool。通过 MCP `tools/list` 自动发现，关联到父 ToolRegistration。

**Inherits from**: `BaseEntity`（提供 Id: Guid, CreatedAt: DateTime, UpdatedAt: DateTime?）

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| ToolRegistrationId | Guid | ✅ | FK → ToolRegistration.Id, CASCADE DELETE |
| ToolName | string | ✅ | MCP Tool 名称, max 200 chars, 在同一父工具源内唯一 |
| Description | string? | ❌ | Tool 描述 |
| InputSchema | JsonElement? | ❌ | 输入参数 JSON Schema |
| OutputSchema | JsonElement? | ❌ | 输出 JSON Schema |
| Annotations | ToolAnnotationsVO? | ❌ | Tool 注解 (JSONB) |

**Factory Method**:
- `Create(toolRegistrationId, toolName, description?, inputSchema?, outputSchema?, annotations?)` → 创建 MCP 子工具项

**Invariants**:
- ToolRegistrationId 必须非空且非 Guid.Empty
- ToolName 非空, 非空白, ≤ 200 字符

---

## Enums

### ToolType

| Value | Description |
|-------|-------------|
| RestApi | 外部 REST API 工具, 通过 HTTP 调用 |
| McpServer | MCP Server 工具源, 通过 MCP 协议调用 |

### ToolStatus

| Value | Description |
|-------|-------------|
| Active | 可用, 可被调用 |
| Inactive | 不可用 (MCP 握手失败, 手动禁用等) |
| CircuitOpen | 熔断中 (SPEC-014 处理, 本 Spec 仅定义枚举值) |

### AuthType

| Value | Description |
|-------|-------------|
| None | 无认证 |
| ApiKey | API Key 认证, 注入 X-Api-Key 头 |
| Bearer | Bearer Token 认证, 注入 Authorization: Bearer 头 |
| OAuth2 | OAuth2 Client Credentials Grant |

### TransportType

| Value | Description |
|-------|-------------|
| Rest | 标准 REST HTTP 调用 |
| StreamableHttp | MCP Streamable HTTP 传输 |
| Stdio | MCP 标准输入/输出传输（本地进程）|

---

## Value Objects

### ConnectionConfigVO

工具的连接配置。存储为 PostgreSQL JSONB 列。

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| Endpoint | string | ✅ | 工具端点 URL, max 2048 chars |
| TransportType | TransportType | ✅ | 传输类型 |

### AuthConfigVO

工具的认证配置。存储为 PostgreSQL JSONB 列。凭据字段加密存储。

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| AuthType | AuthType | ✅ | 认证类型 |
| EncryptedCredential | string? | ❌ | 加密后的凭据 (Base64), AuthType=None 时为空 |
| ApiKeyHeaderName | string? | ❌ | ApiKey 自定义头名 (默认 "X-Api-Key") |
| TokenEndpoint | string? | ❌ | OAuth2 Token 端点 URL (仅 OAuth2) |
| ClientId | string? | ❌ | OAuth2 Client ID (仅 OAuth2, 加密存储) |
| EncryptedClientSecret | string? | ❌ | OAuth2 Client Secret (加密存储, 仅 OAuth2) |

### ToolSchemaVO

工具的输入/输出 Schema 描述。存储为 PostgreSQL JSONB 列。

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| InputSchema | JsonElement? | ❌ | 输入参数 JSON Schema |
| OutputSchema | JsonElement? | ❌ | 输出结果 JSON Schema |
| Annotations | ToolAnnotationsVO? | ❌ | 工具注解 |

### ToolAnnotationsVO

工具的语义注解。嵌套在 ToolSchemaVO 中。

| Field | Type | Required | Default | Notes |
|-------|------|----------|---------|-------|
| ReadOnly | bool | ❌ | false | 是否只读（不修改外部状态） |
| Destructive | bool | ❌ | false | 是否具有破坏性（需人工审批, SPEC-051） |
| Idempotent | bool | ❌ | false | 是否幂等 |
| OpenWorldHint | bool | ❌ | false | 是否可能与外部不可控系统交互 |

---

## Relationships

```
ToolRegistration (Aggregate Root)
├── 1:1  ConnectionConfigVO    (owned, JSONB)
│   └── TransportType enum
├── 1:1  AuthConfigVO          (owned, JSONB)
│   └── AuthType enum + encrypted credentials
├── 1:1  ToolSchemaVO?         (owned, JSONB, nullable)
│   └── 1:1  ToolAnnotationsVO?   (nested in JSON)
└── 1:N  McpToolItem           (separate table, FK + CASCADE DELETE)
     └── 1:1  ToolAnnotationsVO?  (owned, JSONB, nullable)
```

---

## Database Table: `tool_registrations`

| Column | PostgreSQL Type | Constraint | Index |
|--------|----------------|------------|-------|
| id | uuid | PK | ✅ (PK) |
| name | varchar(200) | NOT NULL | ✅ UNIQUE |
| description | text | nullable | |
| tool_type | varchar(20) | NOT NULL | ✅ |
| status | varchar(20) | NOT NULL | |
| connection_config | jsonb | NOT NULL | |
| auth_config | jsonb | NOT NULL | |
| tool_schema | jsonb | nullable | |
| discovery_error | text | nullable | |
| import_source | varchar(500) | nullable | |
| created_at | timestamptz | NOT NULL | |
| updated_at | timestamptz | nullable | |

## Database Table: `mcp_tool_items`

| Column | PostgreSQL Type | Constraint | Index |
|--------|----------------|------------|-------|
| id | uuid | PK | ✅ (PK) |
| tool_registration_id | uuid | FK NOT NULL, CASCADE DELETE | ✅ |
| tool_name | varchar(200) | NOT NULL | ✅ UNIQUE(tool_registration_id, tool_name) |
| description | text | nullable | |
| input_schema | jsonb | nullable | |
| output_schema | jsonb | nullable | |
| annotations | jsonb | nullable | |
| created_at | timestamptz | NOT NULL | |
| updated_at | timestamptz | nullable | |

---

## Validation Rules Summary

| ToolType | Required Fields | Type-Specific Validation |
|-----------|----------------|------------------------|
| RestApi | name, endpoint | TransportType = Rest, AuthConfig required (可 AuthType=None) |
| McpServer | name, endpoint, transportType | TransportType ∈ {StreamableHttp, Stdio} |
| All | name ≤ 200 chars | toolType ∈ {RestApi, McpServer}, endpoint ≤ 2048 chars |
| OpenAPI Import | 上传文件或 URL | 文件 ≤ 10MB, 至少包含 1 个有效接口 |
