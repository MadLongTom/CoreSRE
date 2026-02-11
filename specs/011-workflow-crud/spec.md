# Feature Specification: 工作流定义 CRUD

**Feature Branch**: `011-workflow-crud`  
**Created**: 2026-02-12  
**Status**: Draft  
**Input**: User description: "SPEC-020: 工作流定义 CRUD - Create, query, update, delete workflow definitions with DAG graph (nodes and edges) in JSON format"

## User Scenarios & Testing *(mandatory)*

### User Story 1 — 创建工作流定义（DAG 图构建）(Priority: P1)

平台管理员需要创建一个新的工作流定义，描述一个有向无环图（DAG）来编排多个 Agent 和 Tool 的协作流程。管理员提交工作流名称、描述以及完整的 DAG 图定义——包括节点列表（各节点引用已注册的 Agent 或 Tool，或声明逻辑节点类型如条件分支、并行分发、聚合汇总）和边列表（描述节点间的连接关系与条件）。系统验证 DAG 有效性后持久化工作流定义。

**Why this priority**: 创建工作流是整个 Workflow Engine 的起点。没有工作流定义，后续的执行、发布为 WorkflowAgent 等功能都无法进行。

**Independent Test**: 可独立验证——通过 API 提交一个包含若干节点和边的 DAG 定义，成功返回工作流 ID 和完整定义，数据库中可查询到该记录。

**Acceptance Scenarios**:

1. **Given** 系统中已注册若干 Agent 和 Tool，**When** 管理员提交一个包含 3 个 Agent 节点和 2 条边的顺序工作流定义（含名称、描述、DAG 图），**Then** 系统校验 DAG 有效性后创建工作流定义，返回完整定义（含系统生成的 ID 和时间戳），状态为 Draft。
2. **Given** 管理员提交的 DAG 图包含环路（如 A → B → C → A），**When** 系统进行 DAG 有效性校验，**Then** 系统拒绝创建并返回明确的错误提示："工作流图包含环路，必须为有向无环图"。
3. **Given** 管理员提交的 DAG 图包含未连接的孤立节点，**When** 系统进行连通性校验，**Then** 系统拒绝创建并返回错误提示："工作流图包含未连接的孤立节点"。
4. **Given** 管理员提交的 DAG 图中某个 Agent 节点引用了不存在的 Agent ID，**When** 系统进行引用校验，**Then** 系统拒绝创建并返回错误提示："节点引用的 Agent 不存在"。
5. **Given** 管理员提交工作流定义时未提供名称或 DAG 图为空，**When** 系统进行基础字段校验，**Then** 系统拒绝创建并返回相应的验证错误信息。

---

### User Story 2 — 查询工作流定义列表与详情 (Priority: P1)

管理员需要浏览系统中所有已创建的工作流定义，查看各工作流的名称、描述、状态、节点数量等摘要信息。并可根据工作流 ID 查看某个工作流的完整详情，包括其 DAG 图的全部节点和边定义。

**Why this priority**: 查询是 CRUD 的基础能力，管理员需要快速定位和查看工作流定义，为编辑、删除和后续执行提供入口。

**Independent Test**: 可独立验证——创建若干工作流后，通过列表查询 API 获取所有工作流摘要，通过详情 API 获取指定工作流的完整 DAG 定义。

**Acceptance Scenarios**:

1. **Given** 系统中已创建 5 个工作流定义，**When** 管理员查询工作流列表，**Then** 返回 5 条记录，每条包含工作流 ID、名称、描述、状态、节点数量和创建时间。
2. **Given** 系统中已创建一个包含 4 节点 3 边的工作流定义，**When** 管理员通过 ID 查询该工作流详情，**Then** 返回完整的工作流定义，包括所有节点（含类型、引用 ID、配置）和所有边（含源节点、目标节点、条件）。
3. **Given** 管理员查询一个不存在的工作流 ID，**When** 系统处理请求，**Then** 返回 404 Not Found 错误。

---

