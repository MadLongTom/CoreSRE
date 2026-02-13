# Research: 工作流数据流模型与执行栈引擎

**Feature**: 016-workflow-dataflow-engine
**Date**: 2026-02-13
**Status**: Complete

## R1: Structured Data Model — Items vs Raw String

### Decision
Replace `string? lastOutput` linear chain with structured `WorkflowItemVO[]` items organized by port via `NodeInputData` / `NodeOutputData` value objects.

### Rationale
- **n8n precedent**: n8n's `INodeExecutionData[]` model (items with `{json, binary, pairedItem}`) has proven effective for complex workflows. CoreSRE adopts the JSON-only subset (`WorkflowItemVO` with `JsonElement Json` + `ItemSourceVO? Source`).
- **Port organization**: `NodeInputData.Ports["main"][portIndex]` = `PortDataVO` containing `List<WorkflowItemVO>` items. This naturally models multi-port I/O.
- **Backward compatibility**: When wrapping existing `string?` agent/tool output into the new model, create a single `WorkflowItemVO` with the output string parsed as `JsonElement` on port 0. Existing NodeExecutionVO.Input/Output fields remain `string?` — the engine serializes `NodeInputData`/`NodeOutputData` to JSON strings for recording.

### Alternatives Considered
1. **Keep string? with JSON envelope** — wrap output in `{"port":0,"items":[...]}` JSON string. Rejected: no type safety, parsing overhead on every node, no compile-time port access.
2. **Use `Dictionary<string, JsonElement>` per node** — simpler but loses item-level lineage tracking and port organization. Rejected: insufficient for multi-item batch processing.
3. **Full n8n model with binary data** — overkill for current scope. Binary data support deferred to future phase.

---

## R2: Execution Model — Stack vs Topological Sort

### Decision
Replace `TopologicalSort()` + linear `foreach` with an execution stack (`LinkedList<NodeExecutionTask>`, LIFO) + waiting queue (`Dictionary<string, WaitingNodeData>`).

### Rationale
- **Dynamic routing**: Topological sort computes a fixed order at start — it cannot adapt when conditions skip branches or when multi-input nodes need to wait. The stack model pushes nodes only when their data is actually available.
- **Natural FanOut/FanIn**: Multi-output nodes push multiple tasks onto the stack (one per downstream edge). Multi-input nodes go into the waiting queue until all ports are satisfied. This eliminates the need for special-case `ExecuteFanOutGroupAsync` and `ExecuteFanInAsync` methods.
- **Condition routing**: Condition nodes output data on the matching port only → only the edges from that port push downstream nodes. Unmatched branches are never pushed, so they remain Pending (not explicitly Skipped — unless we add a post-execution cleanup pass).

### Alternatives Considered
1. **Keep topological sort + per-node input resolution from edges** — hybrid approach. Would fix the `lastOutput` problem but not enable dynamic routing or proper multi-input waiting. Rejected: half-measure that doesn't address D2.
2. **Event-driven model with async message passing** — each node completion publishes an event, subscribers resolve their inputs. Rejected: overengineered for in-process single-workflow execution.
3. **Parallel stack (BFS instead of DFS)** — use a queue instead of stack. Rejected: DFS (stack) matches n8n's behavior and produces more intuitive execution traces for debugging.

### Key Design Decision: Skipped Node Handling
When a condition node outputs data only on port 0 (true branch), nodes connected to port 1 (false branch) are never pushed to the stack. They remain in `Pending` status after execution completes. This is acceptable behavior — the execution record shows which nodes were reached. A post-execution pass can optionally mark unreached nodes as `Skipped` for clearer reporting.

**Decision**: Add a post-execution cleanup pass that marks all still-`Pending` nodes as `Skipped` after the main loop completes. This maintains backward compatibility with the old engine's explicit skip behavior.

---

## R3: WorkflowNodeVO Port Defaults and Backward Compatibility

### Decision
Add `InputCount` and `OutputCount` as `int` properties with default values of `1` to `WorkflowNodeVO`. Add `SourcePortIndex` and `TargetPortIndex` as `int` properties with default values of `0` to `WorkflowEdgeVO`.

