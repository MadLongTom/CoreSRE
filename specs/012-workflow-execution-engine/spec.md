# Feature Specification: 工作流执行引擎（顺序 + 并行 + 条件分支）

**Feature Branch**: `012-workflow-execution-engine`  
**Created**: 2026-02-11  
**Status**: Draft  
**Input**: User description: "SPEC-021: 工作流执行引擎 — 将 WorkflowDefinition 的 JSON DAG 转换为 Agent Framework 的 Workflow 对象并执行。支持顺序、并行、条件分支三种基础编排模式。每次执行创建 WorkflowExecution 记录，实时更新各节点执行状态。"

## User Scenarios & Testing *(mandatory)*

### User Story 1 — 执行顺序编排工作流 (Priority: P1)

运维工程师有一个已发布的工作流定义，包含 3 个 Agent 节点按顺序串联（A → B → C）。工程师通过 API 提交执行请求，附带初始输入数据。系统将 DAG 定义转换为可执行的工作流对象，按顺序执行各节点：先调用 Agent A 处理输入，将 A 的输出作为 B 的输入，再将 B 的输出传递给 C。整个过程中，系统创建一条 `WorkflowExecution` 记录并实时更新每个节点的执行状态（Pending → Running → Completed）。执行完成后记录最终输出。

**Why this priority**: 顺序执行是最基础的编排模式，也是所有其他模式的基础。没有顺序执行能力，其他编排模式无从谈起。此 Story 同时验证了核心的 DAG-to-Workflow 转换逻辑和执行状态追踪机制。

**Independent Test**: 创建一个 3 节点顺序 DAG 的 Published 工作流，通过 `POST /api/workflows/{id}/execute` 提交执行，验证返回的 WorkflowExecution 记录包含正确的节点执行序列和最终输出。

**Acceptance Scenarios**:

1. **Given** 一个 Published 状态的工作流定义包含 A → B → C 三个 Agent 节点的顺序 DAG，**When** 工程师提交执行请求并附带 JSON 输入 `{"query": "检查服务状态"}`，**Then** 系统创建一条 WorkflowExecution 记录，状态为 Running，每个节点按顺序执行；执行完成后整体状态变为 Completed，记录最终输出数据。
2. **Given** 一个 Published 状态的顺序工作流正在执行中，**When** 工程师查询该执行记录的状态，**Then** 可以看到每个节点各自的执行状态（Pending/Running/Completed/Failed）、输入数据、输出数据和耗时。
3. **Given** 顺序工作流中的第 2 个节点执行 Agent 调用失败（如 Agent 不可用），**When** 系统检测到节点失败，**Then** 该节点状态标记为 Failed，整体工作流状态标记为 Failed，后续节点不再执行，错误信息记录在节点执行记录中。
4. **Given** 工程师提交执行请求的工作流 ID 不存在或工作流状态为 Draft，**When** 系统处理请求，**Then** 返回错误提示——不存在返回 404，Draft 状态返回 400 并提示"仅已发布的工作流可执行"。

---

### User Story 2 — 执行并行编排工作流（FanOut/FanIn）(Priority: P1)

运维工程师有一个工作流，其中某个节点之后需要同时并发调用多个 Agent 或 Tool（FanOut），待所有并发节点完成后，由聚合节点（FanIn）将结果汇总。例如：告警触发后，同时向日志查询 Agent、指标查询 Agent、配置查询 Agent 发起并行查询，然后由汇总 Agent 综合分析结果。系统识别 FanOut/FanIn 节点类型后，并行调度下游节点，等待全部完成后将输出数组传递给 FanIn 节点。

**Why this priority**: 并行执行是显著提升工作流处理效率的核心能力，AIOps 场景中大量存在并行数据采集需求。必须在 MVP 中支持。

**Independent Test**: 创建一个包含 FanOut → 3 个并行 Agent 节点 → FanIn 的 Published 工作流，执行后验证 3 个 Agent 并发执行（执行时间接近最慢节点而非三者之和），FanIn 节点接收到所有并行节点的输出。

**Acceptance Scenarios**:

1. **Given** 一个 Published 工作流包含 Start → FanOut → (Agent1, Agent2, Agent3) → FanIn → End 的 DAG，**When** 工程师提交执行请求，**Then** FanOut 节点完成后同时启动 Agent1、Agent2、Agent3 的执行，三者的状态同时变为 Running。
2. **Given** 并行执行中，Agent1 和 Agent2 已完成但 Agent3 仍在运行，**When** 工程师查询执行状态，**Then** Agent1 和 Agent2 显示 Completed，Agent3 显示 Running，FanIn 节点仍为 Pending。
3. **Given** 所有并行节点均已完成，**When** FanIn 节点开始执行，**Then** FanIn 节点的输入为所有并行节点输出的聚合数组，FanIn 节点处理完成后将聚合结果传递给下游节点。
4. **Given** 并行执行中某一个节点失败，**When** 系统检测到节点失败，**Then** 等待其余并行节点完成（不中断已启动的并行任务），整体工作流状态标记为 Failed，FanIn 节点记录部分失败信息。