### User Story 3 — 更新工作流定义 (Priority: P1)

管理员需要修改已有的工作流定义，包括更新名称、描述或调整 DAG 图（增删改节点和边）。只有处于 Draft 状态的工作流可以编辑；已发布（Published）的工作流需先取消发布后才能修改。

**Why this priority**: 工作流定义通常需要反复调整和迭代，更新能力是基本的管理需求。

**Independent Test**: 可独立验证——创建一个 Draft 工作流，通过更新 API 修改其名称和 DAG 图，再查询详情确认变更已生效。

**Acceptance Scenarios**:

1. **Given** 存在一个 Draft 状态的工作流定义，**When** 管理员更新其名称和描述，**Then** 系统保存更新后的名称和描述，UpdatedAt 时间戳更新。
2. **Given** 存在一个 Draft 状态的工作流定义，**When** 管理员更新其 DAG 图（新增一个节点和相应的边），**Then** 系统对新图进行 DAG 有效性校验，通过后保存更新后的完整图定义。
3. **Given** 管理员更新工作流的 DAG 图引入了环路，**When** 系统进行校验，**Then** 系统拒绝更新并返回环路错误提示，原定义不受影响。
4. **Given** 存在一个 Published 状态的工作流定义，**When** 管理员尝试更新，**Then** 系统拒绝操作并返回错误提示："已发布的工作流不可编辑，请先取消发布"。
5. **Given** 管理员更新一个不存在的工作流 ID，**When** 系统处理请求，**Then** 返回 404 Not Found 错误。

---

### User Story 4 — 删除工作流定义 (Priority: P2)

管理员需要删除不再需要的工作流定义。只有 Draft 状态的工作流可以被删除；已发布的工作流需先取消发布。删除操作不可逆。

**Why this priority**: 删除是 CRUD 闭环的必要操作，但使用频率较低，不影响创建和查询的核心价值。

**Independent Test**: 可独立验证——创建一个 Draft 工作流后通过删除 API 删除它，再次查询确认已不存在。

**Acceptance Scenarios**:

1. **Given** 存在一个 Draft 状态的工作流定义，**When** 管理员删除该工作流，**Then** 系统成功删除工作流记录，返回成功响应。再次查询该 ID 返回 404。
2. **Given** 存在一个 Published 状态的工作流定义，**When** 管理员尝试删除，**Then** 系统拒绝操作并返回错误提示："已发布的工作流不可删除，请先取消发布"。
3. **Given** 某个工作流已被 AgentRegistration 引用（WorkflowRef 指向该工作流），**When** 管理员尝试删除该工作流，**Then** 系统拒绝操作并返回错误提示："该工作流已被 Agent 引用，无法删除"。
4. **Given** 管理员删除一个不存在的工作流 ID，**When** 系统处理请求，**Then** 返回 404 Not Found 错误。

---

### Edge Cases

- **DAG 中的自环边**: 节点的边指向自身（如 A → A），系统应检测并拒绝，提示"边不能指向自身节点"。
- **重复边**: 同一对源/目标节点之间存在多条无条件边，系统应检测并拒绝重复边（条件边除外，同一对节点可有多条不同条件的边）。
- **空 DAG 图（零节点）**: 提交一个无节点的工作流图，系统应拒绝并提示至少需要一个节点。
- **节点 ID 重复**: DAG 图中两个节点使用相同的节点 ID，系统应拒绝并提示节点 ID 必须唯一。
- **边引用不存在的节点**: 边的 source 或 target 指向图中不存在的节点 ID，系统应拒绝并提示引用无效。
- **工作流名称唯一性**: 创建或更新时如果工作流名称与已有工作流重复，系统应拒绝并提示名称已被占用。
- **大型 DAG（节点数量上限）**: DAG 图包含超过 100 个节点时，系统应提示节点数量已超过建议上限（不阻止保存，仅警告）。
- **并发更新冲突**: 两个管理员同时更新同一工作流定义，后提交的请求应检测到版本冲突并返回 409 Conflict 错误。

