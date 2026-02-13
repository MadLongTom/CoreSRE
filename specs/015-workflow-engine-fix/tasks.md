# Tasks: 工作流引擎基础修复 (Workflow Engine Base Fix)

**Input**: Design documents from `/specs/015-workflow-engine-fix/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅, quickstart.md ✅

**Tests**: Included — TDD is mandated by the project constitution (NON-NEGOTIABLE).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)
- Include exact file paths in descriptions

## Phase 1: Setup

**Purpose**: No new project setup needed — all changes fit within existing project structure. This phase handles the domain-level signature change that all subsequent phases depend on.

- [X] T001 Modify `StartNode(string nodeId)` → `StartNode(string nodeId, string? input)` and write `Input = input` in the `with` expression in `Backend/CoreSRE.Domain/Entities/WorkflowExecution.cs`
- [X] T002 Update all existing `StartNode` calls in `Backend/CoreSRE.Infrastructure.Tests/Workflows/WorkflowExecutionTests.cs` to pass a second argument (e.g., `null` or test input string) — setup/helper refactoring only, no assertion changes

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: DTO change that US1 and US2 both depend on, and the mock client that US3 and US4 depend on. These must complete before story-specific work begins.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T003 [P] Add `public WorkflowGraphDto? GraphSnapshot { get; init; }` property to `WorkflowExecutionDto` in `Backend/CoreSRE.Application/Workflows/DTOs/WorkflowExecutionDto.cs`
- [X] T004 [P] Create `MockChatClient` implementing `IChatClient` in `Backend/CoreSRE.Infrastructure/Services/MockChatClient.cs` — `GetResponseAsync` returns JSON with `mock`, `agentName`, `inputSummary`, `timestamp` fields; constructor takes `string agentName`

**Checkpoint**: Domain signature updated, DTO extended, MockChatClient available — user story implementation can begin

---

## Phase 3: User Story 1 — Node Input Traceability (Priority: P1) 🎯 MVP

**Goal**: Every node execution records its input data in `NodeExecutionVO.Input` before processing begins, across all execution paths (sequential, condition, FanOut, FanIn).

**Independent Test**: Execute a 3-node sequential workflow, inspect all `NodeExecutionVO.Input` fields — all non-null with correct data.

### Tests for User Story 1 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T005 [P] [US1] Write test `SequentialExecution_RecordsInputForAllNodes` — 3-node sequential workflow, assert each `NodeExecutionVO.Input` is non-null and matches expected data chain in `Backend/CoreSRE.Infrastructure.Tests/Workflows/NodeInputRecordingTests.cs`
- [X] T006 [P] [US1] Write test `FirstNode_RecordsWorkflowLevelInput` — single node workflow, assert first node's `Input` contains the workflow-level input data (serialized `JsonElement`) in `Backend/CoreSRE.Infrastructure.Tests/Workflows/NodeInputRecordingTests.cs`
- [X] T007 [P] [US1] Write test `FailedNode_StillRecordsInput` — node that throws, assert `Input` is populated even though node status is `Failed` in `Backend/CoreSRE.Infrastructure.Tests/Workflows/NodeInputRecordingTests.cs`
- [X] T008 [P] [US1] Write test `ConditionNode_RecordsInputBeforeEvaluation` — workflow with condition node, assert condition node's `Input` captures the `lastOutput` passed to it in `Backend/CoreSRE.Infrastructure.Tests/Workflows/NodeInputRecordingTests.cs`

### Implementation for User Story 1

- [X] T009 [US1] Pass `lastOutput` as input to `execution.StartNode(node.NodeId, lastOutput)` at sequential execution path (line ~120) in `Backend/CoreSRE.Infrastructure/Services/WorkflowEngine.cs`
- [X] T010 [US1] Pass `lastOutput` as input to `execution.StartNode(conditionNode.NodeId, lastOutput)` at condition node path (line ~361) in `Backend/CoreSRE.Infrastructure/Services/WorkflowEngine.cs`
- [X] T011 [US1] Pass `lastOutput` as input to `execution.StartNode(fanOutNode.NodeId, lastOutput)` at FanOut node path (line ~462) in `Backend/CoreSRE.Infrastructure/Services/WorkflowEngine.cs`
- [X] T012 [US1] Pass `lastOutput` as input to `execution.StartNode(parallelNode.NodeId, lastOutput)` at FanOut parallel branch path (line ~474) in `Backend/CoreSRE.Infrastructure/Services/WorkflowEngine.cs`
- [X] T013 [US1] Pass aggregated input to `execution.StartNode(fanInNode.NodeId, aggregatedInput)` at FanIn node path (line ~568) in `Backend/CoreSRE.Infrastructure/Services/WorkflowEngine.cs`
- [X] T014 [US1] Run all `NodeInputRecordingTests` — verify all tests pass (Green) in `Backend/CoreSRE.Infrastructure.Tests/Workflows/NodeInputRecordingTests.cs`
- [X] T015 [US1] Run existing `WorkflowEngineTests` — verify zero regressions in `Backend/CoreSRE.Infrastructure.Tests/Workflows/WorkflowEngineTests.cs`

**Checkpoint**: Node Input Traceability complete — all node executions record their input data. US1 independently verifiable.

---

## Phase 4: User Story 2 — Execution Graph Snapshot in API (Priority: P1)

**Goal**: `GET /api/workflows/{id}/executions/{execId}` response includes `graphSnapshot` with the full DAG structure (nodes + edges) captured at execution creation time.

**Independent Test**: Execute a workflow, query the execution detail endpoint, confirm `graphSnapshot.nodes` and `graphSnapshot.edges` are present in the JSON response.

### Tests for User Story 2 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T016 [P] [US2] Write test `MapWorkflowExecution_IncludesGraphSnapshot` — create `WorkflowExecution` with a 3-node graph, map via AutoMapper, assert `WorkflowExecutionDto.GraphSnapshot` is not null and contains 3 nodes and 2 edges in `Backend/CoreSRE.Application.Tests/Workflows/WorkflowExecutionDtoMappingTests.cs`
- [X] T017 [P] [US2] Write test `MapWorkflowExecution_NullGraphSnapshot_MapsToNull` — create execution with empty/default `WorkflowGraphVO`, map via AutoMapper, assert no exception and `GraphSnapshot` is handled gracefully in `Backend/CoreSRE.Application.Tests/Workflows/WorkflowExecutionDtoMappingTests.cs`
- [X] T018 [P] [US2] Write test `MapWorkflowExecution_GraphSnapshotPreservesNodeDetails` — assert mapped nodes include correct `NodeId`, `NodeType` (as string), `DisplayName`, `ReferenceId`, and `Config` in `Backend/CoreSRE.Application.Tests/Workflows/WorkflowExecutionDtoMappingTests.cs`

### Implementation for User Story 2

- [X] T019 [US2] Verify AutoMapper convention mapping works — no mapping profile changes needed since `GraphSnapshot` property name matches and `WorkflowGraphVO → WorkflowGraphDto` mapping already exists in `Backend/CoreSRE.Application/Workflows/DTOs/WorkflowMappingProfile.cs`
- [X] T020 [US2] Run all `WorkflowExecutionDtoMappingTests` — verify all tests pass (Green) in `Backend/CoreSRE.Application.Tests/Workflows/WorkflowExecutionDtoMappingTests.cs`
- [X] T021 [US2] Validate response against contract schema in `specs/015-workflow-engine-fix/contracts/execution-detail-response.json`

**Checkpoint**: GraphSnapshot mapping complete — execution detail API includes full DAG. US2 independently verifiable.

---

## Phase 5: User Story 3 — Mock Agent Execution Mode (Priority: P2)

**Goal**: Agent nodes can execute with simulated responses via `MockChatClient` when no LLM provider is configured or when `Workflow:MockAgentMode` is enabled, allowing workflow development and testing without external dependencies.

**Independent Test**: Configure mock mode, execute a 3-node agent workflow, verify all nodes complete with mock responses containing agent name and input summary.

### Tests for User Story 3 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T022 [P] [US3] Write test `MockChatClient_ReturnsResponseWithAgentNameAndInput` — instantiate `MockChatClient("test-agent")`, call `GetResponseAsync` with a user message, assert response contains `mock: true`, `agentName: "test-agent"`, and `inputSummary` in `Backend/CoreSRE.Infrastructure.Tests/Workflows/MockAgentTests.cs`
- [X] T023 [P] [US3] Write test `MockChatClient_TruncatesLongInput` — pass a 500+ char message, assert `inputSummary` is truncated to 200 chars in `Backend/CoreSRE.Infrastructure.Tests/Workflows/MockAgentTests.cs`
- [X] T024 [P] [US3] Write test `AgentResolver_MockMode_ReturnsMockAgent` — configure `Workflow:MockAgentMode = true`, call `ResolveAsync`, assert returned agent uses `MockChatClient` in `Backend/CoreSRE.Infrastructure.Tests/Workflows/MockAgentTests.cs`
- [X] T025 [P] [US3] Write test `AgentResolver_NoLlmProvider_FallsBackToMock` — no LLM provider configured for agent, call `ResolveAsync`, assert returned agent uses `MockChatClient` instead of throwing in `Backend/CoreSRE.Infrastructure.Tests/Workflows/MockAgentTests.cs`

### Implementation for User Story 3

- [X] T026 [US3] Add mock mode check to `AgentResolverService.ResolveAsync` — read `Workflow:MockAgentMode` from `IConfiguration`, if `true` return `ResolvedAgent` wrapping `MockChatClient` with agent name in `Backend/CoreSRE.Infrastructure/Services/AgentResolverService.cs`
- [X] T027 [US3] Add fallback to `MockChatClient` when no `LlmProvider` is found for the agent in the `ChatClient` resolution path (instead of throwing) in `Backend/CoreSRE.Infrastructure/Services/AgentResolverService.cs`
- [X] T028 [US3] Run all `MockAgentTests` — verify all tests pass (Green) in `Backend/CoreSRE.Infrastructure.Tests/Workflows/MockAgentTests.cs`

**Checkpoint**: Mock Agent Mode complete — workflows execute without LLM. US3 independently verifiable.

---

## Phase 6: User Story 4 — End-to-End Smoke Test (Priority: P2)

**Goal**: A comprehensive smoke test validates the full lifecycle: create workflow graph → create execution → run engine → verify execution status, node inputs, and graph snapshot.

**Independent Test**: Run the smoke test class and confirm it passes within 30 seconds.

### Tests for User Story 4 ⚠️

- [X] T029 [US4] Write test `EndToEnd_CreatePublishExecuteQuery_AllSucceed` — build a 3-node sequential agent workflow graph, create `WorkflowExecution` via factory, run `WorkflowEngine.ExecuteAsync` with mock agents, assert: status `Completed`, all 3 `NodeExecutionVO.Input` fields populated, graph snapshot present on entity, data flows correctly through chain in `Backend/CoreSRE.Infrastructure.Tests/Workflows/WorkflowEngineEndToEndTests.cs`
- [X] T030 [US4] Write test `EndToEnd_MockAgentChain_DataFlowsCorrectly` — verify node-2 input contains node-1 mock output, node-3 input contains node-2 mock output — proving data chaining works end-to-end in `Backend/CoreSRE.Infrastructure.Tests/Workflows/WorkflowEngineEndToEndTests.cs`

### Verification for User Story 4

- [X] T031 [US4] Run all tests in `Backend/CoreSRE.Infrastructure.Tests/Workflows/WorkflowEngineEndToEndTests.cs` — verify all pass
- [X] T032 [US4] Run full test suite across both test projects — verify zero regressions: `dotnet test Backend/CoreSRE.Infrastructure.Tests` and `dotnet test Backend/CoreSRE.Application.Tests`

**Checkpoint**: End-to-End Smoke Test complete — full lifecycle validated. All 4 user stories verified.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final validation and cleanup

- [X] T033 [P] Add `Workflow:MockAgentMode` configuration entry to `Backend/CoreSRE/appsettings.Development.json` with default `false` and a comment explaining usage
- [X] T034 Run quickstart.md validation — verify all verification commands from `specs/015-workflow-engine-fix/quickstart.md` succeed
- [X] T035 Run full test suite 3 consecutive times to confirm consistent results (SC-004)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately. Changes `StartNode` signature in Domain.
- **Phase 2 (Foundational)**: Depends on Phase 1 (StartNode takes 2 params now). Creates DTO property and MockChatClient.
- **Phase 3 (US1)**: Depends on Phase 1 + 2. Implements input recording in WorkflowEngine.
- **Phase 4 (US2)**: Depends on Phase 2 (T003 — DTO property exists). Independent of Phase 3.
- **Phase 5 (US3)**: Depends on Phase 2 (T004 — MockChatClient exists). Independent of Phase 3 and 4.
- **Phase 6 (US4)**: Depends on Phase 3 + 4 + 5 (validates all fixes together).
- **Phase 7 (Polish)**: Depends on Phase 6.

### User Story Dependencies

- **US1 (P1)**: Depends on Phase 2 — no dependency on other stories
- **US2 (P1)**: Depends on Phase 2 — no dependency on other stories
- **US3 (P2)**: Depends on Phase 2 — no dependency on other stories
- **US4 (P2)**: Depends on US1 + US2 + US3 (validates all three fixes together)

### Within Each User Story

- Tests MUST be written and FAIL before implementation (TDD — Constitution II)
- Implementation follows test-by-test
- Run tests after implementation to verify Green
- Run existing tests to verify zero regressions

### Parallel Opportunities

**After Phase 2 completes, US1, US2, and US3 can all proceed in parallel:**

```
Phase 1 (Setup) ──→ Phase 2 (Foundational) ──┬──→ Phase 3 (US1: Input Recording)  ──┐
                                              ├──→ Phase 4 (US2: GraphSnapshot DTO)  ──┤──→ Phase 6 (US4: E2E) ──→ Phase 7
                                              └──→ Phase 5 (US3: Mock Agent Mode)   ──┘