### Rationale
- **Zero-migration compatibility**: EF Core JSONB deserialization with `System.Text.Json` assigns default C# values to missing properties. `int` defaults to `0`, but we need `InputCount` and `OutputCount` to default to `1`. Using `{ get; init; } = 1` ensures the correct default.
- **JSONB transparency**: Existing JSONB data in PostgreSQL lacks these fields. When EF Core deserializes, the `init` default provides the correct value. When new data is written, the fields are included. No migration needed.
- **Port index defaults**: `SourcePortIndex = 0` and `TargetPortIndex = 0` matches existing behavior where all edges implicitly connect the single output to the single input.

### Alternatives Considered
1. **Use `int?` nullable fields** — `null` means "legacy, treat as default". Rejected: adds null-checking overhead throughout the engine and in validation logic. Non-nullable with defaults is cleaner.
2. **SQL migration to backfill existing rows** — explicitly set defaults in persisted data. Rejected: unnecessary risk and complexity; C# defaults handle deserialization correctly.

### Verification
- **Test**: Deserialize existing JSONB node data without `InputCount`/`OutputCount` fields → confirm they default to 1.
- **Test**: Serialize a node with explicit `InputCount=2` → confirm it appears in JSONB.
- **Test**: Existing `WorkflowGraphVO.Validate()` tests continue to pass (no new validation is triggered for legacy graphs).

---

## R4: NodeExecutionVO.Input/Output — Keep as String or Migrate to Structured?

### Decision
Keep `NodeExecutionVO.Input` and `Output` as `string?`. The engine serializes `NodeInputData`/`NodeOutputData` to JSON strings before calling `execution.StartNode(nodeId, serializedInput)` and `execution.CompleteNode(nodeId, serializedOutput)`.

### Rationale
- **Minimal domain change**: `NodeExecutionVO` is a persisted value object stored in JSONB. Changing its type would require migration or break deserialization of existing execution records.
- **API compatibility**: The `NodeExecutionDto.Input/Output` fields are `string?` — API consumers parse JSON strings already. Structured data is embedded within the string.
- **Traceability**: The serialized JSON string contains the full `NodeInputData`/`NodeOutputData` structure — items, ports, sources. It's self-describing.

### Alternatives Considered
1. **Change NodeExecutionVO.Input/Output to JsonElement** — more type-safe but breaks backward compat with existing persisted records that contain raw strings. Rejected.
2. **Add new fields (StructuredInput/StructuredOutput) alongside old fields** — preserves old data but doubles storage and complicates mapping. Rejected: over-complicated for the benefit.

---

## R5: ExecutionContext — Domain VO or Infrastructure-Internal?

### Decision
`ExecutionContext` is an **infrastructure-internal runtime class** in `CoreSRE.Infrastructure/Services/`, not a domain value object. It holds the execution stack, waiting queue, run data, and connection maps — all of which are transient execution state not persisted.

### Rationale
- **DDD layer rule**: Domain layer contains only business entities, value objects, and interfaces. `ExecutionContext` is a runtime execution artifact that manages the engine's internal bookkeeping — it belongs in Infrastructure alongside `WorkflowEngine`.
- **Immutability exception**: Unlike domain VOs which must be immutable (Constitution III), `ExecutionContext` is mutable by design — the stack and waiting queue change as execution progresses. Placing it in Domain would violate the immutability principle.
- **Node-level types in Domain**: `NodeExecutionTask`, `WaitingNodeData`, `NodeRunResult` are also infrastructure-internal types used only by the engine. However, `NodeInputData`, `NodeOutputData`, `PortDataVO`, `WorkflowItemVO`, `ItemSourceVO` ARE domain value objects because they define the data model that flows between nodes and is persisted in execution records.

### Alternatives Considered
1. **Put everything in Domain** — makes all types available but pollutes the domain with execution-engine internals. Rejected per DDD principle.
2. **Create a separate `CoreSRE.WorkflowEngine` project** — too much structure for a single service class. Rejected per complexity tracking.

### Final Classification

| Type | Layer | Reason |
|------|-------|--------|
| `WorkflowItemVO` | Domain/ValueObjects | Fundamental data unit, persisted in execution records |
| `ItemSourceVO` | Domain/ValueObjects | Lineage tracking, embedded in `WorkflowItemVO` |
| `PortDataVO` | Domain/ValueObjects | Port data container, persisted in execution records |
| `NodeInputData` | Domain/ValueObjects | Node input contract, serialized to `NodeExecutionVO.Input` |
| `NodeOutputData` | Domain/ValueObjects | Node output contract, serialized to `NodeExecutionVO.Output` |
| `ExecutionContext` | Infrastructure/Services | Transient engine state, mutable, not persisted |
| `NodeExecutionTask` | Infrastructure/Services | Stack entry, transient |
| `WaitingNodeData` | Infrastructure/Services | Wait queue entry, transient |
| `NodeRunResult` | Infrastructure/Services | Per-run result tracking, transient |

