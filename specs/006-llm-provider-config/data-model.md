# Data Model: LLM Provider 配置与模型发现

**Feature**: 006-llm-provider-config | **Date**: 2026-02-10

## Domain Layer

### Entity: LlmProvider (Aggregate Root)

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | Guid | PK, auto-generated | 唯一标识 |
| Name | string | Required, max 200, unique | Provider 名称 |
| BaseUrl | string | Required, max 500, http/https | OpenAI 兼容 API 的 Base URL |
| ApiKey | string | Required, max 500 | API Key（明文存储，响应中掩码） |
| DiscoveredModels | List\<string\> | 默认空列表 | 已发现的模型 ID 列表 |
| ModelsRefreshedAt | DateTime? | UTC, nullable | 模型列表最后刷新时间 |
| CreatedAt | DateTime | UTC, auto-set | 创建时间 |
| UpdatedAt | DateTime? | UTC, auto-set on modify | 更新时间 |

**Factory Method**: `LlmProvider.Create(string name, string baseUrl, string apiKey) → LlmProvider`
- 验证 name 非空、baseUrl 合法（http/https）、apiKey 非空
- 设置 Id = Guid.NewGuid()，CreatedAt = DateTime.UtcNow

**Behavior Methods**:
- `Update(string name, string baseUrl, string? apiKey)` — 更新基本字段，apiKey 仅在非 null 时更新
- `UpdateDiscoveredModels(List<string> modelIds)` — 替换模型列表，设置 ModelsRefreshedAt = DateTime.UtcNow
- `MaskApiKey() → string` — 返回掩码 Key（如 `sk-****xxxx`，仅显示最后 4 位）

**Invariants**:
- Name 不可为空或纯空白
- BaseUrl 必须以 `http://` 或 `https://` 开头
- ApiKey 不可为空
- DiscoveredModels 不可为 null（空列表可以）

---

### Value Object: LlmConfigVO (MODIFIED — 扩展)

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| ProviderId | Guid? | nullable, 新增 | 关联的 LLM Provider ID |
| ModelId | string | Required | 模型标识（来自 Provider 发现的模型列表或手动输入） |
| Instructions | string? | Optional | System prompt / instructions |
| ToolRefs | List\<Guid\> | 默认空列表 | Tool 引用列表 |

**变更说明**: 新增 `ProviderId` 字段，类型为 `Guid?`。现有 Agent 的 ProviderId 为 null（向后兼容）。新建 ChatClient Agent 时 ProviderId 应从前端 Provider 选择中获取。

---

### Repository Interface: ILlmProviderRepository

```
ILlmProviderRepository : IRepository<LlmProvider>
  + GetByNameAsync(string name) → LlmProvider?
  + ExistsWithNameAsync(string name, Guid? excludeId) → bool
```

---

## Application Layer

### DTOs

#### LlmProviderDto (Full Detail)

| Field | Type | Description |
|-------|------|-------------|
| id | string (Guid) | Provider ID |
| name | string | Provider 名称 |
| baseUrl | string | Base URL |
| maskedApiKey | string | 掩码后的 API Key |
| discoveredModels | string[] | 已发现的模型 ID 列表 |
| modelsRefreshedAt | string? (ISO8601) | 最后刷新时间 |
| createdAt | string (ISO8601) | 创建时间 |
| updatedAt | string? (ISO8601) | 更新时间 |

#### LlmProviderSummaryDto (List Item)

| Field | Type | Description |
|-------|------|-------------|
| id | string (Guid) | Provider ID |
| name | string | Provider 名称 |
| baseUrl | string | Base URL |
| modelCount | number | 已发现模型数量 |
| createdAt | string (ISO8601) | 创建时间 |

#### DiscoveredModelDto

| Field | Type | Description |
|-------|------|-------------|
| id | string | 模型 ID |

#### LlmConfigDto (MODIFIED)

| Field | Type | Description |
|-------|------|-------------|
| providerId | string? (Guid) | 关联的 Provider ID（新增，nullable） |
| providerName | string? | Provider 名称（只读，由查询时填充） |
| modelId | string | 模型 ID |
| instructions | string? | System prompt |
| toolRefs | string[] (Guid) | Tool 引用 |

---

### Commands