```

### Parallel Batches

```text
# Batch 1: Phase 1 (sequential — one file)
T001 → T002

# Batch 2: Phase 2 (parallel — two different files)
T003 + T004 (simultaneously)

# Batch 3: Phase 3 + 4 + 5 tests (all parallel — different new test files)
T005 + T006 + T007 + T008   (US1 tests)
T016 + T017 + T018           (US2 tests)
T022 + T023 + T024 + T025   (US3 tests)

# Batch 4: Phase 3 + 4 + 5 implementation (parallel across stories)
T009–T015 (US1 impl)  |  T019–T021 (US2 impl)  |  T026–T028 (US3 impl)

# Batch 5: Phase 6 (sequential — depends on all stories)
T029 → T030 → T031 → T032

# Batch 6: Phase 7 (polish)
T033 + T034 → T035
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001–T002)
2. Complete Phase 2: Foundational (T003–T004)
3. Complete Phase 3: User Story 1 — Node Input Traceability (T005–T015)
4. **STOP and VALIDATE**: Run `NodeInputRecordingTests` + `WorkflowEngineTests` to confirm input recording works with zero regressions
5. This alone fixes D5 and delivers the most critical observability improvement

### Incremental Delivery

1. Phase 1 + 2 → Foundation ready
2. + US1 (Phase 3) → Input traceability fixed → **MVP complete** (D5 resolved)
3. + US2 (Phase 4) → GraphSnapshot in API → Frontend can render execution details
4. + US3 (Phase 5) → Mock agent mode → Dev/test workflow enabled (D8 resolved)
5. + US4 (Phase 6) → End-to-end smoke test → Regression safety net
6. + Phase 7 → Configuration and final validation

### Suggested MVP Scope

**User Story 1 (Node Input Traceability)** — Phases 1–3 only. This resolves the most critical defect (D5) and can be delivered in ~2 hours.

---

## Notes

- All tasks follow strict TDD: tests ⚠️ → implementation → verify Green
- No existing test assertions are modified (Constitution IV). Only `StartNode` call-site arguments are updated in existing tests (setup refactoring — permitted).
- No database migrations needed — `NodeExecutionVO.Input` already exists in schema
- No mapping profile changes needed — AutoMapper convention handles `GraphSnapshot`
- Total: 35 tasks across 7 phases covering 4 user stories
