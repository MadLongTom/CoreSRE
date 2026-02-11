# API Contract: Workflow Definition CRUD

**Feature**: 011-workflow-crud  
**Date**: 2026-02-11  
**Base Path**: `/api/workflows`

## Endpoints Summary

| Method | Path | Description | Request | Response |
|--------|------|-------------|---------|----------|
| POST | `/api/workflows` | 创建工作流定义 | `CreateWorkflowRequest` | `201` + `Result<WorkflowDefinitionDto>` |
| GET | `/api/workflows` | 查询工作流列表 | `?status=Draft` | `200` + `Result<List<WorkflowSummaryDto>>` |
| GET | `/api/workflows/{id}` | 查询工作流详情 | - | `200` + `Result<WorkflowDefinitionDto>` |
| PUT | `/api/workflows/{id}` | 更新工作流定义 | `UpdateWorkflowRequest` | `200` + `Result<WorkflowDefinitionDto>` |
| DELETE | `/api/workflows/{id}` | 删除工作流定义 | - | `204` No Content |

---

## POST /api/workflows

### Request Body

```json
{
  "name": "AIOps 端到端工作流",
  "description": "告警接收 → 根因分析 → 自动修复的完整 AIOps 流程",
  "graph": {
    "nodes": [
      {
        "nodeId": "alert-receiver",
        "nodeType": "Agent",
        "referenceId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "displayName": "告警接收 Agent",
        "config": null
      },
      {
        "nodeId": "rca-analyzer",
        "nodeType": "Agent",
        "referenceId": "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
        "displayName": "根因分析 Agent",
        "config": null
      },
      {
        "nodeId": "auto-fix",
        "nodeType": "Tool",
        "referenceId": "a1b2c3d4-e5f6-0000-1111-222233334444",
        "displayName": "自动修复工具",
        "config": null
      }
    ],
    "edges": [
      {
        "edgeId": "e1",
        "sourceNodeId": "alert-receiver",
        "targetNodeId": "rca-analyzer",
        "edgeType": "Normal",
        "condition": null
      },
      {
        "edgeId": "e2",
        "sourceNodeId": "rca-analyzer",
        "targetNodeId": "auto-fix",
        "edgeType": "Normal",
        "condition": null
      }
    ]
  }
}
```

### Success Response — 201 Created

```json
{
  "success": true,
  "data": {
    "id": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
    "name": "AIOps 端到端工作流",
    "description": "告警接收 → 根因分析 → 自动修复的完整 AIOps 流程",
    "status": "Draft",
    "graph": {
      "nodes": [
        {
          "nodeId": "alert-receiver",
          "nodeType": "Agent",
          "referenceId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
          "displayName": "告警接收 Agent",
          "config": null
        },
        {
          "nodeId": "rca-analyzer",
          "nodeType": "Agent",
          "referenceId": "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
          "displayName": "根因分析 Agent",
          "config": null
        },
        {
          "nodeId": "auto-fix",
          "nodeType": "Tool",
          "referenceId": "a1b2c3d4-e5f6-0000-1111-222233334444",
          "displayName": "自动修复工具",
          "config": null
        }
      ],
      "edges": [
        {
          "edgeId": "e1",
          "sourceNodeId": "alert-receiver",
          "targetNodeId": "rca-analyzer",
          "edgeType": "Normal",
          "condition": null
        },
        {
          "edgeId": "e2",
          "sourceNodeId": "rca-analyzer",
          "targetNodeId": "auto-fix",
          "edgeType": "Normal",
          "condition": null
        }
      ]
    },
    "createdAt": "2026-02-11T10:30:00Z",
    "updatedAt": null
  },
  "message": null,
  "errors": null,
  "errorCode": null
}
```

### Error Responses

**400 Bad Request** — Validation errors:
```json
{
  "success": false,
  "data": null,
  "message": "Validation failed.",
  "errors": [
    "'Name' must not be empty.",
    "'Graph.Nodes' must not be empty."
  ],
  "errorCode": null
}
```

**400 Bad Request** — DAG cycle detected:
```json
{
  "success": false,
  "data": null,
  "message": "工作流图包含环路，必须为有向无环图。涉及节点: alert-receiver, rca-analyzer",
  "errors": null,
  "errorCode": null
}
```

**400 Bad Request** — Invalid reference:
```json
{
  "success": false,
  "data": null,
  "message": "节点引用的 Agent 不存在: referenceId=3fa85f64-5717-4562-b3fc-2c963f66afa6 (节点: alert-receiver)",
  "errors": null,
  "errorCode": null
}
```