---

## R6: FanOut/FanIn Node Types — Keep or Remove?

### Decision
**Keep** `FanOut` and `FanIn` as node types in `WorkflowNodeType` enum. Their behavior is now expressed through the port model:
- **FanOut**: `InputCount=1`, `OutputCount=N` (N = number of downstream branches). The engine copies input items to all output ports.
- **FanIn**: `InputCount=N` (N = number of upstream branches), `OutputCount=1`. The engine uses the waiting queue to collect data on all input ports before executing.

### Rationale
- **Backward compatibility**: Existing workflows use `FanOut` and `FanIn` node types. Removing them breaks deserialization. The enum values must remain.
- **Semantic clarity**: The node types convey intent (fan-out = broadcast, fan-in = merge). Without them, users must manually set `OutputCount`/`InputCount` and connect edges — the type provides a helpful default.
- **Default port counts**: When `NodeType == FanOut`, the engine can auto-compute `OutputCount` from the number of outgoing edges if not explicitly set. Similarly for `FanIn` with incoming edges.

### Alternatives Considered
1. **Remove FanOut/FanIn types** — all branching/merging expressed purely through port counts. Rejected: breaks backward compat and loses semantic information.
2. **Add new generic types (Splitter/Merger)** — keep old types for compat, add new ones. Rejected: unnecessary complexity; the existing types serve the purpose.

---

## R7: Parallel Execution Within FanOut Branches

### Decision
The stack-based engine processes nodes **sequentially** (DFS). FanOut nodes push multiple downstream tasks onto the stack, which are then executed one-by-one in stack order. True parallel execution (`Task.WhenAll`) is **not** used in the new model.

### Rationale
- **Simplicity**: Sequential stack execution is deterministic and easy to debug. The existing `Task.WhenAll` parallel execution creates race conditions with entity state mutations (the current code serializes state updates after parallel completion — a fragile pattern).
- **n8n precedent**: n8n's core execution is single-threaded with an execution stack. Parallelism is handled at a higher level (separate workflow executions, not within a single execution).
- **Correctness first**: The primary goal of this feature is correct data flow, not performance. Parallel execution within a single workflow can be re-introduced later as an optimization.

### Alternatives Considered
1. **Keep Task.WhenAll for FanOut paths** — higher throughput but requires careful thread-safety for entity mutations and execution context access. Rejected: correctness over performance in this phase.
2. **Use channels/producers for parallelism** — over-engineered for in-process execution. Rejected.

### Impact on Existing Tests
`ExecuteAsync_FanOutFanIn_DispatchesToAllParallelNodes` — this test verifies that all parallel nodes execute. Sequential execution still completes all of them, just not simultaneously. The test should still pass as it asserts on completion states, not on parallelism timing.

---

## R8: Graph Validation — New Port Index Rules

### Decision
Add two new validation rules to `WorkflowGraphVO.Validate()`:
1. **Port index bounds**: For each edge, `SourcePortIndex < sourceNode.OutputCount` and `TargetPortIndex < targetNode.InputCount`. Violation = Error.
2. **Condition node output count**: If a node has `NodeType == Condition`, validate `OutputCount >= 2`. Violation = Error.

### Rationale
- Port index out-of-range would cause silent data loss (output placed on port 2 but edge connects from port 3 → data never propagated). Early validation prevents this.
- Condition nodes must have at least 2 output ports (true/false). A Condition with `OutputCount=1` cannot route conditionally.

### Alternatives Considered
1. **Runtime validation only** — check port bounds during execution. Rejected: spec mandates pre-execution validation (FR-021, FR-022).
2. **Contiguous port checking** — validate that port indices form a contiguous range 0..N. Rejected: too restrictive; sparse port usage should be allowed.

### Backward Compatibility
Existing graphs have no `InputCount`/`OutputCount` (default to 1) and no `SourcePortIndex`/`TargetPortIndex` (default to 0). Validation: `0 < 1` → pass. No existing graphs will fail the new validation rules.
