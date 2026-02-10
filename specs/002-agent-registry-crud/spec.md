# Feature Specification: Agent 注册与 CRUD 管理（多类型）

**Feature Branch**: `002-agent-registry-crud`  
**Created**: 2026-02-09  
**Status**: Draft  
**Input**: User description: "实现多类型 Agent 的完整生命周期管理——注册、查询列表、获取详情、更新、注销。系统支持三种 Agent 类型：A2AAgent、ChatClientAgent、WorkflowAgent。注册 API 统一入口，通过 agentType 字段区分类型，根据类型校验不同的必填字段。数据持久化到数据库。"

## User Scenarios & Testing *(mandatory)*

### User Story 1 — 注册 A2A 类型 Agent（Priority: P1） 🎯 MVP

作为平台管理员，我需要注册一个遵循 A2A 协议的远程 Agent，以便平台能够发现并调度该 Agent 处理业务请求。

注册时我提交 Agent 的基本信息（名称、描述）、类型标识（A2A）、以及 A2A 协议要求的 AgentCard 数据——包括 Agent 拥有的技能（skills）、支持的通信接口（interfaces）、安全认证方案（securitySchemes）和远程端点地址（endpoint）。

系统校验必填字段后，将 Agent 信息持久化到数据库，返回包含系统分配 ID 的完整 Agent 记录。新注册的 Agent 初始状态为"已注册"（Registered）。

**Why this priority**: A2A Agent 是平台的核心 Agent 类型，也是最复杂的注册场景（包含嵌套的 skills、interfaces、securitySchemes 数据）。实现此类型即可验证领域模型设计、值对象映射、类型鉴别校验等核心架构决策。

**Independent Test**: 发送 `POST /api/agents` 请求（`agentType: "A2A"`），验证返回 201 状态码和包含 ID 的完整 Agent 记录。随后通过 `GET /api/agents/{id}` 确认持久化成功。

**Acceptance Scenarios**:

1. **Given** 系统中无任何 Agent 注册记录，**When** 提交包含完整 AgentCard 信息的 A2A Agent 注册请求，**Then** 系统返回 HTTP 201，响应体包含系统分配的唯一 ID、状态为 Registered、且 AgentCard 数据与提交一致
2. **Given** 系统已存在其他 Agent，**When** 提交 A2A Agent 注册请求但缺少 endpoint 字段，**Then** 系统返回 HTTP 400，错误信息明确指出缺少必填字段 endpoint
3. **Given** 系统已存在其他 Agent，**When** 提交 A2A Agent 注册请求但 agentType 为 "A2A" 却未提供 agentCard 数据，**Then** 系统返回 HTTP 400，错误信息指出 A2A 类型需要 agentCard 信息
4. **Given** 系统中已存在同名 Agent，**When** 提交相同名称的 A2A Agent 注册请求，**Then** 系统返回 HTTP 409 Conflict，提示名称已被占用

---

### User Story 2 — 查询 Agent 列表与详情（Priority: P1）

作为平台管理员或其他模块（如 Orchestrator），我需要查看已注册的所有 Agent 列表，并能按类型过滤，以便了解平台当前的 Agent 资源全貌。同时，我需要获取单个 Agent 的完整详情，以查看其配置信息。

列表接口返回 Agent 的摘要信息（ID、名称、类型、状态、创建时间），支持通过 `?type=A2A` 等查询参数过滤特定类型。详情接口返回 Agent 的完整注册信息，包括类型特有的配置数据（如 A2A 的 AgentCard、ChatClient 的 LLM 配置等）。

**Why this priority**: 查询是所有消费方（前端 UI、Orchestrator 选 Agent）的基础依赖。没有查询就无法验证注册是否成功，也无法让其他模块发现 Agent。

**Independent Test**: 注册若干不同类型的 Agent 后，`GET /api/agents` 返回完整列表；`GET /api/agents?type=A2A` 仅返回 A2A 类型；`GET /api/agents/{id}` 返回含完整配置的详情。

**Acceptance Scenarios**:

