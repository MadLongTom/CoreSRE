# Tasks: 工作流数据流模型与执行栈引擎

**Input**: Design documents from `/specs/016-workflow-dataflow-engine/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/workflow-api.md, quickstart.md

**Tests**: TDD is NON-NEGOTIABLE per Constitution. All new code MUST be preceded by failing tests. Red→Green→Refactor.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story. User stories are ordered by implementation dependency (US2→US5→US1→US3→US4→US6).

**Test Immutability**: All 79+ existing committed tests MUST NOT be modified. New test files only.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1–US6)
- Include exact file paths in descriptions

## Path Conventions

- **Backend**: `Backend/CoreSRE.Domain/`, `Backend/CoreSRE.Application/`, `Backend/CoreSRE.Infrastructure/`
- **Tests**: `Backend/CoreSRE.Infrastructure.Tests/`, `Backend/CoreSRE.Application.Tests/`
- All paths relative to repository root (`E:\CoreSRE`)

---

## Phase 1: Setup

**Purpose**: Create project structure for new test categories and verify baseline

- [x] T001 Create test directories Backend/CoreSRE.Infrastructure.Tests/Workflows/DataFlow/, Backend/CoreSRE.Infrastructure.Tests/Workflows/ExecutionStack/, and Backend/CoreSRE.Infrastructure.Tests/Workflows/PortRouting/
- [x] T002 Run all existing workflow and application tests as baseline verification (`dotnet test` on both CoreSRE.Infrastructure.Tests and CoreSRE.Application.Tests — all 79+ workflow tests must pass)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Extend existing domain VOs with port fields, add graph validation rules, update DTOs and mapping. These cross-cutting additions are required by ALL user stories.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Validation Tests (Red)

- [x] T003 [P] Write port index validation tests in Backend/CoreSRE.Infrastructure.Tests/Workflows/PortValidationTests.cs — test cases: (1) edge with SourcePortIndex >= source node OutputCount fails validation, (2) edge with TargetPortIndex >= target node InputCount fails validation, (3) Condition node with OutputCount < 2 fails validation, (4) valid port indices pass, (5) default port indices (0) with default counts (1) pass. Reference existing DagValidationTests.cs for test setup pattern.

### Modified Domain VOs

- [x] T004 [P] Add `InputCount` (int, init, default 1) and `OutputCount` (int, init, default 1) properties to Backend/CoreSRE.Domain/ValueObjects/WorkflowNodeVO.cs — use `public int InputCount { get; init; } = 1;` pattern for backward-compatible JSONB deserialization
- [x] T005 [P] Add `SourcePortIndex` (int, init, default 0) and `TargetPortIndex` (int, init, default 0) properties to Backend/CoreSRE.Domain/ValueObjects/WorkflowEdgeVO.cs — same init-default pattern
- [x] T006 Add 3 new validation rules to `Validate()` method in Backend/CoreSRE.Domain/ValueObjects/WorkflowGraphVO.cs: (1) for each edge, `SourcePortIndex < sourceNode.OutputCount` else error "边 '{edgeId}' 的源端口索引 {idx} 超出节点 '{nodeId}' 的输出端口数 {count}", (2) for each edge, `TargetPortIndex < targetNode.InputCount` else similar error, (3) for each Condition node, `OutputCount >= 2` else error "条件节点 '{nodeId}' 的输出端口数必须 >= 2，当前为 {count}"
- [x] T007 Verify port validation tests (T003) pass — Red→Green for T003

### DTOs & Mapping

- [x] T008 [P] Add `InputCount` (int, default 1) and `OutputCount` (int, default 1) properties to Backend/CoreSRE.Application/Workflows/DTOs/WorkflowNodeDto.cs
- [x] T009 [P] Add `SourcePortIndex` (int, default 0) and `TargetPortIndex` (int, default 0) properties to Backend/CoreSRE.Application/Workflows/DTOs/WorkflowEdgeDto.cs
- [x] T010 Verify AutoMapper automatically maps new same-name properties in Backend/CoreSRE.Application/Workflows/DTOs/WorkflowMappingProfile.cs — AutoMapper convention maps matching property names; add explicit `.ForMember()` only if auto-mapping fails
- [x] T011 Run all existing tests to confirm zero regressions from foundational field additions (79+ workflow tests + application tests)

**Checkpoint**: Foundation ready — all existing tests pass with new fields defaulting to backward-compatible values. User story implementation can now begin.

---

## Phase 3: User Story 2 — Structured Data Flow (Priority: P1) 🎯 MVP

**Goal**: Represent data flowing between nodes as structured items (WorkflowItemVO) organized by port (PortDataVO) with typed input/output containers (NodeInputData/NodeOutputData). Eliminates `string? lastOutput` linear passing (fixes D1).

**Independent Test**: Create WorkflowItemVO with JSON payload and ItemSourceVO, organize items into PortDataVO, construct NodeInputData/NodeOutputData, serialize to JSON string and back. All operations produce correct structured data.

### Tests for User Story 2 ⚠️

> **NOTE: Write these tests FIRST. They will not compile until implementation types exist — create minimal type stubs (empty records) first to make tests compilable, then implement fully.**

- [x] T012 [P] [US2] Write unit tests for WorkflowItemVO and ItemSourceVO in Backend/CoreSRE.Infrastructure.Tests/Workflows/DataFlow/WorkflowItemTests.cs — test cases: (1) construct item with JsonElement payload, (2) construct item with null source, (3) construct item with ItemSourceVO(nodeId, outputIndex, itemIndex), (4) two items with same payload and source are equal (record equality), (5) JsonElement round-trips through serialization
- [x] T013 [P] [US2] Write unit tests for PortDataVO in Backend/CoreSRE.Infrastructure.Tests/Workflows/DataFlow/PortDataTests.cs — test cases: (1) create PortDataVO with list of items, (2) empty port (no items), (3) access items by index, (4) PortDataVO is immutable (items list is read-only)
- [x] T014 [P] [US2] Write unit tests for NodeInputData and NodeOutputData in Backend/CoreSRE.Infrastructure.Tests/Workflows/DataFlow/NodeDataTests.cs — test cases: (1) construct NodeInputData with main connection type and port array, (2) access items on specific port index, (3) construct NodeOutputData with multi-port data, (4) serialize NodeInputData to JSON string matching format `{"main":[[items]]}`, (5) deserialize JSON string back to NodeInputData, (6) single-port convenience (port 0 accessor), (7) empty NodeInputData (no ports)

### Implementation for User Story 2

- [x] T015 [P] [US2] Create ItemSourceVO record in Backend/CoreSRE.Domain/ValueObjects/ItemSourceVO.cs — properties: `string NodeId`, `int OutputIndex`, `int ItemIndex`. Immutable C# record. Represents lineage of a data item.
- [x] T016 [P] [US2] Create WorkflowItemVO record in Backend/CoreSRE.Domain/ValueObjects/WorkflowItemVO.cs — properties: `JsonElement Json` (the data payload), `ItemSourceVO? Source` (optional lineage). Uses `System.Text.Json.JsonElement`.
- [x] T017 [P] [US2] Create PortDataVO record in Backend/CoreSRE.Domain/ValueObjects/PortDataVO.cs — property: `IReadOnlyList<WorkflowItemVO> Items`. Wraps an ordered list of items on a single port. Include static `Empty` property for convenience.
- [x] T018 [US2] Create NodeInputData record in Backend/CoreSRE.Domain/ValueObjects/NodeInputData.cs — property: `IReadOnlyDictionary<string, IReadOnlyList<PortDataVO?>> Connections` where key is connection type ("main"), value is port array indexed by port number. Include convenience methods: `GetPort(int index)` returns items on main port, `ToJsonString()` serializes to the format specified in data-model.md, `static FromJsonString(string json)` deserializes.
- [x] T019 [US2] Create NodeOutputData record in Backend/CoreSRE.Domain/ValueObjects/NodeOutputData.cs — same structure as NodeInputData. Property: `IReadOnlyDictionary<string, IReadOnlyList<PortDataVO?>> Connections`. Include `GetPort(int index)`, `ToJsonString()`, `static FromJsonString(string json)`. Output ports correspond to node's declared OutputCount.

**Checkpoint**: All structured data types exist and are independently unit-tested. The data flow model is ready for the execution engine.

---

## Phase 4: User Story 5 — Execution Stack Engine (Priority: P1)

**Goal**: Replace topological sort + `string? lastOutput` linear execution with a stack-based engine that pops nodes, executes them, propagates structured data to downstream nodes via edges, and pushes ready nodes onto the stack. Directly fixes D2.

**Independent Test**: Execute a linear chain (A → B → C) with the new engine and verify stack-based execution order, structured data propagation between nodes, and correct node execution records.

### Tests for User Story 5 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T020 [P] [US5] Write unit tests for ExecutionContext in Backend/CoreSRE.Infrastructure.Tests/Workflows/ExecutionStack/ExecutionContextTests.cs — test cases: (1) push and pop NodeExecutionTask from stack (LIFO order), (2) add node to waiting queue, (3) promote node from waiting queue to stack when all ports satisfied, (4) check if stack is empty, (5) record node run result, (6) retrieve run results by node ID
- [x] T021 [P] [US5] Write unit tests for NodeExecutionTask in Backend/CoreSRE.Infrastructure.Tests/Workflows/ExecutionStack/NodeExecutionTaskTests.cs — test cases: (1) construct with node reference and input data, (2) run index defaults to 0, (3) construct with explicit run index for re-execution
- [x] T022 [P] [US5] Write unit tests for WaitingNodeData in Backend/CoreSRE.Infrastructure.Tests/Workflows/ExecutionStack/WaitingNodeDataTests.cs — test cases: (1) create with expected port count, (2) receive data on one port — not yet complete, (3) receive data on all ports — IsComplete returns true, (4) build combined NodeInputData from received port data, (5) duplicate port data overwrites previous
- [x] T023 [US5] Write stack-based engine execution tests in Backend/CoreSRE.Infrastructure.Tests/Workflows/ExecutionStack/StackEngineTests.cs — following mock setup pattern from existing WorkflowEngineTests.cs. Test cases: (1) linear 3-node chain executes in order A→B→C, (2) each node receives structured NodeInputData, (3) each node produces structured NodeOutputData, (4) execution records contain structured input/output JSON strings, (5) start node receives initial input wrapped as structured items, (6) engine handles node that produces no output (downstream not executed), (7) infinite loop protection triggers after execution count limit. Use MockChatClient from feature 015 for Agent node mocks.

### Implementation for User Story 5

- [x] T024 [P] [US5] Create ExecutionContext class in Backend/CoreSRE.Infrastructure/Services/ExecutionContext.cs — internal class with: `Stack<NodeExecutionTask> ExecutionStack`, `Dictionary<string, WaitingNodeData> WaitingNodes`, `Dictionary<string, List<NodeRunResult>> RunResults`, pre-computed edge maps `Dictionary<string, List<WorkflowEdgeVO>> OutgoingEdges` and `Dictionary<string, List<WorkflowEdgeVO>> IncomingEdges`. Methods: `Push(task)`, `Pop()`, `IsStackEmpty`, `AddToWaiting(nodeId, data)`, `TryPromote(nodeId)`, `RecordResult(nodeId, result)`.
- [x] T025 [P] [US5] Create NodeExecutionTask record in Backend/CoreSRE.Infrastructure/Services/NodeExecutionTask.cs — internal record with: `WorkflowNodeVO Node`, `NodeInputData InputData`, `int RunIndex = 0`, `ItemSourceVO? TriggerSource = null`.
- [x] T026 [P] [US5] Create WaitingNodeData class in Backend/CoreSRE.Infrastructure/Services/WaitingNodeData.cs — internal class with: `int ExpectedPortCount`, `Dictionary<int, PortDataVO> ReceivedPorts`, `bool IsComplete => ReceivedPorts.Count >= ExpectedPortCount`, `void ReceivePort(int portIndex, PortDataVO data)`, `NodeInputData BuildInputData()`.
- [x] T027 [P] [US5] Create NodeRunResult record in Backend/CoreSRE.Infrastructure/Services/NodeRunResult.cs — internal record with: `NodeOutputData? OutputData`, `DateTimeOffset StartedAt`, `DateTimeOffset? CompletedAt`, `bool IsSuccess`, `string? ErrorMessage`.
- [x] T028 [US5] Rewrite `ExecuteAsync` method in Backend/CoreSRE.Infrastructure/Services/WorkflowEngine.cs — replace TopologicalSort + foreach loop with: (1) build ExecutionContext from graph edges, (2) wrap initial input as NodeInputData, create NodeExecutionTask for start node, push to stack, (3) main loop: while stack not empty, pop task → execute node → record result → propagate data, (4) after stack empty, check waiting queue for promotable nodes. Remove TopologicalSort method. Keep `ExecuteAsync(WorkflowExecution execution, string? input, CancellationToken ct)` signature unchanged.
- [x] T029 [US5] Implement `PropagateData` method in Backend/CoreSRE.Infrastructure/Services/WorkflowEngine.cs — for each outgoing edge from completed node: (1) get PortDataVO from outputData at edge.SourcePortIndex, (2) find target node, (3) if target InputCount == 1: create NodeInputData wrapping port data, push NodeExecutionTask to stack, (4) if target InputCount > 1: call context.AddToWaiting with port data on edge.TargetPortIndex, then TryPromote — if all ports received, build NodeInputData and push to stack. Add ItemSourceVO to propagated items (nodeId, outputIndex, itemIndex).
- [x] T030 [US5] Implement node dispatch methods in Backend/CoreSRE.Infrastructure/Services/WorkflowEngine.cs — refactor existing ExecuteAgentNodeAsync, ExecuteToolNodeAsync, ExecuteConditionNodeAsync to accept NodeInputData and return NodeOutputData: (1) Agent: extract text from input items → build prompt → invoke IChatClient → wrap response as output items on port 0, (2) Tool: extract params from input items → invoke tool → wrap result as output items on port 0, (3) Condition: evaluate condition against input → place output on port 0 (true) or port 1 (false) based on result, (4) FanOut: copy input items to all output ports (OutputCount ports), (5) FanIn: merge items from all input ports into single output on port 0. Keep existing IConditionEvaluator and IExpressionEvaluator integration.
- [x] T031 [US5] Implement infinite loop protection in Backend/CoreSRE.Infrastructure/Services/WorkflowEngine.cs — maintain `Dictionary<string, int> nodeExecutionCounts` in ExecutionContext, increment on each node execution, throw if count exceeds limit (default 100 per node per workflow run, per spec assumptions). Fail the workflow execution with descriptive error.
- [x] T032 [US5] Verify all execution stack tests pass (T020–T023 Red→Green)

**Checkpoint**: Stack-based engine is functional for linear workflows. Structured data flows through nodes. Ready for backward compatibility verification.

---

## Phase 5: User Story 1 — Backward-Compatible Workflow Execution (Priority: P1)

**Goal**: Verify that ALL existing workflow definitions execute with identical results under the new engine. Old nodes default to InputCount=1/OutputCount=1, old edges default to SourcePortIndex=0/TargetPortIndex=0. No existing test may be modified.

**Independent Test**: Run all 79+ existing workflow tests. Every single one must pass without any modification.

### Verification

- [x] T033 [US1] Run all 79+ existing workflow tests (`dotnet test CoreSRE.Infrastructure.Tests --filter "Workflows"`) — verify 100% pass rate with the new stack-based engine. If any test fails, the engine implementation must be fixed before proceeding.

### Tests for User Story 1 ⚠️

> **NOTE: These tests verify backward compatibility with ADDITIONAL scenarios beyond existing tests**

- [x] T034 [P] [US1] Write backward compat test for linear Agent chain (3 nodes, no port fields) in Backend/CoreSRE.Infrastructure.Tests/Workflows/ExecutionStack/BackwardCompatTests.cs — verify: nodes execute in order, each receives previous output as structured input, final output matches expected, execution records have structured format
- [x] T035 [P] [US1] Write backward compat test for FanOut→parallel branches→FanIn workflow (no port fields) in Backend/CoreSRE.Infrastructure.Tests/Workflows/ExecutionStack/BackwardCompatTests.cs — verify: FanOut dispatches to branches, FanIn merges results, execution matches previous behavior
- [x] T036 [P] [US1] Write backward compat test for Condition node with conditional edges (no port fields) in Backend/CoreSRE.Infrastructure.Tests/Workflows/ExecutionStack/BackwardCompatTests.cs — verify: condition evaluates correctly, only matching branch executes, skipped nodes marked appropriately

### Implementation

- [x] T037 [US1] Fix any engine issues discovered by existing tests or new backward compat tests until all pass — iterate on WorkflowEngine.cs until 100% backward compatibility achieved

**Checkpoint**: Full backward compatibility verified. All existing workflows produce identical results. Safe to add new features.

---

## Phase 6: User Story 3 — Multi-Port Output Routing (Priority: P1)

**Goal**: Nodes with multiple output ports (OutputCount > 1) route data to specific downstream nodes based on edge SourcePortIndex. Condition nodes use port 0/1 for true/false branches. Fixes D10.

**Independent Test**: Create a Condition node with OutputCount=2, connect port 0 to "true branch" and port 1 to "false branch". Execute with condition=true. Verify only true-branch node executes.

### Tests for User Story 3 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation fixes**

- [x] T038 [P] [US3] Write test for Condition node OutputCount=2 routing in Backend/CoreSRE.Infrastructure.Tests/Workflows/PortRouting/ConditionRoutingTests.cs — test cases: (1) condition evaluates true → data flows to port 0 target only, port 1 target does not execute, (2) condition evaluates false → data flows to port 1 target only, (3) condition node with OutputCount=2 and chain after each branch — only matching chain executes
- [x] T039 [P] [US3] Write test for multi-output selective port dispatch in Backend/CoreSRE.Infrastructure.Tests/Workflows/PortRouting/MultiPortOutputTests.cs — test cases: (1) node with OutputCount=3 producing data on ports 0 and 2 only → only nodes connected to ports 0 and 2 execute, (2) FanOut node copies input items to all output ports
- [x] T040 [US3] Write test for skipped branch chain in Backend/CoreSRE.Infrastructure.Tests/Workflows/PortRouting/SkippedBranchTests.cs — test case: output port has no data → entire downstream chain from that port never executes, all nodes in chain remain Pending status

### Implementation

- [x] T041 [US3] Verify engine handles multi-port output routing correctly — if any test fails, fix PropagateData and/or Condition dispatch in Backend/CoreSRE.Infrastructure/Services/WorkflowEngine.cs. Ensure: (1) PropagateData reads correct SourcePortIndex from each edge, (2) Condition dispatch places result on correct output port index, (3) ports with no data produce no downstream pushes

**Checkpoint**: Multi-port output routing works for Condition and FanOut scenarios. Data flows only through edges connected to active ports.

---

## Phase 7: User Story 4 — Multi-Input Waiting Queue (Priority: P1)

**Goal**: Nodes with InputCount > 1 wait in a queue until data arrives on ALL input ports, then are promoted to the execution stack. Replaces explicit FanIn node type with general-purpose mechanism.

**Independent Test**: Create diamond workflow: Start → (Branch A, Branch B) → Merge(InputCount=2). Verify Merge executes only after both branches complete, receiving data from both on separate ports.

### Tests for User Story 4 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation fixes**

- [x] T042 [P] [US4] Write test for diamond workflow with merge node InputCount=2 in Backend/CoreSRE.Infrastructure.Tests/Workflows/PortRouting/WaitingQueueTests.cs — test cases: (1) Start fans out to A and B, both complete, Merge receives data from both ports, (2) Merge executes AFTER both A and B (verify execution order), (3) Merge's input contains items from A on port 0 and items from B on port 1
- [x] T043 [P] [US4] Write test for partial port arrival in Backend/CoreSRE.Infrastructure.Tests/Workflows/PortRouting/WaitingQueueTests.cs — test case: node with InputCount=2, only one upstream delivers data → node stays in waiting queue, does not execute
- [x] T044 [US4] Write test for InputCount=3 with incomplete ports in Backend/CoreSRE.Infrastructure.Tests/Workflows/PortRouting/WaitingQueueTests.cs — test case: data arrives on ports 0 and 2 but never on port 1 → node never executes, workflow completes without it, node status is Pending

### Implementation

- [x] T045 [US4] Verify engine handles waiting queue correctly — if any test fails, fix WaitingNodeData and promotion logic in Backend/CoreSRE.Infrastructure/Services/WorkflowEngine.cs. Ensure: (1) nodes with InputCount > 1 go to waiting queue, (2) promotion happens only when all ports received, (3) incomplete nodes remain in queue at workflow end

**Checkpoint**: Multi-input waiting queue works for diamond and multi-branch merge patterns. Nodes correctly wait for all input ports before executing.

---

## Phase 8: User Story 6 — Data Lineage Tracking (Priority: P2)

**Goal**: Each data item carries ItemSourceVO recording which node produced it, from which output port, at which item index. Enables tracing data backwards through the workflow graph.

**Independent Test**: Execute 3-node chain (A → B → C). Inspect items received by C — each has source pointing to B. Inspect items received by B — each has source pointing to A.

### Tests for User Story 6 ⚠️

- [x] T046 [P] [US6] Write test for source tracking through 3-node chain in Backend/CoreSRE.Infrastructure.Tests/Workflows/DataFlow/DataLineageTests.cs — test cases: (1) node B receives items with Source.NodeId = "A" and Source.OutputIndex = 0, (2) node C receives items with Source.NodeId = "B", (3) start node produces items with Source = null (root origin)
- [x] T047 [US6] Write test for lineage in multi-port workflow in Backend/CoreSRE.Infrastructure.Tests/Workflows/DataFlow/DataLineageTests.cs — test case: Condition → port 0 → Node X: items received by X carry Source referencing the Condition node with OutputIndex = 0

### Implementation

- [x] T048 [US6] Verify engine populates ItemSourceVO in PropagateData — if any test fails, fix ItemSourceVO construction in Backend/CoreSRE.Infrastructure/Services/WorkflowEngine.cs PropagateData method. Each propagated item must have Source set to `new ItemSourceVO(producingNodeId, edgeSourcePortIndex, itemIndex)`.

**Checkpoint**: Data lineage is fully traceable. Every item flowing through the workflow carries its origin information.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, documentation, and cleanup

- [x] T049 [P] Update implementation phases table in specs/016-workflow-dataflow-engine/quickstart.md with actual completed phases and task counts
- [x] T050 Run full test suite (all existing 79+ tests + all new tests) and verify 100% pass rate (`dotnet test` on all test projects)
- [x] T051 Code cleanup: verify namespaces match folder structure, add XML doc comments on all new public domain types (WorkflowItemVO, ItemSourceVO, PortDataVO, NodeInputData, NodeOutputData), ensure `internal` access on infrastructure types (ExecutionContext, NodeExecutionTask, WaitingNodeData, NodeRunResult)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Setup — **BLOCKS all user stories**
- **US2 Structured Data (Phase 3)**: Depends on Foundational — creates types needed by US5
- **US5 Execution Stack (Phase 4)**: Depends on US2 — engine uses structured data types
- **US1 Backward Compat (Phase 5)**: Depends on US5 — verifies engine preserves behavior
- **US3 Multi-Port (Phase 6)**: Depends on US5+US1 — routing is verified after backward compat
- **US4 Waiting Queue (Phase 7)**: Depends on US5+US1 — waiting logic is verified after backward compat
- **US6 Data Lineage (Phase 8)**: Depends on US5 — lineage is an engine propagation feature
- **Polish (Phase 9)**: Depends on all user stories

### User Story Dependencies

```
Foundational (Phase 2)
    │
    ▼