---

### User Story 3 — 执行条件分支工作流 (Priority: P1)

运维工程师有一个工作流，在某个节点之后需要根据该节点的输出结果选择不同的下游路径。例如：告警分类 Agent 输出告警严重程度后，高严重度走紧急处理路径，低严重度走常规处理路径。系统根据 Conditional Edge 上配置的条件表达式，对上一步输出进行匹配，选择满足条件的下游节点执行。

**Why this priority**: 条件分支是动态决策的基础，使工作流能根据运行时上下文做出智能路由，是区分静态脚本和智能工作流的核心能力。

**Independent Test**: 创建一个包含 Condition 节点和两条 Conditional Edge 的 Published 工作流，分别用不同输入触发不同分支，验证系统正确路由到对应的下游节点。

**Acceptance Scenarios**:

1. **Given** 一个 Published 工作流包含 Agent-A → Condition → (高严重度路径: Agent-B, 低严重度路径: Agent-C) 的 DAG，Conditional Edge 条件为 `$.severity == "high"` 和 `$.severity == "low"`，**When** Agent-A 输出 `{"severity": "high"}`，**Then** 系统路由到 Agent-B 执行，Agent-C 的节点状态标记为 Skipped。
2. **Given** 同样的条件分支工作流，**When** Agent-A 输出 `{"severity": "low"}`，**Then** 系统路由到 Agent-C 执行，Agent-B 标记为 Skipped。
3. **Given** 条件分支工作流中所有 Conditional Edge 的条件均不满足（如输出不包含 severity 字段），**When** 系统评估条件，**Then** 工作流状态标记为 Failed，错误信息为"无匹配的条件分支"。

---

### User Story 4 — 查询工作流执行记录列表与详情 (Priority: P2)

运维工程师需要查看某个工作流定义的所有历史执行记录，包括每次执行的状态、开始时间、完成时间和输入输出摘要。并可查看某次具体执行的详情，包括每个节点的执行信息。

**Why this priority**: 执行记录的查询是回溯分析和排查问题的基础能力，但不影响核心执行功能的交付。

**Independent Test**: 对一个工作流执行多次后，通过列表查询 API 获取所有执行记录，通过详情 API 获取指定执行的完整节点执行信息。

**Acceptance Scenarios**:

1. **Given** 某个工作流已被执行 3 次，**When** 工程师查询该工作流的执行记录列表（`GET /api/workflows/{id}/executions`），**Then** 返回 3 条记录，每条包含执行 ID、状态、开始时间、完成时间。
2. **Given** 某次执行包含 5 个节点的工作流，**When** 工程师查询该执行详情（`GET /api/workflows/{id}/executions/{execId}`），**Then** 返回完整的执行信息，包括每个节点的节点 ID、状态、输入、输出、开始时间和完成时间。
3. **Given** 工程师查询一个不存在的执行 ID，**When** 系统处理请求，**Then** 返回 404 Not Found。

---

### Edge Cases

- **空输入执行**: 工程师未提交任何输入数据或提交空 JSON `{}`，系统应接受空输入并将空 JSON 传递给第一个节点。
- **节点超时**: 某个 Agent 节点长时间未响应（超过配置的超时阈值，默认 5 分钟），系统应将该节点标记为 Failed（超时），工作流状态标记为 Failed。
- **并行节点部分超时**: FanOut 后的并行节点中，部分节点超时但其余正常完成，系统应等待所有节点完成或超时后，整体标记为 Failed。
- **引用的 Agent/Tool 已不存在**: 工作流定义时引用的 Agent 或 Tool 在执行时已被删除，系统应在执行前校验引用有效性，无效时返回错误且不启动执行。
- **并发执行同一工作流**: 多个用户同时对同一工作流发起执行请求，系统应为每次请求创建独立的 WorkflowExecution 记录，互不干扰。
- **条件表达式语法错误**: Conditional Edge 的条件表达式无法解析（如 JSON Path 语法错误），系统应将该节点标记为 Failed 并记录解析错误。
- **FanIn 无上游节点完成**: FanOut 后所有并行节点均失败，FanIn 节点接收空聚合结果。
- **工作流定义在执行中被修改**: 工作流执行过程中，底层 WorkflowDefinition 被更新或取消发布，不影响已启动的执行实例（执行时使用的是启动时的图快照）。

## Requirements *(mandatory)*

### Functional Requirements