## Requirements *(mandatory)*

### Functional Requirements

**后端 — 工作流定义领域模型**

- **FR-001**: 系统 MUST 定义 `WorkflowDefinition` 聚合根实体，包含名称（唯一，最长 200 字符）、描述（可选，最长 2000 字符）、状态（Draft/Published）和 DAG 图（`WorkflowGraphVO` 值对象）。
- **FR-002**: 系统 MUST 定义 `WorkflowGraphVO` 值对象，包含节点列表（`List<WorkflowNodeVO>`）和边列表（`List<WorkflowEdgeVO>`），以 JSONB 格式持久化。
- **FR-003**: 系统 MUST 定义 `WorkflowNodeVO` 值对象，包含节点 ID（图内唯一标识）、节点类型（Agent/Tool/Condition/FanOut/FanIn）、引用 ID（对于 Agent 和 Tool 类型，指向已注册的 AgentRegistration 或 ToolRegistration 的 ID）、显示名称、配置参数（JSON 格式，可选）。
- **FR-004**: 系统 MUST 定义 `WorkflowEdgeVO` 值对象，包含边 ID、源节点 ID、目标节点 ID、边类型（Normal/Conditional）和条件表达式（仅 Conditional 类型必填）。
- **FR-005**: 系统 MUST 定义 `WorkflowNodeType` 枚举，包含 Agent、Tool、Condition、FanOut、FanIn 五种类型。
- **FR-006**: 系统 MUST 定义 `WorkflowEdgeType` 枚举，包含 Normal 和 Conditional 两种类型。
- **FR-007**: 系统 MUST 定义 `WorkflowStatus` 枚举，包含 Draft 和 Published 两种状态。

**后端 — DAG 有效性校验**

- **FR-008**: 系统 MUST 在创建和更新工作流定义时验证 DAG 图的有效性：无环（使用拓扑排序或 DFS 检测）、无自环边、无孤立节点、无重复节点 ID、边引用的节点均存在于节点列表中。
- **FR-009**: 系统 MUST 在创建和更新时验证 Agent 和 Tool 类型节点的引用 ID 指向真实存在的 AgentRegistration 或 ToolRegistration 记录。
- **FR-010**: 系统 MUST 在验证失败时返回清晰的错误信息，包含具体的校验失败原因和涉及的节点/边 ID。

**后端 — CRUD 操作**

- **FR-011**: 系统 MUST 提供创建工作流定义的能力（`POST /api/workflows`），接受名称、描述和 DAG 图定义，校验通过后持久化并返回完整的工作流定义（含系统生成的 ID 和时间戳），初始状态为 Draft。
- **FR-012**: 系统 MUST 提供查询工作流定义列表的能力（`GET /api/workflows`），返回所有工作流定义的摘要信息（ID、名称、描述、状态、节点数量、创建时间、更新时间）。
- **FR-013**: 系统 MUST 提供查询工作流定义详情的能力（`GET /api/workflows/{id}`），返回完整的工作流定义，包括 DAG 图的所有节点和边。不存在时返回 404。
- **FR-014**: 系统 MUST 提供更新工作流定义的能力（`PUT /api/workflows/{id}`），接受更新后的名称、描述和 DAG 图定义。仅 Draft 状态可更新；Published 状态返回 400 错误。更新时对新图执行完整的 DAG 有效性校验。不存在时返回 404。
- **FR-015**: 系统 MUST 提供删除工作流定义的能力（`DELETE /api/workflows/{id}`），仅 Draft 状态且未被 AgentRegistration 引用的工作流可删除。违反条件时返回 400 错误。不存在时返回 404。
- **FR-016**: 系统 MUST 在创建和更新时验证工作流名称的唯一性，名称冲突时返回 409 Conflict 错误。

**后端 — 仓储层**

- **FR-017**: 系统 MUST 提供 `IWorkflowDefinitionRepository` 接口，继承 `IRepository<WorkflowDefinition>`，并扩展按名称查询（`GetByNameAsync`）和按状态过滤（`GetByStatusAsync`）的能力。

