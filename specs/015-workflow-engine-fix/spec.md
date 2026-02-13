# Feature Specification: 工作流引擎基础修复 (Workflow Engine Base Fix)

**Feature Branch**: `015-workflow-engine-fix`
**Created**: 2026-02-12
**Status**: Draft
**Priority**: P1 (Phase 1 — 1 week)
**Fixes**: Defect D5 (NodeExecutionVO.Input never written), Defect D8 (no mock agent mode)
**Dependency**: SPEC-012 (existing workflow execution engine)
**Input**: User description: "修复现有工作流引擎中「能编译但跑不通」的基础缺陷，使当前引擎在原有架构下达到可用状态。这是后续所有升级的前提——不先跑通现有流程，无法验证后续改进。"

## Overview

The current workflow execution engine compiles but has critical gaps that prevent it from being usable in practice. Specifically:

1. **Node input data is never recorded** — when a workflow executes, each node's `Input` field remains empty, making it impossible to trace what data each node actually received. This destroys observability and debuggability.
2. **Execution details API omits the workflow graph** — when querying a completed execution, the response lacks the DAG snapshot (nodes + edges), so consumers cannot reconstruct what the workflow looked like at execution time.
3. **No way to run workflows without a real LLM** — developers and testers cannot exercise the workflow engine without configuring a live LLM provider, creating a high barrier to development and testing.

This specification addresses these foundational issues to bring the engine to a minimally usable state, which is a prerequisite for all future workflow enhancements.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Node Input Traceability (Priority: P1)

As a platform operator debugging a failed or unexpected workflow execution, I need to see what input data each node received so I can diagnose issues without re-running the workflow.

**Why this priority**: Without input traceability, debugging any workflow issue requires guesswork. This is the single most critical observability gap — every node execution is a black box without it.

**Independent Test**: Can be verified by executing any workflow with 2+ nodes, then inspecting the execution record to confirm each node's Input field contains a valid JSON string representing the data it received.

**Acceptance Scenarios**:

1. **Given** a Published workflow containing 3 sequential Agent nodes, **When** the workflow is executed to completion, **Then** every `NodeExecution` record has a non-null `Input` field containing a JSON string that represents the actual input data passed to that node.
2. **Given** a workflow with a Condition node that branches, **When** the workflow executes and follows one branch, **Then** the Condition node's `Input` field records the data used to evaluate the condition, and each downstream node's `Input` field records the data it received.
3. **Given** a workflow execution where a node fails, **When** the execution is inspected, **Then** the failed node's `Input` field still contains the input data it received before failure.

---

### User Story 2 — Execution Graph Snapshot in API Response (Priority: P1)

As a frontend developer building an execution detail view, I need the execution details API to return the complete workflow graph (nodes and edges) so I can render the DAG visualization showing execution progress on each node.

**Why this priority**: The frontend execution detail page cannot render without the graph structure. This is a blocking gap for the execution visualization feature.

**Independent Test**: Can be verified by executing a workflow, then calling the execution detail endpoint and confirming the response JSON includes the full graph structure with nodes and edges.

**Acceptance Scenarios**:

1. **Given** a completed workflow execution, **When** querying the execution details via the API, **Then** the response includes a `GraphSnapshot` containing a list of nodes (with their IDs, types, names, and configurations) and a list of edges (with source and target node IDs).
2. **Given** a workflow definition that was modified after an execution started, **When** querying the historical execution details, **Then** the `GraphSnapshot` reflects the workflow graph as it was at the time of execution, not the current definition.

---

### User Story 3 — Mock Agent Execution Mode (Priority: P2)

As a developer working on workflow features without access to an LLM provider, I need a mock execution mode where Agent nodes return simulated responses so I can test the full workflow lifecycle end-to-end.

**Why this priority**: While not as critical as the data integrity fixes above, the inability to test workflows without a live LLM severely hampers development velocity. This is the highest-impact developer experience improvement.

**Independent Test**: Can be verified by configuring the system with no LLM providers, creating and executing a workflow with Agent nodes in mock mode, and confirming all nodes complete with simulated responses.

**Acceptance Scenarios**:

1. **Given** the system has no LLM providers configured, **When** a workflow is executed with Agent nodes configured for mock mode, **Then** each Agent node returns a simulated response containing the node name and a summary of the input it received, and the workflow completes successfully.
2. **Given** a workflow with 3 sequential Agent nodes in mock mode, **When** executed, **Then** each subsequent node receives the mock output of the previous node as its input, demonstrating the data flow works correctly even with simulated responses.

---

### User Story 4 — End-to-End Smoke Test (Priority: P2)

As a QA engineer or developer, I need a reliable end-to-end test that validates the complete workflow lifecycle (create → publish → execute → query result) so that regressions in the execution pipeline are caught immediately.

