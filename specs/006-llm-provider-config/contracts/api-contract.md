# API Contract: LLM Provider 配置与模型发现

**Feature**: 006-llm-provider-config | **Date**: 2026-02-10  
**Base Path**: `/api/providers`

---

## 1. Register Provider

**POST** `/api/providers`

### Request

```json
{
  "name": "Ollama Local",
  "baseUrl": "http://localhost:11434/v1",
  "apiKey": "ollama"
}
```

| Field | Type | Required | Validation |
|-------|------|----------|------------|
| name | string | ✅ | 1–200 字符，不可为空白 |
| baseUrl | string | ✅ | 1–500 字符，必须以 http:// 或 https:// 开头 |
| apiKey | string | ✅ | 1–500 字符，不可为空 |

### Response

**201 Created**

```json
{
  "success": true,
  "data": {
    "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "name": "Ollama Local",
    "baseUrl": "http://localhost:11434/v1",
    "maskedApiKey": "****ama",
    "discoveredModels": [],
    "modelsRefreshedAt": null,
    "createdAt": "2026-02-10T08:00:00Z",
    "updatedAt": null
  },
  "message": null,
  "errors": null,
  "errorCode": null
}
```

### Error Responses

| Status | ErrorCode | Condition |
|--------|-----------|-----------|
| 400 | VALIDATION_ERROR | 输入验证失败 |
| 409 | DUPLICATE | name 已存在 |

---

## 2. List Providers

**GET** `/api/providers`

### Request

无参数。

### Response

**200 OK**

```json
{
  "success": true,
  "data": [
    {
      "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "name": "Ollama Local",
      "baseUrl": "http://localhost:11434/v1",
      "modelCount": 5,
      "createdAt": "2026-02-10T08:00:00Z"
    }
  ],
  "message": null,
  "errors": null,
  "errorCode": null
}
```

---

## 3. Get Provider Detail

**GET** `/api/providers/{id}`

### Parameters

| Param | Type | Location | Description |
|-------|------|----------|-------------|
| id | Guid | path | Provider ID |

### Response

**200 OK**

```json
{
  "success": true,
  "data": {
    "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "name": "Ollama Local",
    "baseUrl": "http://localhost:11434/v1",
    "maskedApiKey": "****ama",
    "discoveredModels": ["llama3.2:latest", "codellama:7b", "mistral:latest"],
    "modelsRefreshedAt": "2026-02-10T08:30:00Z",
    "createdAt": "2026-02-10T08:00:00Z",
    "updatedAt": null
  },
  "message": null,
  "errors": null,
  "errorCode": null
}
```

### Error Responses

| Status | ErrorCode | Condition |
|--------|-----------|-----------|
| 404 | NOT_FOUND | Provider 不存在 |

---

## 4. Update Provider

**PUT** `/api/providers/{id}`

### Parameters

| Param | Type | Location | Description |
|-------|------|----------|-------------|
| id | Guid | path | Provider ID |

### Request

```json
{
  "name": "Ollama Local (Updated)",
  "baseUrl": "http://localhost:11434/v1",
  "apiKey": "new-key"
}
```

| Field | Type | Required | Validation |
|-------|------|----------|------------|
| name | string | ✅ | 1–200 字符 |
| baseUrl | string | ✅ | 1–500 字符，http/https |
| apiKey | string | ❌ | 可选；省略或 null 时不更新原 Key |

### Response

**200 OK** — 同 Register 的 data 结构。

### Error Responses

| Status | ErrorCode | Condition |
|--------|-----------|-----------|
| 400 | VALIDATION_ERROR | 输入验证失败 |
| 404 | NOT_FOUND | Provider 不存在 |
| 409 | DUPLICATE | name 已被其他 Provider 使用 |

---

## 5. Delete Provider

**DELETE** `/api/providers/{id}`

### Parameters

| Param | Type | Location | Description |
|-------|------|----------|-------------|
| id | Guid | path | Provider ID |

### Response

**200 OK**

```json
{
  "success": true,
  "data": null,
  "message": "Provider deleted successfully.",
  "errors": null,
  "errorCode": null
}
```

### Error Responses

| Status | ErrorCode | Condition |
|--------|-----------|-----------|
| 404 | NOT_FOUND | Provider 不存在 |
| 409 | CONFLICT | 有 Agent 正在引用此 Provider（LlmConfig.ProviderId） |

**409 Response Body**:

```json
{
  "success": false,
  "data": null,
  "message": "Cannot delete provider: referenced by 3 agent(s).",
  "errors": null,
  "errorCode": "CONFLICT"
}
```

---

## 6. Discover Models

**POST** `/api/providers/{id}/discover`

### Parameters

| Param | Type | Location | Description |
|-------|------|----------|-------------|
| id | Guid | path | Provider ID |

### Request

无 body。

### Behavior

1. 查找 Provider（404 if not found）
2. 调用 `IModelDiscoveryService.DiscoverModelsAsync(baseUrl, apiKey)`
3. 更新 Provider 的 DiscoveredModels 和 ModelsRefreshedAt
4. 持久化并返回更新后的 Provider

### Response

**200 OK** — 同 Get Provider Detail 的 data 结构（包含更新后的 discoveredModels）。

### Error Responses

| Status | ErrorCode | Condition |
|--------|-----------|-----------|
| 404 | NOT_FOUND | Provider 不存在 |
| 502 | UPSTREAM_ERROR | 远程 API 不可达、超时、认证失败、响应格式异常 |

**502 Response Body**:

```json
{
  "success": false,
  "data": null,
  "message": "Failed to discover models: Connection refused (http://localhost:11434/v1/models)",
  "errors": null,
  "errorCode": "UPSTREAM_ERROR"
}
```

---

## 7. Get Provider Models

**GET** `/api/providers/{id}/models`

### Parameters

| Param | Type | Location | Description |
|-------|------|----------|-------------|
| id | Guid | path | Provider ID |

### Response

**200 OK**

```json
{
  "success": true,
  "data": [
    { "id": "llama3.2:latest" },
    { "id": "codellama:7b" },
    { "id": "mistral:latest" }
  ],
  "message": null,
  "errors": null,
  "errorCode": null
}
```

### Error Responses

| Status | ErrorCode | Condition |
|--------|-----------|-----------|
| 404 | NOT_FOUND | Provider 不存在 |

---

## Cross-Cutting Concerns

### Response Envelope

所有响应使用统一 `Result<T>` 封装：

```json
{
  "success": boolean,
  "data": T | null,
  "message": string | null,
  "errors": string[] | null,
  "errorCode": string | null
}
```

### API Key 安全

- **请求**: 创建/更新时以明文传入
- **响应**: 始终返回 `maskedApiKey`（如 `****xxxx`，仅显示最后 4 位）
- **存储**: 数据库明文存储（v1 决策，后续版本考虑加密）

### Naming Convention

- URL: kebab-case（`/api/providers`）
- JSON fields: camelCase
- DB columns: snake_case
