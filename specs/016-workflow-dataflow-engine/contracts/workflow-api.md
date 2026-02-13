# API Contracts: Workflow Dataflow Engine

**Feature**: 016-workflow-dataflow-engine
**Date**: 2026-02-13

## Endpoint Changes

This feature does **not** add or remove any API endpoints. The existing 8 workflow endpoints continue to work. The changes are to the **shape of request/response payloads** due to new fields on nodes and edges.

## Modified Request/Response Schemas

### WorkflowNodeDto (used in Create/Update Workflow requests)

```json
{
  "nodeId": "agent-1",
  "nodeType": "Agent",
  "referenceId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "displayName": "Analyst Agent",
  "config": "{\"systemPrompt\":\"You are an analyst.\"}",
  "inputCount": 1,
  "outputCount": 1
}
```

| Field | Type | Required | Default | Change |
|-------|------|----------|---------|--------|
| `nodeId` | string | yes | — | no change |
| `nodeType` | string | yes | — | no change |
| `referenceId` | uuid? | no | null | no change |
| `displayName` | string | yes | — | no change |
| `config` | string? | no | null | no change |
| **`inputCount`** | **int** | **no** | **1** | **NEW** |
| **`outputCount`** | **int** | **no** | **1** | **NEW** |

**Backward Compatibility**: Requests omitting `inputCount`/`outputCount` default to 1 (standard JSON deserialization of `int` with `init` default).

### WorkflowEdgeDto (used in Create/Update Workflow requests)

```json
{
  "edgeId": "e1",
  "sourceNodeId": "agent-1",
  "targetNodeId": "agent-2",
  "edgeType": "Normal",
  "condition": null,
  "sourcePortIndex": 0,
  "targetPortIndex": 0
}
```

| Field | Type | Required | Default | Change |
|-------|------|----------|---------|--------|
| `edgeId` | string | yes | — | no change |
| `sourceNodeId` | string | yes | — | no change |
| `targetNodeId` | string | yes | — | no change |
| `edgeType` | string | yes | — | no change |
| `condition` | string? | no | null | no change |
| **`sourcePortIndex`** | **int** | **no** | **0** | **NEW** |
| **`targetPortIndex`** | **int** | **no** | **0** | **NEW** |

**Backward Compatibility**: Requests omitting port indices default to 0.

### NodeExecutionDto (read-only, in execution query responses)

```json
{
  "nodeId": "agent-1",
  "status": "Completed",
  "input": "{\"main\":[[{\"json\":{\"message\":\"Hello\"},\"source\":{\"nodeId\":\"start\",\"outputIndex\":0,\"itemIndex\":0}}]]}",
  "output": "{\"main\":[[{\"json\":{\"response\":\"World\"}}]]}",
  "errorMessage": null,
  "startedAt": "2026-02-13T10:00:00Z",
  "completedAt": "2026-02-13T10:00:05Z"
}
```

| Field | Type | Change |
|-------|------|--------|
| `nodeId` | string | no change |
| `status` | string | no change |
| `input` | string? | **CONTENT CHANGE**: now contains serialized `NodeInputData` JSON |
| `output` | string? | **CONTENT CHANGE**: now contains serialized `NodeOutputData` JSON |
| `errorMessage` | string? | no change |
| `startedAt` | datetime? | no change |
| `completedAt` | datetime? | no change |

**Note**: The `input` and `output` fields remain `string?` type. The content format changes from a raw string/JSON to a structured JSON object with `"main"` key. Old execution records retain their old format; new executions use the new format.

## Structured Data Format (inside input/output strings)

### NodeInputData JSON Schema
```json
{
  "main": [
    [
      {
        "json": {},
        "source": {
          "nodeId": "string",
          "outputIndex": 0,
          "itemIndex": 0
        }
      }
    ]
  ]
}
```

- `main`: connection type (only "main" in this phase)
- First array: port list, indexed by port number
- Second array: items on that port
- Each item: `json` (payload) + optional `source` (lineage)

### NodeOutputData JSON Schema
Same structure as `NodeInputData`. Port indices correspond to the node's output ports.

## Validation Error Responses (new)

### Port Index Out of Range
When a workflow definition includes an edge with `sourcePortIndex >= sourceNode.outputCount`:

```
HTTP 400 Bad Request
{
  "errors": [
    "边 'e1' 的源端口索引 2 超出节点 'agent-1' 的输出端口数 1"
  ]
}
```

### Condition Node Invalid OutputCount
When a Condition node has `outputCount < 2`:

```
HTTP 400 Bad Request
{
  "errors": [
    "条件节点 'cond-1' 的输出端口数必须 >= 2，当前为 1"
  ]
}
```

## No Breaking Changes Summary

| Aspect | Change | Impact |
|--------|--------|--------|
| Endpoint URLs | None | Zero impact |
| Request bodies | New optional fields with defaults | Zero impact — old clients work unchanged |
| Response bodies (definition) | New fields in node/edge objects | Additive — old clients ignore unknown fields |
| Response bodies (execution) | `input`/`output` content format change | **Low risk** — field type unchanged (`string?`), content structure is richer JSON |
| HTTP status codes | No change | Zero impact |
| Authentication | No change | Zero impact |
