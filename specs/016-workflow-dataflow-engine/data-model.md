# Data Model: 工作流数据流模型与执行栈引擎

**Feature**: 016-workflow-dataflow-engine
**Date**: 2026-02-13

## Entity Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                     Domain Layer (ValueObjects)                     │
│                                                                     │
│  WorkflowItemVO ◄──── ItemSourceVO                                  │
│       │                  (nodeId, portIndex, itemIndex)              │
│       │                                                             │
│  PortDataVO                                                         │
│       │ contains List<WorkflowItemVO>                               │
│       │                                                             │
│  NodeInputData ─────── Dict<string, List<PortDataVO?>>              │
│  NodeOutputData ────── Dict<string, List<PortDataVO?>>              │
│                                                                     │
│  WorkflowNodeVO (MODIFIED: +InputCount, +OutputCount)               │
│  WorkflowEdgeVO (MODIFIED: +SourcePortIndex, +TargetPortIndex)      │
│  NodeExecutionVO (UNCHANGED: Input/Output remain string?)           │
│  WorkflowGraphVO (MODIFIED: +Validate port index rules)             │
│  WorkflowExecution (UNCHANGED: entity methods keep string? params)  │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│              Infrastructure Layer (Services — internal)              │
│                                                                     │
│  ExecutionContext                                                    │
│       ├── ExecutionStack: LinkedList<NodeExecutionTask>              │
│       ├── WaitingNodes: Dict<string, WaitingNodeData>               │
│       ├── RunData: Dict<string, List<NodeRunResult>>                │
│       ├── ConnectionsBySource: Dict<string, List<WorkflowEdgeVO>>   │
│       └── ConnectionsByTarget: Dict<string, List<WorkflowEdgeVO>>   │
│                                                                     │
│  NodeExecutionTask                                                  │
│       ├── Node: WorkflowNodeVO                                      │
│       ├── InputData: NodeInputData                                  │
│       └── RunIndex: int                                             │
│                                                                     │
│  WaitingNodeData                                                    │
│       ├── TotalInputPorts: int                                      │
│       ├── ReceivedPorts: Dict<int, PortDataVO?>                     │
│       └── AllPortsReceived: bool (computed)                         │
│                                                                     │
│  NodeRunResult                                                      │
│       ├── OutputData: NodeOutputData                                │
│       ├── StartedAt / CompletedAt: DateTime                         │
│       ├── Status: NodeExecutionStatus                               │
│       └── ErrorMessage: string?                                     │
└─────────────────────────────────────────────────────────────────────┘
```

## Domain Value Objects (NEW)

### WorkflowItemVO
**Location**: `CoreSRE.Domain/ValueObjects/WorkflowItemVO.cs`
**Purpose**: Fundamental data unit flowing through the workflow. Each item represents one logical data record.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Json` | `JsonElement` | required | The data payload |
| `Source` | `ItemSourceVO?` | `null` | Lineage: which node produced this item |

**Invariants**:
- `Json` is always a valid `JsonElement` (never undefined/null value kind in normal flow)
- `Source` is null for the initial workflow input items

### ItemSourceVO
**Location**: `CoreSRE.Domain/ValueObjects/ItemSourceVO.cs`
**Purpose**: Tracks data lineage — where an item came from.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `NodeId` | `string` | `""` | ID of the node that produced this item |
| `OutputIndex` | `int` | `0` | Output port index on the producing node |
| `ItemIndex` | `int` | `0` | Position of this item in the port's output list |

### PortDataVO
**Location**: `CoreSRE.Domain/ValueObjects/PortDataVO.cs`
**Purpose**: Data on a single port — a list of items.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Items` | `List<WorkflowItemVO>` | `[]` | Ordered list of data items on this port |

**Invariants**:
- A port with no data is represented as `null` (not an empty `PortDataVO`), distinguishing "no data received" from "received empty batch"

### NodeInputData
**Location**: `CoreSRE.Domain/ValueObjects/NodeInputData.cs`
**Purpose**: Complete input to a node execution, organized by connection type and port index.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Ports` | `Dictionary<string, List<PortDataVO?>>` | `new()` | Port data by connection type. Key is `"main"`. |

**Methods**:
- `GetMainInput(int portIndex = 0) → PortDataVO?`: Convenience accessor for `Ports["main"][portIndex]`
- `GetFirstItem() → WorkflowItemVO?`: Returns the first item on the first main port (common case)
- `ToJsonString() → string`: Serializes to JSON for recording in `NodeExecutionVO.Input`