**领域模型 — 执行实体**

- **FR-001**: 系统 MUST 定义 `WorkflowExecution` 聚合根实体，包含关联的 WorkflowDefinitionId、执行状态（`ExecutionStatus`）、输入数据（JSON）、输出数据（JSON，可选）、执行开始时间、完成时间、节点执行列表（`List<NodeExecutionVO>`）和 TraceId（OpenTelemetry 追踪标识）。
- **FR-002**: 系统 MUST 定义 `NodeExecutionVO` 值对象，包含节点 ID（对应 DAG 中的 NodeId）、节点执行状态（`NodeExecutionStatus`）、输入数据（JSON）、输出数据（JSON，可选）、错误信息（可选）、开始时间和完成时间。
- **FR-003**: 系统 MUST 定义 `ExecutionStatus` 枚举：Pending（等待启动）、Running（执行中）、Completed（成功完成）、Failed（失败）、Canceled（已取消）。
- **FR-004**: 系统 MUST 定义 `NodeExecutionStatus` 枚举：Pending（等待执行）、Running（执行中）、Completed（成功完成）、Failed（失败）、Skipped（被跳过，条件分支未命中时）。

**DAG 转换与执行引擎**

- **FR-005**: 系统 MUST 提供 DAG-to-Workflow 转换能力，将 `WorkflowGraphVO` 中的节点和边定义转换为可执行的工作流对象。转换时针对 Agent 节点解析对应的 Agent 配置，针对 Tool 节点解析对应的 Tool 配置。
- **FR-006**: 系统 MUST 支持顺序（Sequential）编排模式——当 DAG 图为线性链路（每个节点仅有一条出边和一条入边）时，按拓扑序逐个执行节点，前一节点的输出作为后一节点的输入。
- **FR-007**: 系统 MUST 支持并行（FanOut/FanIn）编排模式——当遇到 FanOut 节点时，同时启动所有下游节点的并发执行；当下游节点全部完成后，FanIn 节点接收所有并行结果的聚合数组作为输入。
- **FR-008**: 系统 MUST 支持条件分支（Conditional Edge）编排模式——当遇到 Condition 节点或具有 Conditional Edge 的节点时，评估每条 Conditional Edge 的条件表达式，仅路由到条件匹配的下游节点，不匹配的节点标记为 Skipped。
- **FR-009**: 系统 MUST 在条件分支中，当所有 Conditional Edge 均不匹配时，将工作流标记为 Failed 并记录"无匹配的条件分支"错误。
- **FR-010**: 系统 MUST 在执行过程中实时更新每个节点的执行状态（Pending → Running → Completed/Failed/Skipped），并持久化到 WorkflowExecution 记录。

**执行状态管理**

- **FR-011**: 系统 MUST 在启动执行时创建 WorkflowExecution 记录，初始状态为 Pending，初始化所有节点的 NodeExecutionVO 为 Pending 状态。
- **FR-012**: 系统 MUST 在所有节点执行完成（无失败）后，将工作流执行状态更新为 Completed，记录最终输出和完成时间。
- **FR-013**: 系统 MUST 在任意节点执行失败后，将工作流执行状态更新为 Failed，记录错误信息。对于顺序执行，后续节点不再执行；对于并行执行，等待已启动的并行节点完成后再标记整体失败。
- **FR-014**: 系统 MUST 在执行前验证 WorkflowDefinition 的状态为 Published，Draft 状态的工作流返回 400 错误。
- **FR-015**: 系统 MUST 在执行前验证工作流中 Agent 和 Tool 节点引用的外部资源仍然存在，不存在则返回 400 错误并指明具体缺失的引用。

**条件表达式**

- **FR-016**: 系统 MUST 支持基于 JSON Path 的条件表达式求值。条件表达式从上一节点的输出 JSON 中提取值并与预期值比较。表达式格式为 `<jsonPath> == <expectedValue>`（如 `$.severity == "high"`）。
- **FR-017**: 系统 MUST 在条件表达式解析失败或求值异常时，将关联节点标记为 Failed 并记录错误详情。

**API 端点**

- **FR-018**: 系统 MUST 提供启动工作流执行的端点（`POST /api/workflows/{id}/execute`），接受 JSON 输入数据，返回创建的 WorkflowExecution 记录（含执行 ID 和初始状态）。工作流不存在返回 404，Draft 状态返回 400。
- **FR-019**: 系统 MUST 提供查询工作流执行列表的端点（`GET /api/workflows/{id}/executions`），返回指定工作流的所有执行记录摘要（执行 ID、状态、开始时间、完成时间）。工作流不存在返回 404。
- **FR-020**: 系统 MUST 提供查询执行详情的端点（`GET /api/workflows/{id}/executions/{execId}`），返回完整的执行记录，包含所有节点执行信息。不存在返回 404。