| Command | Input | Output | Validator Rules |
|---------|-------|--------|-----------------|
| RegisterProviderCommand | name, baseUrl, apiKey | Result\<LlmProviderDto\> | name: required, max 200; baseUrl: required, max 500, valid URL (http/https); apiKey: required |
| UpdateProviderCommand | id, name, baseUrl, apiKey? | Result\<LlmProviderDto\> | same as register; apiKey optional (不更新时保持原值) |
| DeleteProviderCommand | id | Result | 检查是否有 Agent 引用，有则拒绝 |
| DiscoverModelsCommand | id | Result\<LlmProviderDto\> | id: required, must exist |

### Queries

| Query | Input | Output |
|-------|-------|--------|
| GetProvidersQuery | (none) | Result\<List\<LlmProviderSummaryDto\>\> |
| GetProviderByIdQuery | id | Result\<LlmProviderDto\> |
| GetProviderModelsQuery | id | Result\<List\<DiscoveredModelDto\>\> |

---

### IModelDiscoveryService Interface

```
IModelDiscoveryService (Application/Common/Interfaces/)
  + DiscoverModelsAsync(string baseUrl, string apiKey, CancellationToken ct) → List<string>
```

**行为**:
- 调用 `GET {baseUrl}/models`，Header: `Authorization: Bearer {apiKey}`
- 解析 OpenAI 标准响应 `{ "data": [{ "id": "model-id" }] }`
- 返回 `data[].id` 列表
- 网络超时/连接失败 → 抛出描述性异常
- 非标准响应格式 → 抛出解析错误异常
- HTTP 401 → 抛出认证失败异常

---

## Infrastructure Layer

### EF Core: LlmProviderConfiguration

| Column | Type | Constraint |
|--------|------|------------|
| id | uuid | PK |
| name | varchar(200) | NOT NULL, UNIQUE index |
| base_url | varchar(500) | NOT NULL |
| api_key | varchar(500) | NOT NULL |
| discovered_models | jsonb | NOT NULL, default '[]' |
| models_refreshed_at | timestamp with time zone | NULL |
| created_at | timestamp with time zone | NOT NULL |
| updated_at | timestamp with time zone | NULL |

**Table**: `llm_providers` (snake_case)

**JSONB mapping**: `DiscoveredModels` 使用 EF Core 原生 JSON 列映射（`builder.Property(e => e.DiscoveredModels).HasColumnType("jsonb")`）

---

### ModelDiscoveryService Implementation

- 注入 `IHttpClientFactory`，创建命名 HttpClient "ModelDiscovery"
- 设置 `Authorization: Bearer {apiKey}` 头
- GET `{baseUrl}/models`
- 反序列化 `{ "data": [{ "id": "..." }] }`
- 提取并返回 `data[].id` 列表
- 异常处理：`HttpRequestException`（连接失败）、`JsonException`（解析失败）、HTTP 非 200 状态码

---

## Frontend Layer

### TypeScript Types

#### LlmProvider

| Field | Type | Description |
|-------|------|-------------|
| id | string | Provider ID |
| name | string | Provider 名称 |
| baseUrl | string | Base URL |
| maskedApiKey | string | 掩码 API Key |
| discoveredModels | string[] | 模型 ID 列表 |
| modelsRefreshedAt | string \| null | 最后刷新时间 |
| createdAt | string | 创建时间 |
| updatedAt | string \| null | 更新时间 |

#### LlmProviderSummary

| Field | Type | Description |
|-------|------|-------------|
| id | string | Provider ID |
| name | string | Provider 名称 |
| baseUrl | string | Base URL |
| modelCount | number | 模型数量 |
| createdAt | string | 创建时间 |

#### CreateProviderRequest

| Field | Type | Description |
|-------|------|-------------|
| name | string | Provider 名称 |
| baseUrl | string | Base URL |
| apiKey | string | API Key（明文传入，仅创建/更新时） |

#### UpdateProviderRequest

| Field | Type | Description |
|-------|------|-------------|
| name | string | Provider 名称 |
| baseUrl | string | Base URL |
| apiKey | string \| undefined | API Key（可选，不传则不更新） |

#### LlmConfig (MODIFIED)

| Field | Type | Description |
|-------|------|-------------|
| providerId | string \| null | Provider ID（新增） |
| providerName | string \| null | Provider 名称（只读） |
| modelId | string | 模型 ID |
| instructions | string \| undefined | System prompt |
| toolRefs | string[] | Tool 引用 |
