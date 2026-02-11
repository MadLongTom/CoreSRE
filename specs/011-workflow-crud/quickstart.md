# Quickstart: 工作流定义 CRUD

**Feature**: 011-workflow-crud  
**Date**: 2026-02-11  

## Prerequisites

- CoreSRE backend running (`dotnet run` or `dev.ps1`)
- PostgreSQL database accessible (via Aspire AppHost)
- At least 1 Agent and 1 Tool registered in the system

## Verification Steps

### 1. Create a Workflow Definition

```http
POST /api/workflows
Content-Type: application/json

{
  "name": "Test Sequential Workflow",
  "description": "A simple 2-agent sequential workflow for testing",
  "graph": {
    "nodes": [
      {
        "nodeId": "step1",
        "nodeType": "Agent",
        "referenceId": "<EXISTING_AGENT_ID>",
        "displayName": "Step 1 Agent",
        "config": null
      },
      {
        "nodeId": "step2",
        "nodeType": "Agent",
        "referenceId": "<EXISTING_AGENT_ID>",
        "displayName": "Step 2 Agent",
        "config": null
      }
    ],
    "edges": [
      {
        "edgeId": "e1",
        "sourceNodeId": "step1",
        "targetNodeId": "step2",
        "edgeType": "Normal",
        "condition": null
      }
    ]
  }
}
```

**Expected**: `201 Created` with complete workflow definition including system-generated ID and `"status": "Draft"`.

### 2. List All Workflows

```http
GET /api/workflows
```

**Expected**: `200 OK` with array containing the created workflow summary (nodeCount: 2).

### 3. Get Workflow Details

```http
GET /api/workflows/{id}
```

**Expected**: `200 OK` with full workflow definition including all nodes and edges.

### 4. Update Workflow (Add a Node)

```http
PUT /api/workflows/{id}
Content-Type: application/json

{
  "name": "Test Sequential Workflow - Updated",
  "description": "Now with 3 agents",
  "graph": {
    "nodes": [
      { "nodeId": "step1", "nodeType": "Agent", "referenceId": "<EXISTING_AGENT_ID>", "displayName": "Step 1", "config": null },
      { "nodeId": "step2", "nodeType": "Agent", "referenceId": "<EXISTING_AGENT_ID>", "displayName": "Step 2", "config": null },
      { "nodeId": "step3", "nodeType": "Agent", "referenceId": "<EXISTING_AGENT_ID>", "displayName": "Step 3", "config": null }
    ],
    "edges": [
      { "edgeId": "e1", "sourceNodeId": "step1", "targetNodeId": "step2", "edgeType": "Normal", "condition": null },
      { "edgeId": "e2", "sourceNodeId": "step2", "targetNodeId": "step3", "edgeType": "Normal", "condition": null }
    ]
  }
}
```

**Expected**: `200 OK` with updated workflow (3 nodes, 2 edges, updated name).

### 5. Validate DAG — Cycle Detection

```http
POST /api/workflows
Content-Type: application/json

{
  "name": "Cycle Test",
  "graph": {
    "nodes": [
      { "nodeId": "a", "nodeType": "Condition", "displayName": "A", "config": null },
      { "nodeId": "b", "nodeType": "Condition", "displayName": "B", "config": null },
      { "nodeId": "c", "nodeType": "Condition", "displayName": "C", "config": null }
    ],
    "edges": [
      { "edgeId": "e1", "sourceNodeId": "a", "targetNodeId": "b", "edgeType": "Normal" },
      { "edgeId": "e2", "sourceNodeId": "b", "targetNodeId": "c", "edgeType": "Normal" },
      { "edgeId": "e3", "sourceNodeId": "c", "targetNodeId": "a", "edgeType": "Normal" }
    ]
  }
}
```

**Expected**: `400 Bad Request` with message about cycle detection.

### 6. Delete Workflow

```http
DELETE /api/workflows/{id}
```

**Expected**: `204 No Content`. Subsequent GET returns 404.

## Validation Checklist

- [ ] POST creates workflow with Draft status
- [ ] GET list returns all workflows with nodeCount
- [ ] GET by ID returns full graph details
- [ ] PUT updates name, description, and graph
- [ ] PUT rejects update when status is Published
- [ ] DELETE returns 204 and removes workflow
- [ ] DELETE rejects Published workflows
- [ ] DELETE rejects workflows referenced by AgentRegistration
- [ ] POST/PUT rejects DAGs with cycles
- [ ] POST/PUT rejects DAGs with orphan nodes
- [ ] POST/PUT rejects duplicate node IDs
- [ ] POST/PUT rejects duplicate workflow names (409)
- [ ] POST/PUT validates Agent/Tool reference IDs exist
