# Research: 工作流引擎基础修复

**Feature**: 015-workflow-engine-fix | **Date**: 2026-02-12

## Research Summary

All unknowns from the Technical Context have been resolved through codebase analysis. No external research was needed — all fixes are internal to the existing codebase with clear, deterministic solutions.

---

## R1: How to add input recording to `StartNode`

### Question
How should `NodeExecutionVO.Input` be populated? Where in the code path should the write happen?

### Research Findings

**Current state**: `WorkflowExecution.StartNode(string nodeId)` creates a `with` expression on the `NodeExecutionVO` record, setting only `Status = Running` and `StartedAt`. The `Input` field is never set anywhere — not in `StartNode`, not in `CompleteNode`, not in the Create factory.

**All 5 call sites** in `WorkflowEngine.cs` that call `execution.StartNode()`:
1. **Line 120** — Sequential node execution: `lastOutput` is available as input
2. **Line 361** — Condition node: `lastOutput` is available as input
3. **Line 462** — FanOut node: `lastOutput` is the input being fanned out
4. **Line 474** — FanOut parallel branch nodes: each branch receives `lastOutput`
5. **Line 568** — FanIn node: aggregated upstream outputs (JSON array) is the input

### Decision
Add a `string? input` parameter to `WorkflowExecution.StartNode()` and write it to the `NodeExecutionVO.Input` field in the `with` expression. Update all 5 call sites to pass the current input data.

### Rationale
- Modifying the domain method is the cleanest approach — input recording is a domain concern (execution traceability).
- Adding a new overload `StartNode(string nodeId, string? input)` would also work, but since all call sites should always pass input, changing the existing signature is simpler and avoids dead-code paths.
- The `Input` field on `NodeExecutionVO` already exists as `string? Input { get; init; }` so the record's `with` expression just needs to include `Input = input`.

### Alternatives Considered
1. **Record input in `WorkflowEngine` after `StartNode` via a separate domain method**: Rejected — violates encapsulation; the domain entity should own all state transitions.
2. **Record input in `CompleteNode`**: Rejected — if the node fails, the input would be lost (FR-003 requires input to survive failure).

---

## R2: How to add `GraphSnapshot` to `WorkflowExecutionDto`

### Question
How should the execution detail API return the graph snapshot?

### Research Findings

**Current state**: 
- `WorkflowExecution` entity has `WorkflowGraphVO GraphSnapshot` property — correctly populated at creation time via `WorkflowExecution.Create(...)`.
- `WorkflowExecutionDto` does NOT have a `GraphSnapshot` property.
- `WorkflowMappingProfile` has mappings for `WorkflowGraphVO → WorkflowGraphDto`, `WorkflowNodeVO → WorkflowNodeDto`, `WorkflowEdgeVO → WorkflowEdgeDto` — all already defined and working for the definition endpoints.
- AutoMapper's `WorkflowExecution → WorkflowExecutionDto` mapping will automatically map `GraphSnapshot` if the DTO has a matching property with the correct type.

### Decision
Add `public WorkflowGraphDto? GraphSnapshot { get; init; }` to `WorkflowExecutionDto`. No mapping profile changes needed — AutoMapper convention-based mapping will handle it.

### Rationale
- The mapping infrastructure already exists (Phase 1 of workflow CRUD, SPEC-012).
- AutoMapper matches by property name convention — `GraphSnapshot` on the entity maps to `GraphSnapshot` on the DTO, using the already-configured `WorkflowGraphVO → WorkflowGraphDto` mapping.
- Using `WorkflowGraphDto?` (nullable) handles the edge case of legacy executions that might have null snapshots.

### Alternatives Considered
1. **Add explicit `.ForMember` mapping**: Rejected — unnecessary overhead; AutoMapper handles this automatically.
2. **Return a separate endpoint for graph data**: Rejected — violates the spec requirement that execution detail includes the graph; also adds unnecessary API calls.

---

## R3: Mock Agent execution strategy

### Question
How should mock agent mode be implemented? Where in the architecture should the mock behavior live?

### Research Findings

**Current state**:
- `IAgentResolver.ResolveAsync()` returns `ResolvedAgent(AIAgent Agent, LlmConfigVO? LlmConfig)`.
- `AgentResolverService` builds `IChatClient` from `OpenAIClient` → wraps in tool functions → creates `ChatClientAgent`.
- `WorkflowEngine.ExecuteAgentNodeAsync()` gets `IChatClient` from the resolved agent and calls `chatClient.GetResponseAsync()`.
- No mock/fallback mechanism exists in production code. Tests use `Moq` to create fake `IChatClient` instances.

**Key interface**: `IChatClient` from `Microsoft.Extensions.AI.Abstractions` — has method `GetResponseAsync(IEnumerable<ChatMessage>, ChatOptions?, CancellationToken)`.