**Factory**:
- `static FromSingleString(string? input) → NodeInputData`: Wraps a raw string (from old-style execution) into a single item on port 0. Used for backward compatibility.

### NodeOutputData
**Location**: `CoreSRE.Domain/ValueObjects/NodeOutputData.cs`
**Purpose**: Complete output of a node execution, organized by connection type and port index.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Ports` | `Dictionary<string, List<PortDataVO?>>` | `new()` | Port data by connection type. Key is `"main"`. |

**Methods**:
- `GetMainOutput(int portIndex = 0) → PortDataVO?`: Convenience accessor
- `ToJsonString() → string`: Serializes for recording in `NodeExecutionVO.Output`

**Factory**:
- `static FromSingleString(string? output, string nodeId) → NodeOutputData`: Wraps a raw agent/tool output string into a single item on port 0 with source tracking. Used by `ExecuteAgentNodeAsync` and `ExecuteToolNodeAsync`.

## Domain Value Objects (MODIFIED)

### WorkflowNodeVO
**Location**: `CoreSRE.Domain/ValueObjects/WorkflowNodeVO.cs`

| Property | Type | Default | Change |
|----------|------|---------|--------|
| `NodeId` | `string` | `""` | no change |
| `NodeType` | `WorkflowNodeType` | — | no change |
| `ReferenceId` | `Guid?` | `null` | no change |
| `DisplayName` | `string` | `""` | no change |
| `Config` | `string?` | `null` | no change |
| **`InputCount`** | **`int`** | **`1`** | **NEW** — number of input ports |
| **`OutputCount`** | **`int`** | **`1`** | **NEW** — number of output ports |

**Backward Compatibility**: Existing JSONB data missing these fields → C# `init` default = `1`. All legacy nodes behave as single-input, single-output.

### WorkflowEdgeVO
**Location**: `CoreSRE.Domain/ValueObjects/WorkflowEdgeVO.cs`

| Property | Type | Default | Change |
|----------|------|---------|--------|
| `EdgeId` | `string` | `""` | no change |
| `SourceNodeId` | `string` | `""` | no change |
| `TargetNodeId` | `string` | `""` | no change |
| `EdgeType` | `WorkflowEdgeType` | — | no change |
| `Condition` | `string?` | `null` | no change |
| **`SourcePortIndex`** | **`int`** | **`0`** | **NEW** — output port on source node |
| **`TargetPortIndex`** | **`int`** | **`0`** | **NEW** — input port on target node |

**Backward Compatibility**: Existing JSONB data missing these fields → C# `init` default = `0`. All legacy edges connect port 0 → port 0.

### WorkflowGraphVO — New Validation Rules
**Location**: `CoreSRE.Domain/ValueObjects/WorkflowGraphVO.cs`

Added to `Validate()`:
| # | Rule | Severity |
|---|------|----------|
| 10 | Edge `SourcePortIndex >= sourceNode.OutputCount` | Error |
| 11 | Edge `TargetPortIndex >= targetNode.InputCount` | Error |
| 12 | Condition node with `OutputCount < 2` | Error |

## Infrastructure Types (Internal to WorkflowEngine)

### ExecutionContext
**Location**: `CoreSRE.Infrastructure/Services/ExecutionContext.cs` (new file, internal class)

| Property | Type | Description |
|----------|------|-------------|
| `ExecutionStack` | `LinkedList<NodeExecutionTask>` | LIFO stack of ready-to-execute nodes |
| `WaitingNodes` | `Dictionary<string, WaitingNodeData>` | Multi-input nodes awaiting data |
| `RunData` | `Dictionary<string, List<NodeRunResult>>` | Completed node results by node ID |
| `Graph` | `WorkflowGraphVO` | Reference to the execution graph |
| `ConnectionsBySource` | `Dictionary<string, List<WorkflowEdgeVO>>` | Pre-computed: outgoing edges per node |
| `ConnectionsByTarget` | `Dictionary<string, List<WorkflowEdgeVO>>` | Pre-computed: incoming edges per node |

### NodeExecutionTask
| Property | Type | Description |
|----------|------|-------------|
| `Node` | `WorkflowNodeVO` | The node to execute |
| `InputData` | `NodeInputData` | Resolved input for this execution |
| `RunIndex` | `int` | Execution iteration index (for loop detection) |

### WaitingNodeData
| Property | Type | Description |
|----------|------|-------------|
| `TotalInputPorts` | `int` | How many input ports the node has |
| `ReceivedPorts` | `Dictionary<int, PortDataVO?>` | Data received per port index |
| `AllPortsReceived` | `bool` | Computed: `ReceivedPorts.Count >= TotalInputPorts && all values non-null` |

### NodeRunResult
| Property | Type | Description |
|----------|------|-------------|
| `OutputData` | `NodeOutputData` | Structured output from this run |
| `StartedAt` | `DateTime` | When execution started |
| `CompletedAt` | `DateTime` | When execution completed |
| `Status` | `NodeExecutionStatus` | Completed/Failed |
| `ErrorMessage` | `string?` | Error detail if failed |

## Application DTOs (MODIFIED)

### WorkflowNodeDto
| Property | Type | Change |
|----------|------|--------|
| `NodeId` | `string` | no change |
| `NodeType` | `string` | no change |
| `ReferenceId` | `Guid?` | no change |
| `DisplayName` | `string` | no change |
| `Config` | `string?` | no change |
| **`InputCount`** | **`int`** | **NEW** |
| **`OutputCount`** | **`int`** | **NEW** |

### WorkflowEdgeDto
| Property | Type | Change |
|----------|------|--------|
| `EdgeId` | `string` | no change |
| `SourceNodeId` | `string` | no change |
| `TargetNodeId` | `string` | no change |
| `EdgeType` | `string` | no change |
| `Condition` | `string?` | no change |
| **`SourcePortIndex`** | **`int`** | **NEW** |
| **`TargetPortIndex`** | **`int`** | **NEW** |

### WorkflowMappingProfile
No special mapping needed — AutoMapper auto-maps same-name properties. The new `int` fields have matching names and types in both VO and DTO.

## Data Flow Architecture

```
Workflow Input (JsonElement)
        │
        ▼
   ┌─────────────────────────────────────────────┐
   │ NodeInputData.FromSingleString(input.json)  │
   │   → main[0] = [WorkflowItemVO{Json=input}]  │
   └───────────────┬─────────────────────────────┘
                   │ push to ExecutionStack
                   ▼
   ┌─────────────────────────────────────────────┐
   │        ExecutionStack (LIFO)                 │
   │  ┌─────────────────────────────────────────┐│
   │  │ NodeExecutionTask {                     ││
   │  │   Node: startNode                       ││
   │  │   InputData: main[0]=[{Json:input}]     ││
   │  │ }                                       ││
   │  └─────────────────────────────────────────┘│
   └───────────────┬─────────────────────────────┘
                   │ pop & execute
                   ▼
   ┌─────────────────────────────────────────────┐
   │ ExecuteNodeAsync(node, inputData)           │
   │   → Agent: builds prompt from items         │
   │   → Tool: extracts params from items        │
   │   → Condition: evaluates, outputs on port   │
   │   → FanOut: copies items to all ports       │
   │   → FanIn: merges items from all ports      │
   │   returns NodeOutputData                    │
   └───────────────┬─────────────────────────────┘
                   │
                   ▼
   ┌─────────────────────────────────────────────┐
   │ PropagateData(context, node, outputData)    │
   │   for each outgoing edge:                   │
   │     portData = outputData.main[edge.SrcPort]│
   │     target = graph.Nodes[edge.TargetNodeId] │
   │     if target.InputCount == 1:              │
   │       push to ExecutionStack                │
   │     else:                                   │
   │       add to WaitingNodes[target]           │
   │       if all ports received:                │
   │         promote to ExecutionStack           │
   └─────────────────────────────────────────────┘
```

## Serialization Format

### NodeExecutionVO.Input (string?) — New Format
```json
{
  "main": [
    [
      { "json": { "message": "Hello" }, "source": { "nodeId": "agent-1", "outputIndex": 0, "itemIndex": 0 } }
    ]
  ]
}
```

### NodeExecutionVO.Output (string?) — New Format
```json
{
  "main": [
    [
      { "json": { "response": "World" }, "source": null }
    ]
  ]
}
```

### Backward Compatibility
Old format (plain string): `"Hello world"` or `"{\"key\":\"value\"}"`
New format: JSON object with `"main"` key containing port arrays.

The engine always writes new format. Old execution records with plain strings remain readable — they just aren't in the structured format. New API consumers can detect the format by checking if the string parses as a JSON object with a `"main"` key.
