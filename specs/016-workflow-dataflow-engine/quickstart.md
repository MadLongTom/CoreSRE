# Quickstart: 工作流数据流模型与执行栈引擎

**Feature**: 016-workflow-dataflow-engine
**Date**: 2026-02-13

## Prerequisites

- Feature 015 (SPEC-080 基础修复) must be complete and merged
- .NET 10 SDK installed
- PostgreSQL database running (via Aspire AppHost)

## What Changed

### Before (old engine)
```
Node A → string lastOutput → Node B → string lastOutput → Node C
         (single string, linear chain, pre-sorted execution order)
```

### After (new engine)
```
Node A → NodeOutputData{main[0]=[items]} → edges → NodeInputData{main[0]=[items]} → Node B
         (structured items, port-aware routing, stack-based execution)
```

## Key Concepts

### Items
Every piece of data flowing between nodes is a **WorkflowItemVO** containing:
- `Json`: the data payload (a `JsonElement`)
- `Source`: optional lineage tracking (which node/port/item produced this)

### Ports
Nodes have numbered input and output ports:
- `InputCount`: how many input ports (default 1)
- `OutputCount`: how many output ports (default 1)
- Edges connect a specific output port to a specific input port

### Execution Stack
Instead of pre-computing the execution order (topological sort), the engine uses a stack:
1. Push the start node onto the stack
2. Pop a node, execute it
3. Propagate its output to downstream nodes via edges
4. Push downstream nodes onto the stack (or add to waiting queue if multi-input)
5. Repeat until stack is empty

### Waiting Queue
When a node has multiple input ports (`InputCount > 1`), it waits in a queue until data arrives on all ports. Then it's promoted to the execution stack.

## Running Tests

```bash
# Run all workflow tests (including existing backward-compat tests)
cd Backend
dotnet test CoreSRE.Infrastructure.Tests --filter "Workflows"

# Run only the new data flow tests
dotnet test CoreSRE.Infrastructure.Tests --filter "DataFlow"

# Run only the new execution stack tests
dotnet test CoreSRE.Infrastructure.Tests --filter "ExecutionStack"
```

## Creating a Multi-Port Workflow (API Example)

### Condition Node with 2 Output Ports
```json
POST /api/workflows
{
  "name": "Conditional Routing",
  "description": "Routes based on severity",
  "graph": {
    "nodes": [
      { "nodeId": "start", "nodeType": "Agent", "referenceId": "...", "displayName": "Analyzer" },
      { "nodeId": "condition", "nodeType": "Condition", "displayName": "Check Severity", "inputCount": 1, "outputCount": 2 },
      { "nodeId": "high", "nodeType": "Agent", "referenceId": "...", "displayName": "High Priority Handler" },
      { "nodeId": "low", "nodeType": "Agent", "referenceId": "...", "displayName": "Low Priority Handler" }
    ],
    "edges": [
      { "edgeId": "e1", "sourceNodeId": "start", "targetNodeId": "condition", "edgeType": "Normal" },
      { "edgeId": "e2", "sourceNodeId": "condition", "targetNodeId": "high", "edgeType": "Conditional", "condition": "severity == 'high'", "sourcePortIndex": 0 },
      { "edgeId": "e3", "sourceNodeId": "condition", "targetNodeId": "low", "edgeType": "Conditional", "condition": "severity == 'low'", "sourcePortIndex": 1 }
    ]
  }
}
```

### Diamond Workflow with Multi-Input Merge
```json
POST /api/workflows
{
  "name": "Diamond Merge",
  "description": "Fan out to two branches, merge results",
  "graph": {
    "nodes": [
      { "nodeId": "start", "nodeType": "Agent", "referenceId": "...", "displayName": "Start", "outputCount": 2 },
      { "nodeId": "branch-a", "nodeType": "Agent", "referenceId": "...", "displayName": "Branch A" },
      { "nodeId": "branch-b", "nodeType": "Agent", "referenceId": "...", "displayName": "Branch B" },
      { "nodeId": "merge", "nodeType": "FanIn", "displayName": "Merge", "inputCount": 2 }
    ],
    "edges": [
      { "edgeId": "e1", "sourceNodeId": "start", "targetNodeId": "branch-a", "edgeType": "Normal", "sourcePortIndex": 0 },
      { "edgeId": "e2", "sourceNodeId": "start", "targetNodeId": "branch-b", "edgeType": "Normal", "sourcePortIndex": 1 },
      { "edgeId": "e3", "sourceNodeId": "branch-a", "targetNodeId": "merge", "edgeType": "Normal", "targetPortIndex": 0 },
      { "edgeId": "e4", "sourceNodeId": "branch-b", "targetNodeId": "merge", "edgeType": "Normal", "targetPortIndex": 1 }
    ]
  }
}
```

## Backward Compatibility

**Existing workflows work without any changes:**
- Nodes without `inputCount`/`outputCount` default to 1
- Edges without `sourcePortIndex`/`targetPortIndex` default to 0
- The engine wraps old-style string data into structured items transparently
- All 79+ existing tests pass without modification

## Implementation Phases (from tasks.md)

| Phase | Focus | Tasks | Status |
|-------|-------|-------|--------|
| 1 | Setup | T001-T002 | ✅ Complete |
| 2 | Foundational (Port Fields, Validation, DTOs) | T003-T011 | ✅ Complete |
| 3 | US2 Structured Data Flow (VOs) | T012-T019 | ✅ Complete (20 tests) |
| 4 | US5 Execution Stack Engine (Core Rewrite) | T020-T032 | ✅ Complete (21 tests) |
| 5 | US1 Backward Compatibility | T033-T037 | ✅ Complete (5 tests) |
| 6 | US3 Multi-Port Output Routing | T038-T041 | ✅ Complete (6 tests) |
| 7 | US4 Multi-Input Waiting Queue | T042-T045 | ✅ Complete (4 tests) |
| 8 | US6 Data Lineage Tracking | T046-T048 | ✅ Complete (3 tests) |
| 9 | Polish & Final Validation | T049-T051 | ✅ Complete |

**Total**: 51 tasks, 144 workflow tests + 87 application tests = 231 passing tests