**409 Conflict** — Name already exists:
```json
{
  "success": false,
  "data": null,
  "message": "工作流名称已存在: AIOps 端到端工作流",
  "errors": null,
  "errorCode": 409
}
```

---

## GET /api/workflows

### Query Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `status` | string | No | Filter by status: `Draft`, `Published`. Omit for all. |

### Success Response — 200 OK

```json
{
  "success": true,
  "data": [
    {
      "id": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
      "name": "AIOps 端到端工作流",
      "description": "告警接收 → 根因分析 → 自动修复",
      "status": "Draft",
      "nodeCount": 3,
      "createdAt": "2026-02-11T10:30:00Z",
      "updatedAt": null
    },
    {
      "id": "a1b2c3d4-e5f6-7890-abcd-ef0123456789",
      "name": "日志分析流水线",
      "description": "日志采集 → 聚合 → 异常检测",
      "status": "Published",
      "nodeCount": 5,
      "createdAt": "2026-02-10T08:00:00Z",
      "updatedAt": "2026-02-10T14:00:00Z"
    }
  ],
  "message": null,
  "errors": null,
  "errorCode": null
}
```

---

## GET /api/workflows/{id}

### Path Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | Guid | Yes | Workflow definition ID |

### Success Response — 200 OK

Same structure as POST 201 response `data` field.

### Error Response — 404 Not Found

```json
{
  "success": false,
  "data": null,
  "message": "Resource not found.",
  "errors": null,
  "errorCode": 404
}
```

---

## PUT /api/workflows/{id}

### Path Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | Guid | Yes | Workflow definition ID |

### Request Body

Same structure as POST request body (name, description, graph).

### Success Response — 200 OK

Same structure as POST 201 response.

### Error Responses

**400 Bad Request** — Published status guard:
```json
{
  "success": false,
  "data": null,
  "message": "已发布的工作流不可编辑，请先取消发布。",
  "errors": null,
  "errorCode": null
}
```

**404 Not Found** — Workflow not found (same as GET 404).

**409 Conflict** — Name already exists (same as POST 409).

---

## DELETE /api/workflows/{id}

### Path Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | Guid | Yes | Workflow definition ID |

### Success Response — 204 No Content

No body.

### Error Responses

**400 Bad Request** — Published status guard:
```json
{
  "success": false,
  "data": null,
  "message": "已发布的工作流不可删除，请先取消发布。",
  "errors": null,
  "errorCode": null
}
```

**400 Bad Request** — Referenced by agent:
```json
{
  "success": false,
  "data": null,
  "message": "该工作流已被 Agent 引用，无法删除。",
  "errors": null,
  "errorCode": null
}
```

**404 Not Found** — Workflow not found (same as GET 404).

---

## MediatR Command/Query Contracts

### Commands

```csharp
// CreateWorkflowCommand : IRequest<Result<WorkflowDefinitionDto>>
{
    Name: string,
    Description: string?,
    Graph: WorkflowGraphDto
}

// UpdateWorkflowCommand : IRequest<Result<WorkflowDefinitionDto>>
{
    Id: Guid,                // Bound from route parameter
    Name: string,
    Description: string?,
    Graph: WorkflowGraphDto
}

// DeleteWorkflowCommand : IRequest<Result<bool>>
{
    Id: Guid
}
```

### Queries

```csharp
// GetWorkflowsQuery : IRequest<Result<List<WorkflowSummaryDto>>>
{
    Status: WorkflowStatus?   // Optional filter
}

// GetWorkflowByIdQuery : IRequest<Result<WorkflowDefinitionDto>>
{
    Id: Guid
}
```

### Validation Rules (FluentValidation)

**CreateWorkflowCommandValidator**:
- `Name`: NotEmpty, MaximumLength(200)
- `Graph`: NotNull
- `Graph.Nodes`: NotEmpty (at least 1 node)
- Each `Graph.Nodes[].NodeId`: NotEmpty
- Each `Graph.Nodes[].DisplayName`: NotEmpty
- Each `Graph.Nodes[].NodeType`: IsInEnum
- Each `Graph.Edges[].EdgeId`: NotEmpty
- Each `Graph.Edges[].SourceNodeId`: NotEmpty
- Each `Graph.Edges[].TargetNodeId`: NotEmpty
- Each `Graph.Edges[].EdgeType`: IsInEnum
- Conditional edge: when EdgeType == Conditional, Condition NotEmpty

**UpdateWorkflowCommandValidator**: Same as Create + `Id` must not be `Guid.Empty`.

**DeleteWorkflowCommandValidator**: `Id` must not be `Guid.Empty`.