1. **Given** 系统中已注册 2 个 A2A Agent 和 1 个 ChatClient Agent，**When** 请求 `GET /api/agents`，**Then** 返回 HTTP 200 和包含 3 条记录的列表，每条包含 id、name、agentType、status、createdAt
2. **Given** 系统中已注册多种类型 Agent，**When** 请求 `GET /api/agents?type=A2A`，**Then** 仅返回 agentType 为 A2A 的 Agent 列表
3. **Given** 系统中已注册一个 A2A Agent，**When** 请求 `GET /api/agents/{该Agent的ID}`，**Then** 返回 HTTP 200 和完整的 Agent 详情，包含 agentCard 嵌套数据
4. **Given** 系统中无此 ID 的 Agent，**When** 请求 `GET /api/agents/{不存在的ID}`，**Then** 返回 HTTP 404

---

### User Story 3 — 注册 ChatClient 和 Workflow 类型 Agent（Priority: P1）

作为平台管理员，我需要注册 ChatClient 类型的 Agent（指定 LLM 模型、系统指令和工具引用）和 Workflow 类型的 Agent（引用已有的 WorkflowDefinition），以满足平台对多种 Agent 类型的管理需求。

ChatClient Agent 注册时提交 LLM 配置信息（modelId、instructions、toolRefs），系统校验 modelId 不为空后创建记录。

Workflow Agent 注册时提交 workflowRef（引用的 WorkflowDefinition ID），系统记录该引用关系。由于 WorkflowDefinition 属于 M3 模块（尚未实现），当前仅记录 ID 引用，不做跨模块存在性校验。

**Why this priority**: 三种 Agent 类型是 BRD 明确要求的（SC-1："平台可注册、发现并调度至少 3 个不同类型的智能体"），需要全部实现才能满足业务目标。

**Independent Test**: 分别发送 `POST /api/agents`（agentType: ChatClient / Workflow），验证各自的类型特有校验规则和持久化逻辑。

**Acceptance Scenarios**:

1. **Given** 系统中无 ChatClient Agent，**When** 提交含 modelId 和 instructions 的 ChatClient Agent 注册请求，**Then** 系统返回 HTTP 201，响应包含完整的 LLM 配置信息
2. **Given** 提交 ChatClient Agent 注册请求但 modelId 为空，**When** 系统处理请求，**Then** 返回 HTTP 400，错误信息指出 ChatClient 类型需要 modelId
3. **Given** 系统中无 Workflow Agent，**When** 提交含有效 workflowRef（GUID）的 Workflow Agent 注册请求，**Then** 系统返回 HTTP 201，响应包含 workflowRef 字段
4. **Given** 提交 Workflow Agent 注册请求但未提供 workflowRef，**When** 系统处理请求，**Then** 返回 HTTP 400，错误信息指出 Workflow 类型需要 workflowRef

---

### User Story 4 — 更新 Agent 注册信息（Priority: P1）

作为平台管理员，我需要更新已注册 Agent 的配置信息（如更改描述、更新技能列表、修改 LLM 指令等），以便在 Agent 服务升级或配置变更时保持注册中心信息的准确性。

更新操作通过 `PUT /api/agents/{id}` 执行。系统验证目标 Agent 存在后，根据 Agent 类型校验更新数据的合法性（与注册时相同的校验规则），然后替换 Agent 的配置信息并更新 updatedAt 时间戳。Agent 的类型（agentType）不可变更。

**Why this priority**: 更新是 CRUD 生命周期的重要组成部分。Agent 配置变更是日常运维高频操作。

**Independent Test**: 注册一个 Agent 后，通过 `PUT /api/agents/{id}` 修改描述和技能，再通过 `GET /api/agents/{id}` 验证更新生效。

**Acceptance Scenarios**:

1. **Given** 系统中已注册一个 A2A Agent，**When** 提交 `PUT /api/agents/{id}` 更新其 description 和 skills 列表，**Then** 返回 HTTP 200，响应中 description 和 skills 反映新值，updatedAt 已更新
2. **Given** 系统中已注册一个 A2A Agent，**When** 提交 `PUT /api/agents/{id}` 试图将 agentType 从 A2A 改为 ChatClient，**Then** 返回 HTTP 400，错误信息指出 Agent 类型不可变更
3. **Given** 系统中不存在此 ID 的 Agent，**When** 提交 `PUT /api/agents/{id}`，**Then** 返回 HTTP 404

---

### User Story 5 — 注销 Agent（Priority: P1）

