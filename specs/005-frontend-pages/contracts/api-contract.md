# API Contract: Agent Management

> 前端消费的后端 REST API 契约。
> 基于 SPEC-001 (Agent CRUD) 和 SPEC-003 (Agent Search) 的已实现端点。
> 后端路由定义：`Backend/CoreSRE/Endpoints/AgentEndpoints.cs`

## Base URL

- **Development**: Vite proxy `/api` → `http://localhost:5156`（在 `vite.config.ts` 中配置）
- **Production**: 同域名下 `/api` 路径

## Endpoints

### 1. List Agents

```
GET /api/agents?type={agentType}
```

| Parameter | Location | Required | Type | Description |
|-----------|----------|----------|------|-------------|
| `type` | query | No | string | Filter by AgentType (A2A, ChatClient, Workflow) |

**Response**: `200 OK`
```json
{
  "success": true,
  "data": [
    {
      "id": "guid-string",
      "name": "Agent Name",
      "agentType": "A2A",
      "status": "Active",
      "createdAt": "2025-01-01T00:00:00Z"
    }
  ]
}
```

**Type**: `ApiResult<AgentSummary[]>`

---

### 2. Get Agent by ID

```
GET /api/agents/{id}
```

| Parameter | Location | Required | Type | Description |
|-----------|----------|----------|------|-------------|
| `id` | path | Yes | string (GUID) | Agent identifier |

**Response 200**: `ApiResult<AgentRegistration>`
```json
{
  "success": true,
  "data": {
    "id": "guid-string",
    "name": "Agent Name",
    "description": "optional",
    "agentType": "A2A",
    "status": "Registered",
    "endpoint": "https://example.com/agent",
    "agentCard": {
      "skills": [{ "name": "Skill", "description": "desc" }],
      "interfaces": [{ "protocol": "HTTP", "path": "/api" }],
      "securitySchemes": [{ "type": "Bearer", "parameters": null }]
    },
    "llmConfig": null,
    "workflowRef": null,
    "createdAt": "2025-01-01T00:00:00Z",
    "updatedAt": "2025-01-02T00:00:00Z"
  }
}
```

**Response 404**: `ApiResult<AgentRegistration>` with `success: false`, `errorCode: 404`

---

### 3. Register Agent

```
POST /api/agents
Content-Type: application/json
```

**Request Body**: `CreateAgentRequest`
```json
{
  "name": "New Agent",
  "description": "optional description",
  "agentType": "A2A",
  "endpoint": "https://example.com/agent",
  "agentCard": {
    "skills": [{ "name": "Skill", "description": "desc" }],
    "interfaces": [{ "protocol": "HTTP", "path": "/api" }],
    "securitySchemes": []
  }
}
```

**Response 201**: `ApiResult<AgentRegistration>`（同 Get Agent 响应结构）

**Response 400**: Validation error
```json
{
  "success": false,
  "message": "Validation failed",
  "errors": ["Name must not be empty", "'Agent Type' is not valid."]
}
```

**Response 409**: Conflict (duplicate name)
```json
{
  "success": false,
  "message": "Resource already exists.",
  "errorCode": 409
}
```

---

### 4. Update Agent

```
PUT /api/agents/{id}
Content-Type: application/json
```

| Parameter | Location | Required | Type | Description |
|-----------|----------|----------|------|-------------|
| `id` | path | Yes | string (GUID) | Agent identifier |

**Request Body**: `UpdateAgentRequest`（注意：无 `agentType` 字段，类型不可变）
```json
{
  "name": "Updated Name",
  "description": "updated description",
  "endpoint": "https://example.com/agent-v2",
  "agentCard": { "skills": [], "interfaces": [], "securitySchemes": [] }
}
```

**Response 200**: `ApiResult<AgentRegistration>`

**Response 404**: Not found

**Response 400**: Validation error

---

### 5. Delete Agent

```
DELETE /api/agents/{id}
```

| Parameter | Location | Required | Type | Description |
|-----------|----------|----------|------|-------------|
| `id` | path | Yes | string (GUID) | Agent identifier |

**Response 204**: No Content（成功删除，无响应体）

**Response 404**: Not found（返回 `ApiResult` with `success: false`）

---

### 6. Search Agents

```
GET /api/agents/search?q={query}
```

| Parameter | Location | Required | Type | Description |
|-----------|----------|----------|------|-------------|
| `q` | query | Yes | string | Search keyword |

**Response 200**: `AgentSearchResponse`（注意：此端点直接返回 `AgentSearchResponse`，非包装在 `ApiResult` 中 — 后端 `result.Data` 已解包）
```json
{
  "results": [
    {
      "id": "guid-string",
      "name": "Agent Name",
      "agentType": "A2A",
      "status": "Active",
      "createdAt": "2025-01-01T00:00:00Z",
      "matchedSkills": [
        { "name": "Matching Skill", "description": "matches query" }
      ],
      "similarityScore": 0.85
    }
  ],
  "searchMode": "keyword",
  "query": "search term",
  "totalCount": 1
}
```

**Response 400**: Validation error（query 为空时）

## Error Handling Contract

前端 API 客户端统一错误处理：

| HTTP Status | 处理方式 | UI 表现 |
|-------------|---------|---------|
| 200/201 | 解析 JSON → `ApiResult<T>` 检查 `success` | 正常渲染数据 |
| 204 | 无响应体，操作成功 | 跳转或刷新列表 |
| 400 | 解析 `errors[]` 字段 | 表单字段级 / 全局错误消息 |
| 404 | 解析 `message` 字段 | "Agent 未找到" 提示或 404 页面 |
| 409 | 解析 `message` 字段 | "名称已存在" 提示 |
| 500 | 通用错误 | "服务器错误，请稍后重试" |
| Network Error | `fetch` reject | "网络连接失败" |

## Content-Type

- Request: `application/json`
- Response: `application/json`（DELETE 204 除外）

## CORS

- 开发环境通过 Vite proxy 绕过 CORS
- 生产环境同域部署，无 CORS 问题
