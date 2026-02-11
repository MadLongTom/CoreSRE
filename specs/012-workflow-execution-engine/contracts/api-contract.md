# API Contract: 工作流执行引擎

**Feature**: 012-workflow-execution-engine  
**Date**: 2026-02-11  
**Base Path**: `/api/workflows`

---

## POST /api/workflows/{id}/execute

**Description**: 启动工作流执行。创建 WorkflowExecution 记录后立即返回，执行在后台异步进行。

**Path Parameters**:
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| id | uuid | Yes | 工作流定义 ID |

**Request Body**: `application/json`
```json
{
  "input": {
    "query": "检查服务状态",
    "context": { "service": "api-gateway" }
  }
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| input | object | No | 执行输入数据（JSON 对象）。省略或为空时默认 `{}` |

**Responses**:

### 202 Accepted
```json
{
  "success": true,
  "data": {
    "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "workflowDefinitionId": "f0e1d2c3-b4a5-6789-0abc-def123456789",
    "status": "Pending",
    "input": { "query": "检查服务状态" },
    "output": null,
    "errorMessage": null,
    "startedAt": null,
    "completedAt": null,
    "traceId": null,
    "nodeExecutions": [
      { "nodeId": "agent-a", "status": "Pending", "input": null, "output": null, "errorMessage": null, "startedAt": null, "completedAt": null },
      { "nodeId": "agent-b", "status": "Pending", "input": null, "output": null, "errorMessage": null, "startedAt": null, "completedAt": null },
      { "nodeId": "agent-c", "status": "Pending", "input": null, "output": null, "errorMessage": null, "startedAt": null, "completedAt": null }
    ],
    "createdAt": "2026-02-11T06:00:00Z"
  }
}
```

### 400 Bad Request — Draft 状态
```json
{
  "success": false,
  "message": "仅已发布的工作流可执行。当前状态: Draft",
  "errorCode": 400
}
```

### 400 Bad Request — 引用校验失败
```json
{
  "success": false,
  "message": "工作流引用的外部资源不存在",
  "errors": [
    "节点引用的 Agent 不存在: referenceId=abc123 (节点: classify-agent)"
  ],
  "errorCode": 400
}
```

### 404 Not Found
```json
{
  "success": false,
  "message": "Resource not found.",
  "errorCode": 404
}
```

---

## GET /api/workflows/{id}/executions

**Description**: 查询指定工作流的所有执行记录列表（摘要信息）。

**Path Parameters**:
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| id | uuid | Yes | 工作流定义 ID |

**Query Parameters**:
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| status | string | No | 按状态过滤（Pending/Running/Completed/Failed/Canceled） |

**Responses**:

### 200 OK
```json
{
  "success": true,
  "data": [
    {
      "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "status": "Completed",
      "startedAt": "2026-02-11T06:00:01Z",
      "completedAt": "2026-02-11T06:00:15Z",
      "createdAt": "2026-02-11T06:00:00Z"
    },
    {
      "id": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
      "status": "Running",
      "startedAt": "2026-02-11T06:01:00Z",
      "completedAt": null,
      "createdAt": "2026-02-11T06:00:59Z"
    }
  ]
}
```

### 404 Not Found — 工作流不存在
```json
{
  "success": false,
  "message": "Resource not found.",
  "errorCode": 404
}
```

---

## GET /api/workflows/{id}/executions/{execId}

**Description**: 查询指定执行记录的完整详情，包含所有节点执行信息。

**Path Parameters**:
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| id | uuid | Yes | 工作流定义 ID |
| execId | uuid | Yes | 执行记录 ID |

**Responses**:

### 200 OK
```json
{
  "success": true,
  "data": {
    "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "workflowDefinitionId": "f0e1d2c3-b4a5-6789-0abc-def123456789",
    "status": "Completed",
    "input": { "query": "检查服务状态" },
    "output": { "result": "所有服务运行正常", "details": ["api-gateway: healthy", "db: healthy"] },
    "errorMessage": null,
    "startedAt": "2026-02-11T06:00:01Z",
    "completedAt": "2026-02-11T06:00:15Z",
    "traceId": null,
    "nodeExecutions": [
      {
        "nodeId": "classify-agent",
        "status": "Completed",
        "input": "{\"query\": \"检查服务状态\"}",
        "output": "{\"severity\": \"low\", \"category\": \"health-check\"}",
        "errorMessage": null,
        "startedAt": "2026-02-11T06:00:01Z",
        "completedAt": "2026-02-11T06:00:05Z"
      },
      {
        "nodeId": "check-agent",
        "status": "Completed",
        "input": "{\"severity\": \"low\", \"category\": \"health-check\"}",
        "output": "{\"result\": \"所有服务运行正常\"}",
        "errorMessage": null,
        "startedAt": "2026-02-11T06:00:05Z",
        "completedAt": "2026-02-11T06:00:12Z"
      },
      {
        "nodeId": "skip-branch",
        "status": "Skipped",
        "input": null,
        "output": null,
        "errorMessage": null,
        "startedAt": null,
        "completedAt": null
      }
    ],
    "createdAt": "2026-02-11T06:00:00Z"
  }
}
```

### 404 Not Found
```json
{
  "success": false,
  "message": "Resource not found.",
  "errorCode": 404
}
```

---

## Error Response Schema

All error responses follow the `Result<T>` pattern:

```json
{
  "success": false,
  "message": "Human-readable error description",
  "errors": ["Optional array of detailed error messages"],
  "errorCode": 400
}
```

| ErrorCode | HTTP Status | Trigger |
|-----------|-------------|---------|
| 400 | Bad Request | Draft 状态执行、引用校验失败、验证错误 |
| 404 | Not Found | 工作流/执行记录不存在 |

---

## HTTP Status Code Mapping

| Endpoint | Success | Error Codes |
|----------|---------|-------------|
| POST /{id}/execute | 202 Accepted | 400, 404 |
| GET /{id}/executions | 200 OK | 404 |
| GET /{id}/executions/{execId} | 200 OK | 404 |