US2: Structured Data Flow (Phase 3) ─── data types
    │
    ▼
US5: Execution Stack Engine (Phase 4) ─── core engine
    │
    ├──────────────────┐
    ▼                  ▼
US1: Backward     US6: Data Lineage
Compat (Phase 5)  (Phase 8)
    │
    ├──────────┐
    ▼          ▼
US3: Multi-  US4: Waiting
Port (Ph 6)  Queue (Ph 7)
    │          │
    └────┬─────┘
         ▼
    Polish (Phase 9)
```

- **US3 and US4** are independent of each other — can proceed in parallel after US1
- **US6** is independent of US1/US3/US4 — can proceed after US5

### Within Each User Story

1. Tests MUST be written and FAIL before implementation (TDD)
2. Domain types before infrastructure types
3. Types before engine logic
4. Core implementation before integration
5. Story checkpoint before moving to next priority

### Parallel Opportunities

**Phase 2 (Foundational)**:
- T003, T004, T005 can run in parallel (different files)
- T008, T009 can run in parallel (different DTO files)

**Phase 3 (US2)**:
- T012, T013, T014 can run in parallel (different test files)
- T015, T016, T017 can run in parallel (different VO files)

**Phase 4 (US5)**:
- T020, T021, T022 can run in parallel (different test files)
- T024, T025, T026, T027 can run in parallel (different type files)

**Phase 6 + Phase 7 + Phase 8**:
- US3 (T038–T041) and US4 (T042–T045) can proceed in parallel after US1
- US6 (T046–T048) can proceed in parallel with US3/US4

---

## Parallel Example: User Story 2

```bash
# Step 1: Launch all US2 tests together (they won't compile yet — create type stubs):
Task: "Write WorkflowItemVO tests in DataFlow/WorkflowItemTests.cs"         # T012
Task: "Write PortDataVO tests in DataFlow/PortDataTests.cs"                 # T013
Task: "Write NodeInputData/NodeOutputData tests in DataFlow/NodeDataTests.cs" # T014