**Why this priority**: This test codifies the "definition of done" for the engine being in a working state. Without it, there is no automated way to verify the engine works after changes.

**Independent Test**: Can be verified by running the test suite and confirming the smoke test passes, exercising the full create → publish → execute → query flow.

**Acceptance Scenarios**:

1. **Given** a clean test environment, **When** a workflow is created, published, executed, and queried in sequence, **Then** all steps succeed, the execution status is `Completed`, node inputs are recorded, and the response includes the graph snapshot.
2. **Given** the smoke test is part of the test suite, **When** the test suite runs, **Then** the end-to-end test completes within 30 seconds (using mock agents to avoid external dependencies).

---

### Edge Cases

- What happens when a node's input data is very large (e.g., > 1 MB of JSON)? The system should still record it, though truncation behavior should be considered for future iterations.
- What happens when a workflow has a single node with no predecessor? The first node's `Input` should contain the workflow-level input data provided at execution time.
- What happens during FanOut execution? Each parallel branch node should record its specific input independently.
- What happens when mock mode is enabled but an LLM provider is also configured? Mock mode should take precedence for agents explicitly configured to use it, while agents with real providers continue to use live LLM.
- What happens if the workflow graph snapshot is null on the execution entity (e.g., from a legacy execution)? The API should return `null` for `GraphSnapshot` rather than failing.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST record the input data for every node execution, serialized as a JSON string, in the `Input` field of the node execution record before the node begins processing.
- **FR-002**: System MUST record node input data across all execution paths: sequential, conditional branching, FanOut parallel, and FanIn aggregation.
- **FR-003**: System MUST persist node input data even when the node subsequently fails during execution.
- **FR-004**: The first node in a workflow MUST record the workflow-level input (the data submitted with the execution request) as its input.
- **FR-005**: System MUST include the `GraphSnapshot` (node list and edge list) in the execution details API response when querying a specific workflow execution.
- **FR-006**: The `GraphSnapshot` MUST reflect the workflow graph structure as it was captured at execution creation time (point-in-time snapshot), not the current definition.
- **FR-007**: System MUST support a mock execution mode for Agent nodes that returns simulated responses without requiring a real LLM provider.
- **FR-008**: Mock Agent responses MUST include the node name and a summary of the input received, producing a deterministic and inspectable output.
- **FR-009**: Mock execution mode MUST be configurable per-agent or per-execution context, allowing mixed real and mock agents in the same workflow.
- **FR-010**: System MUST pass an end-to-end smoke test covering the full lifecycle: create workflow → publish → execute → query execution details, with all assertions passing.

### Key Entities

- **NodeExecutionVO**: Value object representing a single node's execution within a workflow run. Key attributes: NodeId, Status, Input (JSON string of data received), Output (JSON string of data produced), ErrorMessage, StartedAt, CompletedAt.
- **WorkflowExecution**: Entity representing a complete workflow run. Contains the list of NodeExecutionVOs, execution status, and a GraphSnapshot capturing the workflow structure at execution time.
- **WorkflowGraphVO**: Value object capturing the DAG structure (nodes and edges) of a workflow definition, used as the point-in-time snapshot in executions.
- **ResolvedAgent**: Record representing a resolved agent with its configuration, used by the engine to determine how to execute each Agent node (real LLM or mock).

## Assumptions

- The `NodeExecutionVO.Input` field already exists in the domain model and the corresponding DTO — only the write path in the engine is missing.
- The `WorkflowGraphVO → WorkflowGraphDto` mapping already exists in the mapping layer — only the inclusion in `WorkflowExecutionDto` is missing.
- BoxLite dependency removal (P1-1) is already completed and is not in scope for this specification.
- The mock agent mode does not need to simulate tool calls, streaming, or multi-turn conversations — a simple text response is sufficient for this phase.
- Existing workflow creation, publishing, and basic execution endpoints are functional — this spec fixes only the data recording, API response completeness, and testability gaps.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of node executions in a completed workflow have a non-null, valid JSON `Input` field — verified by executing a 3-node sequential workflow and inspecting all node records.
- **SC-002**: Execution detail API responses include a complete `GraphSnapshot` for 100% of new executions — verified by querying any execution created after the fix and confirming nodes and edges are present.
- **SC-003**: A workflow with 3 Agent nodes completes successfully in mock mode without any LLM provider configured — verified by the end-to-end smoke test.
- **SC-004**: The end-to-end smoke test (create → publish → execute → query) passes consistently — verified by running the test suite 3 consecutive times with no failures.
- **SC-005**: All existing workflow engine tests continue to pass — verified by running the full test suite with zero regressions.
