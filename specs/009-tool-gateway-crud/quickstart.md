# Quick Start: Tool Gateway — 工具注册与统一调用

**前置条件**: CoreSRE 后端服务已启动（默认 `http://localhost:5000`）

---

## 1. 注册 REST API 工具

```bash
curl -X POST http://localhost:5000/api/tools \
  -H "Content-Type: application/json" \
  -d '{
    "name": "weather-api",
    "description": "天气查询 API",
    "toolType": "RestApi",
    "connectionConfig": {
      "endpoint": "https://api.weather.com/v1/forecast",
      "transportType": "Rest"
    },
    "authConfig": {
      "authType": "ApiKey",
      "credential": "sk-abc123",
      "apiKeyHeaderName": "X-Api-Key"
    }
  }'
```

**响应** (201 Created):
```json
{
  "id": "a1b2c3d4-...",
  "name": "weather-api",
  "toolType": "RestApi",
  "status": "Active",
  "connectionConfig": {
    "endpoint": "https://api.weather.com/v1/forecast",
    "transportType": "Rest"
  },
  "authConfig": {
    "authType": "ApiKey",
    "hasCredential": true,
    "apiKeyHeaderName": "X-Api-Key"
  },
  "createdAt": "2026-02-10T10:00:00Z"
}
```

---

## 2. 注册 MCP Server 工具源

```bash
curl -X POST http://localhost:5000/api/tools \
  -H "Content-Type: application/json" \
  -d '{
    "name": "code-analysis-mcp",
    "description": "代码分析 MCP Server",
    "toolType": "McpServer",
    "connectionConfig": {
      "endpoint": "https://mcp.example.com/sse",
      "transportType": "StreamableHttp"
    },
    "authConfig": {
      "authType": "Bearer",
      "credential": "eyJhbGciOiJSUzI1NiIs..."
    }
  }'
```

**响应** (201 Created):
```json
{
  "id": "e5f6a7b8-...",
  "name": "code-analysis-mcp",
  "toolType": "McpServer",
  "status": "Inactive",
  "connectionConfig": {
    "endpoint": "https://mcp.example.com/sse",
    "transportType": "StreamableHttp"
  },
  "authConfig": {
    "authType": "Bearer",
    "hasCredential": true
  },
  "mcpToolCount": 0,
  "createdAt": "2026-02-10T10:01:00Z"
}
```

> 注册后系统自动在后台发起 MCP 握手和 `tools/list` 发现。握手成功后 `status` 更新为 `Active`，`mcpToolCount` 更新为发现的工具数。

---

## 3. 查询 MCP 子工具

```bash
curl http://localhost:5000/api/tools/e5f6a7b8-.../mcp-tools
```

**响应** (200 OK):
```json
[
  {
    "id": "11111111-...",
    "toolRegistrationId": "e5f6a7b8-...",
    "toolName": "analyze-code",
    "description": "分析代码质量和风格",
    "inputSchema": {
      "type": "object",
      "properties": {
        "language": { "type": "string" },
        "code": { "type": "string" }
      },
      "required": ["language", "code"]
    },
    "annotations": {
      "readOnly": true,
      "destructive": false,
      "idempotent": true
    }
  }
]
```

---

## 4. 通过 OpenAPI 文档导入工具

```bash
curl -X POST http://localhost:5000/api/tools/import-openapi \
  -F "file=@petstore.yaml" \
  -F "baseUrl=https://petstore.swagger.io/v2" \
  -F "authConfig.authType=ApiKey" \
  -F "authConfig.credential=demo-key" \
  -F "authConfig.apiKeyHeaderName=api_key"
```

**响应** (200 OK):
```json
{
  "totalOperations": 12,
  "importedCount": 10,
  "skippedCount": 2,
  "tools": [
    {
      "id": "...",
      "name": "petstore-getPetById",
      "toolType": "RestApi",
      "status": "Active",
      "importSource": "petstore.yaml"
    }
  ],
  "errors": [
    "Skipped operation at POST /pets - missing operationId"
  ]
}
```

---

## 5. 查询工具列表（带过滤）

```bash
# 查询所有 McpServer 类型且 Active 状态的工具
curl "http://localhost:5000/api/tools?toolType=McpServer&status=Active&page=1&pageSize=10"

# 关键词搜索
curl "http://localhost:5000/api/tools?search=weather"
```

**响应** (200 OK):
```json
{
  "items": [ ... ],
  "totalCount": 5,
  "page": 1,
  "pageSize": 10,
  "totalPages": 1
}
```

---

## 6. 调用 REST API 工具

```bash
curl -X POST http://localhost:5000/api/tools/a1b2c3d4-.../invoke \
  -H "Content-Type: application/json" \
  -d '{
    "parameters": {
      "city": "Beijing",
      "unit": "celsius"
    }
  }'
```

**响应** (200 OK):
```json
{
  "success": true,
  "data": {
    "temperature": 22,
    "condition": "sunny",
    "humidity": 45
  },
  "durationMs": 340,
  "toolRegistrationId": "a1b2c3d4-...",
  "invokedAt": "2026-02-10T10:05:00Z"
}
```

---

## 7. 调用 MCP Server 工具

```bash
curl -X POST http://localhost:5000/api/tools/e5f6a7b8-.../invoke \
  -H "Content-Type: application/json" \
  -d '{
    "mcpToolName": "analyze-code",
    "parameters": {
      "language": "csharp",
      "code": "public class Foo { }"
    }
  }'
```

**响应** (200 OK):
```json
{
  "success": true,
  "data": {
    "score": 85,
    "issues": [
      { "severity": "info", "message": "Consider adding XML documentation" }
    ]
  },
  "durationMs": 1200,
  "toolRegistrationId": "e5f6a7b8-...",
  "invokedAt": "2026-02-10T10:06:00Z"
}
```

---

## 8. 更新工具

```bash
curl -X PUT http://localhost:5000/api/tools/a1b2c3d4-... \
  -H "Content-Type: application/json" \
  -d '{
    "name": "weather-api-v2",
    "description": "天气查询 API v2",
    "connectionConfig": {
      "endpoint": "https://api.weather.com/v2/forecast",
      "transportType": "Rest"
    },
    "authConfig": {
      "authType": "ApiKey",
      "credential": "sk-new-key-456",
      "apiKeyHeaderName": "X-Api-Key"
    }
  }'
```

---

## 9. 删除工具

```bash
curl -X DELETE http://localhost:5000/api/tools/a1b2c3d4-...
# 204 No Content
```

---

## 常见错误

| 状态码 | 场景 | 示例 |
|--------|------|------|
| 400 | 名称为空或超长 | `"errors": { "Name": ["Name is required"] }` |
| 400 | McpServer 使用了 Rest 传输类型 | `"detail": "McpServer requires StreamableHttp or Stdio transport"` |
| 400 | 非 McpServer 调用给了 mcpToolName | `"detail": "mcpToolName is only for McpServer type"` |
| 404 | 工具不存在 | `"detail": "Tool registration not found"` |
| 409 | 名称重复 | `"detail": "Tool with name 'weather-api' already exists"` |
| 502 | 上游调用失败 | `{ "success": false, "error": "Connection refused" }` |