# Step 2: Launch all US2 implementations together (independent files):
Task: "Create ItemSourceVO in Domain/ValueObjects/ItemSourceVO.cs"          # T015
Task: "Create WorkflowItemVO in Domain/ValueObjects/WorkflowItemVO.cs"      # T016
Task: "Create PortDataVO in Domain/ValueObjects/PortDataVO.cs"              # T017

# Step 3: Sequential (depends on T017):
Task: "Create NodeInputData in Domain/ValueObjects/NodeInputData.cs"        # T018
Task: "Create NodeOutputData in Domain/ValueObjects/NodeOutputData.cs"      # T019
```

---

## Implementation Strategy

### MVP First (User Story 2 + User Story 5 + User Story 1)

1. ✅ Complete Phase 1: Setup
2. ✅ Complete Phase 2: Foundational (CRITICAL — blocks all stories)
3. ✅ Complete Phase 3: US2 — Structured data types exist and are tested
4. ✅ Complete Phase 4: US5 — Engine rewrite functional for linear workflows
5. ✅ Complete Phase 5: US1 — All 79+ existing tests pass
6. **STOP and VALIDATE**: Full backward compatibility achieved
7. Deploy/demo if ready — existing workflows work identically with new engine

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. US2 (Structured Data) → Data types tested independently → Building block ready
3. US5 (Execution Stack) → Engine works for linear flows → Core rewrite done
4. US1 (Backward Compat) → All existing tests pass → **MVP! Safe to deploy**
5. US3 (Multi-Port) → Condition routing verified → New routing capability
6. US4 (Waiting Queue) → Diamond merge verified → Multi-input capability
7. US6 (Data Lineage) → Traceability verified → Debugging capability
8. Each story adds value without breaking previous stories

### Key Technical Decisions (from research.md)

- **R1**: Structured items model (WorkflowItemVO with JsonElement + ItemSourceVO)
- **R2**: Stack-based execution (LIFO/DFS, not topological sort BFS)
- **R3**: Default port values (InputCount=1, OutputCount=1, PortIndex=0) for backward compat
- **R4**: Keep `string?` in NodeExecutionVO.Input/Output — engine serializes structured data to JSON strings
- **R5**: Data flow VOs in Domain, execution types in Infrastructure (internal)
- **R6**: Keep FanOut/FanIn node types — express behavior through port model
- **R7**: Sequential execution only (no Task.WhenAll) — correctness first
- **R8**: 3 new validation rules (2 port bounds + Condition OutputCount >= 2)

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story for traceability
- Each user story checkpoint verifies that story independently
- **Test Immutability**: Never modify existing committed test files
- **TDD**: Write failing test → implement → verify test passes
- Commit after each task or logical group
- Stop at any checkpoint to validate independently
- Infrastructure types (ExecutionContext, NodeExecutionTask, WaitingNodeData, NodeRunResult) are `internal` per R5 — not exposed outside CoreSRE.Infrastructure
