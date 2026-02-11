# Research: 工作流定义 CRUD

**Feature**: 011-workflow-crud  
**Date**: 2026-02-11  

## R1: DAG Validation Algorithm

**Decision**: Kahn's algorithm (BFS-based topological sort)  
**Rationale**: Kahn's algorithm naturally detects cycles by checking if all nodes are visited after processing. It's O(V+E) complexity, straightforward to implement, and produces a clear error message when a cycle exists (remaining unvisited nodes form the cycle). For DAGs up to 100 nodes, performance is negligible.  
**Alternatives considered**:
- DFS with coloring (White/Gray/Black): Equally valid, slightly more complex to extract cycle path. Rejected for readability.
- External graph library: Unnecessary for simple cycle detection. No NuGet dependency needed.

**Implementation approach**: Pure domain logic in `WorkflowGraphVO` or a dedicated `DagValidator` domain service. No infrastructure dependencies.

## R2: JSONB Storage for WorkflowGraphVO

**Decision**: Use EF Core `OwnsOne` + `ToJson()` for the entire DAG graph, matching existing patterns (ToolSchemaVO, AgentCardVO).  
**Rationale**: The DAG graph is a value object — always read/written as a whole unit with the WorkflowDefinition aggregate root. JSONB storage preserves the graph structure naturally and enables PostgreSQL JSON queries if needed later.  
**Alternatives considered**:
- Normalized tables (separate `workflow_nodes` and `workflow_edges` tables): Over-engineered for this use case. Would complicate CRUD with multi-table joins. The graph is always loaded fully.
- Raw `JsonDocument` column: Loses type safety. `OwnsOne/ToJson()` provides typed access.

**Nested configuration pattern**: `WorkflowGraphVO` contains `List<WorkflowNodeVO>` and `List<WorkflowEdgeVO>`. EF Core `OwnsMany` within `OwnsOne.ToJson()` handles collections inside JSON. Enums inside JSON use `HasConversion<string>()`.

## R3: WorkflowDefinition Aggregate Root Pattern

**Decision**: Follow existing `ToolRegistration` / `AgentRegistration` patterns.  
**Rationale**: Maintain consistency. The pattern is:
1. Inherit `BaseEntity` (Guid Id, CreatedAt, UpdatedAt)
2. `private set` on all properties
3. `private WorkflowDefinition() { }` for EF Core
4. Static factory method `Create(name, description, graph)` with validation
5. Domain-level `Update(name, description, graph)` method with status guard (Draft only)
6. Status transition methods: `Publish()` and `Unpublish()` (Unpublish deferred to SPEC-026 but method stub added)

**Key invariants**:
- Name: non-empty, max 200 chars, globally unique (enforced at repository/DB level)
- Status: `Draft` at creation; only Draft allows Update/Delete
- Graph: validated via DAG validation before assignment

## R4: Repository Interface Design

**Decision**: `IWorkflowDefinitionRepository : IRepository<WorkflowDefinition>` with custom queries.  
**Rationale**: Matches `IToolRegistrationRepository` and `IAgentRegistrationRepository` patterns.

**Custom methods**:
- `GetByNameAsync(string name)` — for uniqueness checks
- `GetByStatusAsync(WorkflowStatus status)` — for filtering
- `ExistsWithNameAsync(string name, Guid? excludeId)` — for create/update uniqueness (avoids loading full entity)
- `IsReferencedByAgentAsync(Guid workflowId)` — for delete protection

## R5: CQRS Command/Query Design (MediatR)

**Decision**: Follow existing Application layer patterns.  
**Rationale**: Consistency with Agents and Tools feature areas.

**Commands**:
| Command | Request | Response | Validators |
|---------|---------|----------|------------|
| `CreateWorkflowCommand` | name, description?, graph | `Result<WorkflowDefinitionDto>` | name required/max200, graph not null, DAG validity |
| `UpdateWorkflowCommand` | id, name, description?, graph | `Result<WorkflowDefinitionDto>` | id valid, name required/max200, graph not null, DAG validity |
| `DeleteWorkflowCommand` | id | `Result<bool>` | id valid |

**Queries**:
| Query | Request | Response |
|-------|---------|----------|
| `GetWorkflowsQuery` | status? filter | `Result<List<WorkflowSummaryDto>>` |
| `GetWorkflowByIdQuery` | id | `Result<WorkflowDefinitionDto>` |