### Decision
1. Create `MockChatClient : IChatClient` in Infrastructure/Services that returns a deterministic response containing the node name and input summary.
2. Modify `AgentResolverService.ResolveAsync()` to check for a "mock mode" configuration (via `IConfiguration` — e.g., `Workflow:MockAgentMode` setting or per-agent metadata). When mock mode is active or when no LLM provider is found for the agent, return an `AIAgent` wrapping `MockChatClient`.

### Rationale
- `MockChatClient` implements an existing interface (`IChatClient`) — no new interface needed (Constitution V satisfied).
- Placing it in Infrastructure is correct — it's an implementation of an external interface.
- Using `IConfiguration` for the global toggle is consistent with other feature flags in the project.
- Fallback behavior (no LLM configured → mock) provides graceful degradation for dev/test environments.

### Alternatives Considered
1. **Create a `MockAgentResolver : IAgentResolver`**: Rejected — requires DI registration switching and doesn't support mixed real/mock agents in the same workflow.
2. **Add mock behavior directly in `WorkflowEngine`**: Rejected — violates DDD (business logic in Infrastructure orchestrator); the resolution logic belongs in the resolver.
3. **Use environment variable only**: Rejected — less flexible; `IConfiguration` supports both env vars and appsettings.json.

---

## R4: Test strategy for the fixes

### Question
How should the new tests be structured to validate all three fixes?

### Research Findings

**Current test patterns** (from `WorkflowEngineTests.cs`):
- All 7 dependencies of `WorkflowEngine` are mocked with Moq.
- Helper method `Create3NodeSequentialExecution()` builds a 3-node agent workflow graph.
- Mock `IChatClient` wraps responses via `mockChatClient.Setup(c => c.GetResponseAsync(...)).ReturnsAsync(...)`.
- FluentAssertions used for assertions: `.Should().Be(...)`, `.Should().NotBeNull()`.
- xUnit `[Fact]` attributes, standard method naming.

### Decision
1. **Node input recording tests** (`NodeInputRecordingTests.cs` in Infrastructure.Tests/Workflows/):
   - Test sequential 3-node workflow — verify all `NodeExecutionVO.Input` fields populated.
   - Test condition node — verify condition node input recorded.
   - Test failed node — verify input still recorded on failure.
   - Test first node — verify workflow-level input is used.

2. **GraphSnapshot DTO tests** (`WorkflowExecutionDtoTests.cs` in Application.Tests/Workflows/):
   - Test AutoMapper mapping — verify `WorkflowExecutionDto.GraphSnapshot` is populated from entity.
   - Test null snapshot — verify graceful handling.

3. **Mock agent tests** (`MockAgentTests.cs` in Infrastructure.Tests/Workflows/):
   - Test `MockChatClient` returns response with node name and input summary.
   - Test workflow completes with mock agents end-to-end.
   - Test data flows correctly through mock agent chain.

4. **End-to-end smoke test** — in Infrastructure.Tests, uses mock agents to exercise complete lifecycle.

### Rationale
- Follows existing test convention and project organization.
- Tests are separated by concern (input recording, DTO mapping, mock mode) for independent verifiability per spec requirement.
- All tests use mock dependencies — no real database or LLM needed.

### Alternatives Considered
1. **Integration tests with real DB**: Rejected for this phase — adds complexity and external dependency. Can be added later.
2. **Single combined test class**: Rejected — harder to run independently and violates spec's "independently testable" requirement.

---

## R5: Backward compatibility of `StartNode` signature change

### Question
Will changing `StartNode(string nodeId)` to `StartNode(string nodeId, string? input)` break existing code?

### Research Findings

**Callers of `StartNode`**:
- `WorkflowEngine.cs` — 5 call sites (all must be updated)
- `WorkflowEngineTests.cs` — uses `execution.StartNode(...)` in test helpers

**Breaking change analysis**:
- Adding a `string? input` parameter with no default value is a **source-breaking change** — all callers must be updated.
- Adding it with a default value `string? input = null` is **non-breaking** — existing callers compile without changes, but they will silently not record input.

### Decision
Add the parameter **without** a default value: `StartNode(string nodeId, string? input)`. This forces all callers to explicitly pass input, making it impossible to accidentally forget the input.

### Rationale
- There are only 5+N call sites (N = tests). This is a small, manageable update.
- A mandatory parameter prevents future regressions — any new call site must consciously provide input.
- Per Constitution IV (Test Immutability), existing test assertions cannot be modified, but test **setup/helper methods** CAN be refactored — updating `StartNode` calls in test helpers is permitted since it doesn't change assertion semantics.

### Alternatives Considered
1. **Default value `= null`**: Rejected — risks silent omission of input data in future code paths.
2. **New overload**: Rejected — leaves the old overload callable, same silent-omission risk.
