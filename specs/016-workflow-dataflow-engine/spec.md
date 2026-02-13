# Feature Specification: 工作流数据流模型与执行栈引擎

**Feature Branch**: `016-workflow-dataflow-engine`
**Created**: 2025-07-14
**Status**: Draft
**Priority**: P1 — Phase 2 (depends on SPEC-080 / Feature 015 基础修复)
**Estimated Duration**: 2 weeks
**Fixes Defects**: D1 (线性数据传递), D2 (固定执行顺序), D10 (无多端口支持)

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Backward-Compatible Workflow Execution (Priority: P1)

A workflow author who has already built workflows using the existing node and edge model runs those workflows without any modifications. The system treats all existing nodes as having one input port and one output port, and all existing edges as connecting port index 0 to port index 0. Execution results remain identical to the previous engine behavior.

**Why this priority**: Without backward compatibility, all existing workflows break. This is the foundation that makes every other story safe to deliver.

**Independent Test**: Execute an existing workflow definition (linear chain of Agent nodes) with the new engine and verify the same node execution sequence, same node inputs/outputs, and same final workflow output as the old engine.

**Acceptance Scenarios**:

1. **Given** a workflow defined before this feature (nodes without InputCount/OutputCount, edges without SourcePortIndex/TargetPortIndex), **When** the workflow is executed, **Then** all nodes default to InputCount=1, OutputCount=1 and all edges default to SourcePortIndex=0, TargetPortIndex=0, and the execution produces identical results to the previous engine.
2. **Given** a workflow with FanOut and FanIn nodes defined under the old model, **When** it is executed, **Then** the FanOut node dispatches data to multiple downstream branches and the FanIn node collects all upstream branch outputs into a merged result, matching the previous behavior.
3. **Given** a workflow with a Condition node and conditional edges, **When** it is executed, **Then** the Condition node routes data to the matching branch and skips non-matching branches, producing the same outcome as before.

---

### User Story 2 — Structured Data Flow Between Nodes (Priority: P1)

A workflow author creates a workflow where each node produces structured output data organized as a list of items (each item containing a JSON payload). When data flows between nodes, it is carried as structured items through port connections rather than as a raw string. The system records each node's input and output in this structured format, enabling clear traceability of what data each node received and produced.

**Why this priority**: Structured data flow is the core architectural change that eliminates defect D1 and enables all downstream features (multi-port, waiting queue, expression references). Without it, the engine remains limited to single-string linear passing.

**Independent Test**: Execute a two-node linear workflow (Agent A → Agent B), inspect the execution record, and verify that both nodes have structured input/output data containing items with JSON payloads and source tracking information.

**Acceptance Scenarios**:

1. **Given** a two-node workflow (A → B), **When** Node A completes with output items, **Then** Node B receives those items as structured input on port 0 with source tracking referencing Node A.
2. **Given** a completed workflow execution, **When** the execution record is retrieved, **Then** each node's recorded input and output contains structured data (items organized by port) rather than a raw string.
3. **Given** a node that produces multiple output items, **When** data propagates to the downstream node, **Then** the downstream node receives all items as a batch on the connected port.

---

### User Story 3 — Multi-Port Output Routing (Priority: P1)

A workflow author configures a node with multiple output ports (e.g., a Condition node with OutputCount=2: one port for the "true" branch and one for the "false" branch). Edges from this node specify which output port they connect from (SourcePortIndex). When the node executes, data is placed on the appropriate output port and only flows along edges connected to that port.

**Why this priority**: Multi-port output replaces the ad-hoc FanOut and Condition routing logic with a unified, general-purpose mechanism. This directly fixes defect D10 and makes the routing model extensible.

**Independent Test**: Create a Condition node with OutputCount=2, connect port 0 to a "true branch" node and port 1 to a "false branch" node. Execute with a condition that evaluates to true. Verify only the true-branch node executes.

**Acceptance Scenarios**:

1. **Given** a Condition node with OutputCount=2 and edges connecting port 0 to Node-True and port 1 to Node-False, **When** the condition evaluates to true, **Then** the system places output data on port 0 only, Node-True executes with that data, and Node-False does not execute.
2. **Given** a node with OutputCount=3 and edges from each port to different downstream nodes, **When** the node produces data on ports 0 and 2 but not port 1, **Then** only the nodes connected to ports 0 and 2 execute.
3. **Given** a node with multiple output ports where one port's edge connects to a chain of downstream nodes, **When** that port has no data, **Then** the entire downstream chain is not executed.

---

### User Story 4 — Multi-Input Waiting Queue (Priority: P1)

A workflow author creates a node that receives data from multiple upstream branches (e.g., a merge/aggregation node with InputCount=2). The system waits until data has arrived on all input ports before executing the node. This replaces the explicit FanIn node type with a general-purpose mechanism where any node with multiple input connections automatically waits for all inputs.

**Why this priority**: The waiting queue mechanism is the counterpart to multi-port output — together they replace the rigid FanOut/FanIn node types with a flexible data-driven model. This is essential for correct execution ordering in non-linear workflows.

**Independent Test**: Create a diamond-shaped workflow: Start → (Branch A, Branch B) → Merge. Verify that the Merge node only executes after both Branch A and Branch B have completed, and receives data from both branches on separate input ports.

**Acceptance Scenarios**:

1. **Given** a node with InputCount=2 connected to two upstream nodes via edges targeting port 0 and port 1, **When** only one upstream node has completed, **Then** the target node is placed in the waiting queue and does not execute.
2. **Given** a waiting node that has received data on port 0 only, **When** the second upstream node completes and provides data on port 1, **Then** the node is promoted from the waiting queue to the execution stack and receives combined input from both ports.
3. **Given** a node with InputCount=3, **When** data arrives on ports 0 and 2 but never on port 1, **Then** the node remains in the waiting queue indefinitely (workflow completes without executing this node, which is reported as incomplete).

---

### User Story 5 — Execution Stack Engine (Priority: P1)

The workflow engine uses a stack-based execution model instead of topological sort. When a node completes, its output data is propagated to downstream nodes by consulting the graph's edge connections. Downstream nodes with single inputs are pushed directly onto the execution stack. The engine processes nodes by popping from the stack until it is empty. This model supports dynamic routing, where the execution path is determined at runtime by actual data flow rather than a pre-computed order.

**Why this priority**: The stack-based engine is the runtime mechanism that powers all data flow features. Without it, the structured data model, multi-port routing, and waiting queue cannot function. It directly fixes defect D2.

**Independent Test**: Execute a workflow with a conditional branch (A → Condition → B or C → D). Verify that only the matching branch executes, and the non-matching branch's nodes are never pushed onto the stack.

**Acceptance Scenarios**:

1. **Given** a workflow with a conditional branch, **When** the engine executes, **Then** only the nodes on the taken branch are pushed to the stack and executed; nodes on the skipped branch never appear in the execution record as "Running" or "Completed".
2. **Given** a linear workflow (A → B → C), **When** the engine executes, **Then** nodes execute in order A, B, C with each node's output propagated as the next node's input through the stack mechanism.
3. **Given** a workflow execution in progress, **When** the execution stack becomes empty but the waiting queue contains nodes, **Then** the engine checks the waiting queue and promotes any node whose input ports are all satisfied.

---

### User Story 6 — Data Lineage Tracking (Priority: P2)

Each data item flowing through the workflow carries information about its origin: which node produced it, from which output port, and which item index. This allows users and developers to trace any piece of data back through the workflow to understand how it was derived.

**Why this priority**: Data lineage is valuable for debugging and auditing but is not required for the core execution to function. It enhances the structured data model without blocking other features.

**Independent Test**: Execute a three-node chain (A → B → C). Inspect the items received by Node C and verify that each item carries source information pointing back to Node B (and transitively to Node A).

**Acceptance Scenarios**:

1. **Given** a node that produces output items, **When** those items flow to a downstream node, **Then** each item's source information records the producing node's ID, output port index, and the item's position in the output list.
2. **Given** a multi-step workflow, **When** the execution completes, **Then** the execution record for each node contains input items with source references that allow tracing data backwards through the graph.

---

### Edge Cases