作为平台管理员，我需要注销不再使用的 Agent，将其从可用 Agent 列表中移除，以保持注册中心的准确性。

注销通过 `DELETE /api/agents/{id}` 执行。系统将 Agent 记录从数据库中永久删除。注销后，该 Agent 不再出现在列表查询结果中，通过 ID 查询也返回 404。

**Why this priority**: 注销是 Agent 生命周期闭环的最后一环，不可缺少。

**Independent Test**: 注册一个 Agent，通过 `DELETE /api/agents/{id}` 注销，再通过 `GET /api/agents/{id}` 确认返回 404。

**Acceptance Scenarios**:

1. **Given** 系统中已注册一个 Agent，**When** 提交 `DELETE /api/agents/{id}`，**Then** 返回 HTTP 204 No Content
2. **Given** Agent 已被成功注销，**When** 查询 `GET /api/agents/{id}`，**Then** 返回 HTTP 404
3. **Given** 系统中不存在此 ID 的 Agent，**When** 提交 `DELETE /api/agents/{不存在的ID}`，**Then** 返回 HTTP 404

---

### Edge Cases

- 提交请求体中 agentType 字段值不在 [A2A, ChatClient, Workflow] 枚举范围内时，系统返回 HTTP 400 并明确列出合法的类型值
- Agent 名称中包含特殊字符（如 `<script>`、SQL 注入片段）时，系统正常处理并存储（数据库参数化查询防注入），不做内容过滤
- Agent 名称长度超过 200 字符时，系统返回 HTTP 400
- Agent 描述为空字符串时，系统接受注册（描述为可选字段）
- A2A Agent 的 skills 列表为空时，系统接受注册（Agent 可能暂无技能）
- ChatClient Agent 的 toolRefs 列表为空时，系统接受注册（Agent 可不绑定工具）
- 并发注册同名 Agent 时，仅第一个成功，后续请求返回 409 Conflict
- 请求体 JSON 格式错误时，系统返回 HTTP 400 并提示解析失败

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: 系统必须提供统一的 Agent 注册入口，接受包含 agentType 字段的注册请求，根据类型值（A2A / ChatClient / Workflow）分派到对应的校验与存储逻辑
- **FR-002**: 系统必须支持注册 A2A 类型 Agent，要求提交 name、agentType、endpoint 和 agentCard 数据（含 skills、interfaces、securitySchemes），其中 name、endpoint 为必填
- **FR-003**: 系统必须支持注册 ChatClient 类型 Agent，要求提交 name、agentType 和 llmConfig 数据（含 modelId、instructions、toolRefs），其中 name、modelId 为必填
- **FR-004**: 系统必须支持注册 Workflow 类型 Agent，要求提交 name、agentType 和 workflowRef（GUID），其中 name、workflowRef 为必填
- **FR-005**: 注册成功后，系统必须为 Agent 分配唯一 ID，设置状态为 Registered，记录 createdAt 时间戳，并将完整记录持久化到数据库
- **FR-006**: 系统必须保证 Agent 名称在全局范围内唯一，重复名称注册返回冲突错误
- **FR-007**: 系统必须提供 Agent 列表查询接口，返回所有已注册 Agent 的摘要信息（id、name、agentType、status、createdAt）
- **FR-008**: Agent 列表查询必须支持按 agentType 过滤（通过查询参数 `?type=`）
- **FR-009**: 系统必须提供 Agent 详情查询接口，根据 ID 返回 Agent 的完整注册信息，包含类型特有的配置数据
- **FR-010**: 系统必须提供 Agent 更新接口，支持修改 Agent 的名称、描述及类型特有配置，但不允许变更 agentType
- **FR-011**: 更新操作必须对更新后的数据执行与注册相同的校验规则
- **FR-012**: 更新成功后必须更新 updatedAt 时间戳
- **FR-013**: 系统必须提供 Agent 注销接口，根据 ID 永久删除 Agent 记录
- **FR-014**: 所有涉及 ID 查找的操作（详情、更新、注销），当目标 Agent 不存在时返回 Not Found 错误
- **FR-015**: 所有写入操作（注册、更新）的请求数据不符合校验规则时，返回结构化的错误信息，包含具体的字段级错误描述

### Key Entities

