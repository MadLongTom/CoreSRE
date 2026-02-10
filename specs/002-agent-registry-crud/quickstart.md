# Quick Start: Agent 注册与 CRUD 管理

**Feature**: 002-agent-registry-crud

## Prerequisites

1. .NET 10 SDK installed
2. Docker Desktop running (Aspire orchestrates PostgreSQL container)
3. Solution builds cleanly: `dotnet build Backend/CoreSRE/CoreSRE.slnx`

## Step 1: Start the platform

```bash
dotnet run --project Backend/CoreSRE.AppHost
```

Wait for the Aspire Dashboard to show `postgres` and `api` resources as **Running**.

## Step 2: Register an A2A Agent

```bash
curl -X POST http://localhost:5156/api/agents \
  -H "Content-Type: application/json" \
  -d '{
    "name": "customer-support-agent",
    "description": "Customer support via A2A protocol",
    "agentType": "A2A",
    "endpoint": "https://agents.example.com/customer-support",
    "agentCard": {
      "skills": [
        { "name": "answer-questions", "description": "Answer product questions" }
      ],
      "interfaces": [
        { "protocol": "HTTP+SSE", "path": "/a2a" }
      ],
      "securitySchemes": [
        { "type": "bearer" }
      ]
    }
  }'
```

**Expected**: HTTP 201 with `{ "success": true, "data": { "id": "...", "status": "Registered", ... } }`

## Step 3: Register a ChatClient Agent

```bash
curl -X POST http://localhost:5156/api/agents \
  -H "Content-Type: application/json" \
  -d '{
    "name": "code-review-agent",
    "agentType": "ChatClient",
    "llmConfig": {
      "modelId": "gpt-4o",
      "instructions": "You are a code review expert.",
      "toolRefs": []
    }
  }'
```

**Expected**: HTTP 201

## Step 4: Register a Workflow Agent

```bash
curl -X POST http://localhost:5156/api/agents \
  -H "Content-Type: application/json" \
  -d '{
    "name": "incident-triage-agent",
    "agentType": "Workflow",
    "workflowRef": "550e8400-e29b-41d4-a716-446655440000"
  }'
```

**Expected**: HTTP 201

## Step 5: List all Agents

```bash
curl http://localhost:5156/api/agents
```

**Expected**: HTTP 200 with 3 agents in list

## Step 6: Filter by type

```bash
curl "http://localhost:5156/api/agents?type=A2A"
```

**Expected**: HTTP 200 with only A2A agents

## Step 7: Get Agent details

```bash
curl http://localhost:5156/api/agents/{id}
```

Replace `{id}` with the ID from Step 2. **Expected**: HTTP 200 with full agent details including `agentCard`.

## Step 8: Update an Agent

```bash
curl -X PUT http://localhost:5156/api/agents/{id} \
  -H "Content-Type: application/json" \
  -d '{
    "name": "customer-support-agent-v2",
    "description": "Updated customer support agent",
    "endpoint": "https://agents.example.com/customer-support/v2",
    "agentCard": {
      "skills": [
        { "name": "answer-questions", "description": "Answer product questions" },
        { "name": "escalate", "description": "Escalate to human agent" }
      ],
      "interfaces": [
        { "protocol": "HTTP+SSE", "path": "/a2a" }
      ],
      "securitySchemes": [
        { "type": "bearer" }
      ]
    }
  }'
```

**Expected**: HTTP 200 with updated data and new `updatedAt` timestamp.

## Step 9: Delete an Agent

```bash
curl -X DELETE http://localhost:5156/api/agents/{id}
```

**Expected**: HTTP 204 No Content

## Verify deletion

```bash
curl http://localhost:5156/api/agents/{id}
```

**Expected**: HTTP 404

## Validation Examples

### Missing required field (A2A without endpoint)

```bash
curl -X POST http://localhost:5156/api/agents \
  -H "Content-Type: application/json" \
  -d '{ "name": "bad-agent", "agentType": "A2A" }'
```

**Expected**: HTTP 400 with structured error messages

### Duplicate name

```bash
curl -X POST http://localhost:5156/api/agents \
  -H "Content-Type: application/json" \
  -d '{ "name": "customer-support-agent-v2", "agentType": "ChatClient", "llmConfig": { "modelId": "gpt-4o" } }'
```

**Expected**: HTTP 409 Conflict