- **Empty workflow input**: When the workflow is triggered with no input, the start node receives an empty items list on port 0, and execution proceeds normally (nodes handle empty input gracefully).
- **Node produces no output**: When a node completes but returns no output items, downstream nodes connected to that output port are not pushed to the stack (no empty propagation).
- **Disconnected nodes**: Nodes with no incoming edges (other than the start node) are never pushed to the execution stack and remain in "Pending" status at workflow completion.
- **Cycle detection**: The graph validation (already implemented in WorkflowGraphVO.Validate) rejects cycles before execution begins; the execution engine additionally guards against infinite loops via a per-node execution count limit.
- **Single branch failure in parallel paths**: When one branch of a multi-output dispatch fails, the other branches that have already been pushed to the stack continue to completion (error handling for the failed branch does not abort unrelated branches). The merge node downstream will not execute if it is waiting for the failed branch's data.
- **Port index out of range**: When an edge references a SourcePortIndex or TargetPortIndex that exceeds the node's declared OutputCount or InputCount, graph validation rejects the workflow definition before execution.

## Requirements *(mandatory)*

### Functional Requirements

#### Data Model Requirements

- **FR-001**: The system MUST represent data flowing between nodes as structured items, where each item contains a JSON payload and optional source tracking information (producing node ID, output port index, item position).
- **FR-002**: Each node MUST declare the number of input ports and output ports it supports. Default values MUST be 1 for both InputCount and OutputCount, ensuring backward compatibility with existing workflows.
- **FR-003**: Each edge MUST specify which output port of the source node and which input port of the target node it connects. Default values MUST be 0 for both SourcePortIndex and TargetPortIndex, ensuring backward compatibility.
- **FR-004**: Node execution input MUST be organized by port — a mapping of port indices to lists of data items received on each port.
- **FR-005**: Node execution output MUST be organized by port — a mapping of port indices to lists of data items produced on each port.
- **FR-006**: The system MUST record each node's structured input and output data in the execution record for traceability.

#### Execution Engine Requirements

- **FR-007**: The engine MUST use a stack-based execution model where nodes are dynamically pushed as their input data becomes available, replacing the pre-computed topological sort order.
- **FR-008**: When a node completes execution, the engine MUST propagate its output data to downstream nodes by consulting the graph's edge connections, placing data on the correct target port based on the edge's SourcePortIndex and TargetPortIndex.
- **FR-009**: For downstream nodes with a single input port (InputCount=1), the engine MUST push them directly onto the execution stack when data arrives.
- **FR-010**: For downstream nodes with multiple input ports (InputCount > 1), the engine MUST place them in a waiting queue until data has arrived on all declared input ports, then promote them to the execution stack.
- **FR-011**: When the execution stack is empty, the engine MUST check the waiting queue for any nodes that have all input ports satisfied and promote them to the stack.
- **FR-012**: The engine MUST protect against infinite execution loops by limiting how many times a single node can be executed within one workflow run.

#### Node Type Behavior Requirements

- **FR-013**: Agent nodes MUST receive structured input items on their input port, use the item data to construct the prompt, invoke the resolved agent, and produce structured output items containing the agent's response.
- **FR-014**: Tool nodes MUST receive structured input items, extract parameters from the item data, invoke the tool, and produce structured output items containing the tool's result.
- **FR-015**: Condition nodes MUST evaluate their condition against the input data and produce output items on the appropriate output port (e.g., port 0 for true, port 1 for false). Condition nodes MUST have OutputCount >= 2.
- **FR-016**: FanOut nodes MUST pass their input data through to all output ports, creating a copy of the input items on each connected output port. This is equivalent to a node with OutputCount matching the number of downstream branches.
- **FR-017**: FanIn nodes MUST have InputCount matching the number of upstream branches and MUST wait for all input ports to receive data before executing. The FanIn node MUST merge input items from all ports into a single output.

#### Backward Compatibility Requirements

- **FR-018**: All new fields on nodes (InputCount, OutputCount) and edges (SourcePortIndex, TargetPortIndex) MUST have default values that preserve existing behavior (InputCount=1, OutputCount=1, SourcePortIndex=0, TargetPortIndex=0).
- **FR-019**: Existing workflow definitions stored in the database MUST execute correctly without any data migration — the defaults applied at deserialization time MUST produce identical execution behavior to the previous engine.
- **FR-020**: The execution record format MUST remain compatible with existing API consumers — structured data MUST be serializable as JSON that degrades gracefully when read by consumers expecting the old string format.