**仓储层**

- **FR-021**: 系统 MUST 提供 `IWorkflowExecutionRepository` 接口，继承 `IRepository<WorkflowExecution>`，并扩展按 WorkflowDefinitionId 查询（`GetByWorkflowIdAsync`）和按状态过滤（`GetByStatusAsync`）的能力。

**节点超时**

- **FR-022**: 系统 MUST 为每个节点执行施加可配置的超时限制（默认 5 分钟），超时后将节点标记为 Failed（错误信息为"节点执行超时"），工作流标记为 Failed。

**图快照**

- **FR-023**: 系统 MUST 在启动执行时将当前 WorkflowDefinition 的 DAG 图快照保存到 WorkflowExecution 记录中，确保执行过程中定义变更不影响正在运行的实例。

### Key Entities

- **WorkflowExecution（聚合根）**: 工作流的一次执行实例。继承 `BaseEntity`（Guid ID, CreatedAt, UpdatedAt）。关联一个 WorkflowDefinitionId。包含执行状态生命周期（Pending → Running → Completed/Failed/Canceled）、输入输出数据（JSON）、节点执行列表和 OpenTelemetry TraceId。保存启动时的 DAG 图快照，不受后续定义修改影响。
- **NodeExecutionVO（值对象）**: 工作流执行中单个节点的执行记录。绑定到 DAG 图中的 NodeId，记录该节点的执行状态、输入输出数据、错误信息和时间戳。作为 WorkflowExecution 的嵌套值对象以 JSONB 形式存储。
- **ExecutionStatus（枚举）**: 工作流执行状态——Pending、Running、Completed、Failed、Canceled。
- **NodeExecutionStatus（枚举）**: 节点执行状态——Pending、Running、Completed、Failed、Skipped。
- **WorkflowDefinition（已有聚合根，SPEC-020）**: 工作流定义。执行引擎通过 WorkflowDefinitionId 获取 DAG 图并转换为可执行对象。仅 Published 状态的定义可被执行。

## Assumptions

- 工作流执行为异步操作。`POST /api/workflows/{id}/execute` 端点在创建 WorkflowExecution 记录后立即返回 202 Accepted，执行在后台进行。客户端通过查询执行详情端点轮询执行状态。
- 条件表达式采用简单的 JSON Path 比较格式（`<jsonPath> == <expectedValue>`），语法足够覆盖 80% 的条件路由场景（基于字段值的等值比较）。后续迭代可扩展为更复杂的表达式语法。
- FanIn 节点的聚合逻辑为将所有并行节点的输出收集为 JSON 数组，不执行语义级别的合并。如需智能汇总，FanIn 节点可引用一个 Agent 来处理聚合数组。
- Agent 节点的执行通过 Agent Framework 的 `IChatClient`/`AIAgent.RunAsync()` 调用。Tool 节点的执行通过 Tool Gateway 的统一调用端点（SPEC-013 范畴，本 Spec 中 Tool 节点暂以桩函数实现）。
- 每次工作流执行相互独立，不共享上下文。同一工作流可同时拥有多个并发执行实例。
- 节点超时默认 5 分钟，后续可通过节点 Config 字段配置单节点超时时间。
- 本 Spec 不包含暂停/恢复/取消正在运行的执行（属于 SPEC-024 范畴），但 ExecutionStatus 枚举中预留了 Canceled 状态以便未来扩展。
- DAG 图快照以 JSON 形式存储在 WorkflowExecution 中，与 WorkflowDefinition 的 Graph 字段结构相同，使用同一 `WorkflowGraphVO` 类型。
- OpenTelemetry TraceId 在本 Spec 中仅记录字段占位，实际追踪集成属于 SPEC-040 范畴。

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 工程师可在 5 秒内通过 API 提交一个包含 5 个顺序节点的工作流执行请求，系统返回执行 ID 和初始状态，后台开始异步执行。
- **SC-002**: 一个包含 3 个并行 Agent 节点的 FanOut/FanIn 工作流，总执行时间接近最慢单节点的执行时间（而非三者耗时之和），验证并行调度有效。
- **SC-003**: 条件分支工作流能根据上游节点输出正确路由到匹配的下游节点，100% 的条件匹配场景产生正确路由结果。
- **SC-004**: 工作流执行过程中每个节点状态变更在 1 秒内持久化到数据库，客户端通过查询端点可实时获取最新进度。
- **SC-005**: 执行记录列表查询在 100 条执行记录下响应时间不超过 1 秒。
- **SC-006**: 节点执行失败时，错误信息明确指出失败原因（Agent 不可用、超时、条件不匹配等），工程师可在无额外日志的情况下初步定位问题。
