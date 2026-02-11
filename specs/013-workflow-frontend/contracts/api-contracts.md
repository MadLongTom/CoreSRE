# Workflow Frontend API Contracts

**Date**: 2026-02-11  
**Base URL**: `/api/workflows` (proxied via Vite dev server to `http://localhost:5156`)

## Workflow Definition CRUD

### GET /api/workflows

List all workflow definitions. Optional status filter.

**Query Parameters**:
| Param | Type | Required | Description |
|-------|------|----------|-------------|
| status | `string` | No | Filter by status: `Draft`, `Published` |

**Response** `200 OK`:
```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "name": "Incident Triage Workflow",
    "description": "Triages incoming incidents by severity",
    "status": "Published",
    "nodeCount": 5,
    "createdAt": "2026-02-10T08:00:00Z",
    "updatedAt": "2026-02-11T10:00:00Z"
  }
]
```

---

### GET /api/workflows/{id}

Get workflow definition with full graph.

**Path Parameters**: `id` (Guid)

**Response** `200 OK`:
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Incident Triage Workflow",
  "description": "Triages incoming incidents by severity",
  "status": "Draft",
  "nodeCount": 3,
  "createdAt": "2026-02-10T08:00:00Z",
  "updatedAt": null,
  "graph": {
    "nodes": [
      {
        "nodeId": "node-1",
        "nodeType": "Agent",
        "referenceId": "a1b2c3d4-...",
        "displayName": "Triage Agent",
        "config": "{\"position\":{\"x\":100,\"y\":50},\"prompt\":\"Classify severity\"}"
      },
      {
        "nodeId": "node-2",
        "nodeType": "Condition",
        "referenceId": null,
        "displayName": "Severity Check",
        "config": "{\"position\":{\"x\":100,\"y\":200}}"
      }
    ],
    "edges": [
      {
        "edgeId": "edge-1",
        "sourceNodeId": "node-1",
        "targetNodeId": "node-2",
        "edgeType": "Normal",
        "condition": null
      }
    ]
  }
}
```

**Error** `404 Not Found`: Workflow not found.

---

### POST /api/workflows

Create a new workflow definition.

**Request Body**:
```json
{
  "name": "My Workflow",
  "description": "Optional description",
  "graph": {
    "nodes": [
      {
        "nodeId": "node-1",
        "nodeType": "Agent",
        "referenceId": "a1b2c3d4-...",
        "displayName": "Agent Node",
        "config": "{\"position\":{\"x\":0,\"y\":0}}"
      },
      {
        "nodeId": "node-2",
        "nodeType": "Tool",
        "referenceId": "e5f6g7h8-...",
        "displayName": "Tool Node",
        "config": "{\"position\":{\"x\":0,\"y\":150}}"
      }
    ],
    "edges": [
      {
        "edgeId": "edge-1",
        "sourceNodeId": "node-1",
        "targetNodeId": "node-2",
        "edgeType": "Normal",
        "condition": null
      }
    ]
  }
}
```

**Response** `201 Created`: Full `WorkflowDefinitionDto` (same as GET /{id}).

**Error** `400 Bad Request`: Validation errors (name empty, DAG cycle, orphan nodes, duplicate edges, etc.)
```json
{
  "status": 400,
  "errors": ["DAG 包含环路", "节点 'node-3' 为孤立节点"]
}
```

**Error** `409 Conflict`: Name already exists.

---

### PUT /api/workflows/{id}

Update a workflow definition. Only Draft status allowed.

**Path Parameters**: `id` (Guid)

**Request Body**: Same shape as POST.

**Response** `200 OK`: Updated `WorkflowDefinitionDto`.

**Error** `400 Bad Request`: Validation errors or Published status.  
**Error** `404 Not Found`: Workflow not found.

---

### DELETE /api/workflows/{id}

Delete a workflow definition. Only Draft status allowed.

**Path Parameters**: `id` (Guid)

**Response** `204 No Content`.

**Error** `404 Not Found`: Workflow not found.  
**Error** `409 Conflict`: Published status or has execution references.

---

## Workflow Execution

### POST /api/workflows/{id}/execute

Trigger workflow execution. Only Published status allowed.

**Path Parameters**: `id` (Guid)

**Request Body**:
```json
{
  "input": { "key": "value" }
}
```

**Response** `202 Accepted`:
```json
{
  "id": "7fa85f64-5717-4562-b3fc-2c963f66afa6",
  "workflowDefinitionId": "3fa85f64-...",
  "status": "Pending",
  "input": { "key": "value" },
  "output": null,
  "errorMessage": null,
  "startedAt": null,
  "completedAt": null,
  "traceId": null,
  "graphSnapshot": { "nodes": [...], "edges": [...] },
  "nodeExecutions": [
    {
      "nodeId": "node-1",
      "status": "Pending",
      "input": null,
      "output": null,
      "errorMessage": null,
      "startedAt": null,
      "completedAt": null
    }
  ],
  "createdAt": "2026-02-11T12:00:00Z"
}
```

**Error** `400 Bad Request`: Draft status or missing agent/tool references.  
**Error** `404 Not Found`: Workflow not found.

---

### GET /api/workflows/{id}/executions

List execution records for a workflow. Optional status filter.

**Path Parameters**: `id` (Guid)  
**Query Parameters**: `status` (optional: `Pending`, `Running`, `Completed`, `Failed`, `Canceled`)

**Response** `200 OK`:
```json
[
  {
    "id": "7fa85f64-...",
    "status": "Completed",
    "startedAt": "2026-02-11T12:00:01Z",
    "completedAt": "2026-02-11T12:00:15Z",
    "createdAt": "2026-02-11T12:00:00Z"
  }
]
```

**Error** `404 Not Found`: Workflow not found.

---

### GET /api/workflows/{id}/executions/{execId}

Get execution detail with node-level data and graph snapshot.

**Path Parameters**: `id` (Guid), `execId` (Guid)

**Response** `200 OK`: Full `WorkflowExecutionDto` (same as POST execute response but with updated statuses).

**Error** `404 Not Found`: Workflow or execution not found.

---

## Dependent APIs (for node configuration dropdowns)

### GET /api/agents
Returns agent list for Agent node `referenceId` selection.

### GET /api/tools
Returns tool list for Tool node `referenceId` selection.