**Handler responsibilities**:
- CreateHandler: uniqueness check → DAG validation → reference validation (Agent/Tool IDs exist) → create entity → save
- UpdateHandler: fetch → status guard → uniqueness check → DAG validation → reference validation → update entity → save
- DeleteHandler: fetch → status guard → reference check (AgentRegistration.WorkflowRef) → delete → save

## R6: API Endpoint Design

**Decision**: Minimal API with `MapGroup("/api/workflows")`, following `ToolEndpoints.cs` pattern.  
**Rationale**: Consistency. `Result<T>.ErrorCode` maps directly to HTTP status codes.

**ErrorCode mapping**:
| ErrorCode | HTTP | Scenario |
|-----------|------|----------|
| 404 | `Results.NotFound(result)` | Workflow not found |
| 409 | `Results.Conflict(result)` | Name already exists |
| 400 | `Results.BadRequest(result)` | Validation errors, Published status guard, agent reference guard |
| - | `Results.Created(...)` | Successful POST |
| - | `Results.Ok(result)` | Successful GET/PUT |
| - | `Results.NoContent()` | Successful DELETE |

## R7: Test Project Structure

**Decision**: Use existing test projects. Add `Workflows/` folders.  
**Rationale**: Domain tests go in `CoreSRE.Infrastructure.Tests` (no separate Domain.Tests project exists — domain entity logic tested alongside repository tests, matching existing pattern). Application tests go in `CoreSRE.Application.Tests/Workflows/`.

**Test categories**:
1. **Domain tests** (entity invariants, DAG validation): In Infrastructure.Tests as domain unit tests — isolated, no DB needed
2. **Application tests** (command/query handler logic): In Application.Tests/Workflows/
3. **Infrastructure tests** (repository CRUD against real DB): In Infrastructure.Tests/Workflows/ — if integration test patterns exist

**Note**: The existing codebase places domain-adjacent tests in `CoreSRE.Infrastructure.Tests/Services/`. For this feature, handler tests belong in `Application.Tests`, and DAG validator tests can be in either project. Following the pattern of placing tests where they best fit.

## R8: Agent-Framework Workflow Type Mapping

**Decision**: CoreSRE `WorkflowDefinition` is a persistence model. Runtime conversion to `Microsoft.Agents.AI.Workflows.Workflow` is deferred to SPEC-021.  
**Rationale**: This spec is CRUD-only. The mapping between `WorkflowNodeType` and agent-framework types is:

| CoreSRE `WorkflowNodeType` | Framework Type | Notes |
|---|---|---|
| Agent | `ExecutorBinding` from `AIAgent` | Implicit conversion `AIAgent → ExecutorBinding` |
| Tool | `ExecutorBinding` wrapping `FunctionExecutor` | Custom executor calling Tool Gateway |
| Condition | Not a framework node type | Expressed via `AddEdge<T>(source, target, condition)` conditional edges |
| FanOut | Not a framework node type | Expressed via `AddFanOutEdge(source, targets)` edge pattern |
| FanIn | Not a framework node type | Expressed via `AddFanInEdge(sources, target)` edge pattern |

**Implication for SPEC-020**: Condition, FanOut, and FanIn are node types in our domain model but translate to edge patterns at runtime. This is correct — our DAG model is higher-level than the framework's builder API. During SPEC-021 implementation, a `WorkflowCompiler` service will translate our domain model to `WorkflowBuilder` API calls.

## R9: EF Core Configuration Pattern for WorkflowGraphVO

**Decision**: Separate `WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>` class.  
**Rationale**: Matches existing pattern (auto-discovered by `ApplyConfigurationsFromAssembly`).

**JSONB nesting**: `WorkflowGraphVO` contains `List<WorkflowNodeVO>` and `List<WorkflowEdgeVO>`. Configuration pattern:

```csharp
builder.OwnsOne(e => e.Graph, g =>
{
    g.ToJson("graph");
    g.OwnsMany(x => x.Nodes, n =>
    {
        n.Property(p => p.NodeType).HasConversion<string>();
    });
    g.OwnsMany(x => x.Edges, e =>
    {
        e.Property(p => p.EdgeType).HasConversion<string>();
    });
});
```

Enum-to-string conversion inside JSONB via `HasConversion<string>()`.