### Key Entities

- **WorkflowDefinition（聚合根）**: 工作流定义的核心实体，包含名称、描述、状态和 DAG 图。继承 `BaseEntity`（Guid ID, CreatedAt, UpdatedAt）。通过状态枚举区分 Draft 和 Published 阶段。一个 WorkflowDefinition 可被 AgentRegistration（AgentType=Workflow）通过 WorkflowRef 引用。
- **WorkflowGraphVO（值对象）**: 表示工作流的 DAG 图结构，包含节点列表和边列表。作为 WorkflowDefinition 的 JSONB 列存储。整体替换更新（值对象不可变语义）。
- **WorkflowNodeVO（值对象）**: DAG 图中的单个节点，描述一个执行单元。节点类型决定其行为：Agent 节点引用已注册 Agent 执行对话或推理，Tool 节点引用已注册工具执行操作，Condition 节点根据条件选择下游路径，FanOut 节点将执行并行分发，FanIn 节点将并行结果聚合。
- **WorkflowEdgeVO（值对象）**: DAG 图中的连接边，描述节点间的执行流向。Normal 边表示无条件执行，Conditional 边在满足条件表达式时路由到目标节点。
- **WorkflowNodeType（枚举）**: 节点类型枚举——Agent、Tool、Condition、FanOut、FanIn。
- **WorkflowEdgeType（枚举）**: 边类型枚举——Normal、Conditional。
- **WorkflowStatus（枚举）**: 工作流状态枚举——Draft、Published。

## Assumptions

- DAG 校验采用拓扑排序算法（Kahn's algorithm），在创建和更新时同步执行。对于 100 节点以内的 DAG，校验时间可忽略不计。
- 工作流定义的 DAG 图以 JSONB 格式存储在 PostgreSQL 中，整体读写（非逐节点操作），与现有 Value Object 存储模式一致。
- Condition 节点的条件表达式语法为简单的 JSON 路径匹配表达式，具体语法将在 SPEC-021（工作流执行引擎）中细化定义。本 Spec 仅要求条件表达式作为字符串存储。
- FanOut/FanIn 节点的并行执行语义将在 SPEC-021 中定义。本 Spec 仅关注其作为节点类型在定义中的存在和校验。
- Published 状态的工作流不可编辑或删除，需通过独立的"取消发布"操作回到 Draft 状态。取消发布的具体端点将在 SPEC-026（工作流发布为 WorkflowAgent）中定义。
- Agent 和 Tool 节点的引用 ID 校验为创建/更新时的即时校验。引用的 Agent 或 Tool 被后续删除不影响已创建的工作流定义（延迟校验在执行时处理，属于 SPEC-021 范畴）。
- 工作流名称全局唯一（跨所有状态），最长 200 字符，与 AgentRegistration、ToolRegistration 名称长度限制一致。
- 节点数量建议上限为 100 个，超过时返回警告提示但不阻止保存。此限制可通过配置调整。

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 管理员可在 30 秒内通过 API 创建一个包含 5 个节点和 4 条边的工作流定义，系统返回完整定义且数据立即可查。
- **SC-002**: 系统对包含 50 个节点的 DAG 图执行有效性校验（环检测、孤立节点检测、引用校验）的响应时间不超过 2 秒。
- **SC-003**: 工作流列表查询在 100 条工作流定义下响应时间不超过 1 秒。
- **SC-004**: 所有 DAG 校验失败场景（环路、孤立节点、无效引用、重复 ID）均返回明确、可操作的错误提示，用户无需猜测失败原因。
- **SC-005**: Draft 状态工作流支持全字段更新（名称、描述、DAG 图），Published 状态工作流的编辑和删除操作被可靠拦截。
- **SC-006**: 删除保护机制生效——已被 AgentRegistration 引用的工作流无法被删除，返回清晰的依赖关系提示。