#### Validation Requirements

- **FR-021**: Graph validation MUST verify that edge port indices do not exceed the declared InputCount or OutputCount of their connected nodes.
- **FR-022**: Graph validation MUST verify that Condition nodes have OutputCount >= 2.

### Key Entities

- **WorkflowItemVO**: A single data item flowing through the workflow. Contains a JSON payload (the actual data) and an optional source reference pointing to the originating node, output port index, and item position. This is the fundamental unit of data exchange between nodes, analogous to a row in a data pipeline.

- **ItemSourceVO**: Tracks the lineage of a data item — which node produced it, from which output port (index), and its position (item index) within that port's output list. Enables data tracing and debugging.

- **PortDataVO**: Represents the data on a single port — an ordered list of WorkflowItemVO items. A port with no data is represented as null or an empty list.

- **NodeInputData**: The complete input to a node execution, organized by connection type ("main") and port index. Provides convenience methods to access the items on a specific input port.

- **NodeOutputData**: The complete output of a node execution, organized by connection type and port index. A node with OutputCount=2 would produce data on up to 2 output ports.

- **ExecutionContext**: The runtime state container for workflow execution. Holds the execution stack (pending node tasks), the waiting queue (multi-input nodes awaiting data), and the run data (results of all executed nodes). Also contains pre-computed connection maps (edges indexed by source and target node).

- **NodeExecutionTask**: A unit of work on the execution stack. References the node to execute, the input data for that execution, the run index (supporting nodes that execute multiple times), and source information about what triggered this task.

- **WaitingNodeData**: Tracks the state of a multi-input node waiting in the queue. Records how many input ports are expected, which ports have received data so far, and the data and source information for each received port. When all ports are satisfied, the node is promoted to the execution stack.

- **NodeRunResult**: The outcome of a single node execution. Records the structured output data, timing (started/completed), status (completed/failed), and optional error message. Supports nodes that run multiple times by storing results in a list.

- **WorkflowNodeVO** (modified): Extended with InputCount (number of input ports, default 1) and OutputCount (number of output ports, default 1).

- **WorkflowEdgeVO** (modified): Extended with SourcePortIndex (which output port of the source node, default 0) and TargetPortIndex (which input port of the target node, default 0).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All existing workflow definitions execute with identical results under the new engine without any modification — 100% backward compatibility verified by re-running all existing workflow tests.
- **SC-002**: Each node execution record contains structured input and output data (items organized by port with JSON payloads) instead of raw strings, verified by inspection of execution records after workflow completion.
- **SC-003**: A workflow with a multi-output Condition node (OutputCount=2) correctly routes data to only the matching branch, verified by executing a conditional workflow and confirming only the correct branch's nodes appear as completed.
- **SC-004**: A workflow with a multi-input merge node (InputCount=2) correctly waits for both upstream branches before executing, verified by executing a diamond-shaped workflow and confirming the merge node executes last with data from both branches.
- **SC-005**: The execution engine correctly handles a workflow with at least 10 nodes across 3 branching/merging paths, completing all paths in the correct data-driven order without topological pre-sorting.
- **SC-006**: All new domain value objects and engine logic achieve >= 90% unit test coverage, verified by test run reports.
- **SC-007**: Graph validation rejects workflows with invalid port index references (SourcePortIndex >= OutputCount or TargetPortIndex >= InputCount) before execution begins.

## Assumptions

- The "main" connection type is the only connection type implemented in this phase. Future phases may add specialized types (e.g., "ai_agent", "ai_tool") following the n8n model.
- This feature does not implement real-time execution notifications (SignalR push) — that is a separate defect (D3) for a future phase.
- Expression engine enhancements to reference structured data (e.g., `$node["A"].json.field`) are a separate concern and may be addressed in a companion feature; this spec focuses on the data model and execution engine.
- The per-node execution count limit for infinite loop protection defaults to a reasonable number (e.g., 100 executions per node per workflow run).