- **AgentRegistration**（聚合根）: 代表一个已注册的 Agent。包含通用属性（ID、名称、描述、类型、状态、时间戳）以及按类型区分的配置数据。是 Agent Registry 模块的核心实体，所有 Agent 生命周期操作的入口。继承自 BaseEntity（获得 Id、CreatedAt、UpdatedAt）。
- **AgentType**（枚举）: 区分 Agent 的三种类型——A2A（远程 A2A 协议 Agent）、ChatClient（本地 LLM Agent）、Workflow（工作流 Agent）。一旦注册后不可变更。
- **AgentStatus**（枚举）: 表示 Agent 的生命周期状态——Registered（已注册，初始状态）、Active（活跃）、Inactive（不活跃）、Error（错误）。本 Spec 范围内，新注册 Agent 状态固定为 Registered；状态流转逻辑由后续 SPEC-002（健康检查）处理。
- **AgentCardVO**（值对象）: A2A 类型 Agent 的协议描述卡片，包含技能列表（skills）、通信接口列表（interfaces）、安全认证方案列表（securitySchemes）。仅在 agentType 为 A2A 时存在。
- **AgentSkillVO**（值对象）: Agent 的单项技能描述，包含名称（name）和描述（description）。嵌套在 AgentCardVO 中。
- **AgentInterfaceVO**（值对象）: Agent 支持的通信接口类型（如 HTTP+SSE、WebSocket 等），包含协议（protocol）和 URL 路径（path）。嵌套在 AgentCardVO 中。
- **SecuritySchemeVO**（值对象）: Agent 的安全认证方案，包含方案类型（type，如 apiKey、oauth2、bearer）和配置参数。嵌套在 AgentCardVO 中。
- **LlmConfigVO**（值对象）: ChatClient 类型 Agent 的 LLM 配置，包含模型 ID（modelId）、系统指令（instructions）、工具引用列表（toolRefs，为 GUID 列表）。仅在 agentType 为 ChatClient 时存在。

## Assumptions

- **Workflow 跨模块引用**: WorkflowAgent 的 workflowRef 引用的 WorkflowDefinition 属于 M3 模块（尚未实现）。当前仅记录 GUID 引用，不做跨模块存在性校验。待 M3 模块上线后，可通过领域事件或后台任务验证引用的有效性。
- **Tool 跨模块引用**: ChatClientAgent 的 toolRefs 引用的 Tool 属于 M2 模块（尚未实现）。当前仅记录 GUID 列表，不做跨模块存在性校验。
- **初始状态**: 新注册 Agent 的状态固定为 Registered。状态到 Active 的流转由 SPEC-002（健康检查）负责。
- **HealthCheckVO 定义**: AgentRegistration 的领域模型中包含 HealthCheckVO 属性（来自 PRD），但本 Spec 不实现健康检查行为。HealthCheckVO 在注册时初始化为默认值（未检查状态），其业务逻辑由 SPEC-002 处理。
- **认证鉴权**: 本 Spec 不涉及 API 认证鉴权（无 JWT、API Key 等）。Agent Registry 的访问控制由后续安全模块统一处理。
- **分页**: Agent 列表查询初期不实现分页（预期早期 Agent 数量 < 100）。当 Agent 数量增长时，在后续迭代中添加分页支持。
- **软删除 vs 硬删除**: 注销操作执行硬删除（物理删除数据库记录），非软删除。审计需求由后续迭代通过事件溯源或操作日志满足。
- **名称唯一性范围**: Agent 名称在全局范围内唯一（不按类型分区），以避免歧义。

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 平台可成功注册 A2A、ChatClient、Workflow 三种类型的 Agent，并通过列表接口查询到全部三种类型
- **SC-002**: Agent 注册操作（从提交到收到响应）在 1 秒内完成
- **SC-003**: 非法注册请求（缺少必填字段、类型不匹配）100% 返回结构化错误信息，用户无需查阅文档即可理解错误原因
- **SC-004**: Agent 列表查询在注册数量 ≤ 100 条时，响应时间在 500 毫秒以内
- **SC-005**: Agent 名称唯一性约束在并发场景下无数据不一致，重复名称 100% 返回冲突错误
- **SC-006**: 完整 CRUD 生命周期（注册→查询→更新→注销）可在 5 分钟内通过 API 端到端验证完成
