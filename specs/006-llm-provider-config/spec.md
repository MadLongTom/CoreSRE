# Feature Specification: LLM Provider 配置与模型发现

**Feature Branch**: `006-llm-provider-config`
**Created**: 2026-02-10
**Status**: Draft
**Input**: User description: "在创建ChatClient时，没有选择OpenAI Compatible API Provider，需要现有地方配置这个provider，通过Endpoint，可以基于OpenAI协议发现可用的模型列表，在创建ChatClient时，先选择Provider，再选择这个Provider可以使用的模型"

## User Scenarios & Testing

### User Story 1 — 注册 LLM Provider (Priority: P1)

管理员需要在系统中注册一个 OpenAI 兼容的 API Provider，填写名称、Base URL（如 `https://api.openai.com/v1`）和 API Key，以便后续创建 ChatClient Agent 时使用该 Provider 的模型。

**Why this priority**: 这是整个功能的基础。没有 Provider 注册，后续的模型发现和 ChatClient 关联都无法进行。

**Independent Test**: 在 Provider 管理界面点击"新建 Provider"，填写名称、Base URL、API Key，提交后在列表中看到新建的 Provider，且 API Key 以掩码形式显示。

**Acceptance Scenarios**:

1. **Given** 管理员在 Provider 管理页面，**When** 填写完整的名称、Base URL、API Key 并提交，**Then** Provider 创建成功，列表中出现该 Provider，API Key 显示为掩码（如 `sk-****xxxx`）。
2. **Given** 管理员在 Provider 管理页面，**When** 提交的 Base URL 格式不合法，**Then** 显示验证错误提示。
3. **Given** 已有同名 Provider，**When** 再次提交相同名称，**Then** 显示名称冲突错误。
4. **Given** 管理员查看 Provider 列表，**When** 系统返回数据，**Then** 每个 Provider 显示名称、Base URL 和创建时间，API Key 永远不以明文返回。

---

### User Story 2 — 发现可用模型列表 (Priority: P1)

管理员注册 Provider 后，系统应能通过该 Provider 的 Base URL 调用 OpenAI 兼容的 `/models` 端点，自动发现该 Provider 支持的模型列表，让用户了解有哪些模型可用。

**Why this priority**: 模型发现是 Provider 与 ChatClient 之间的关键桥梁，与 US1 同优先级。

**Independent Test**: 注册一个 Provider 后，在该 Provider 的详情页点击"刷新模型列表"，系统调用 Provider 端点获取模型并展示列表。

**Acceptance Scenarios**:

1. **Given** 已注册的 Provider 且端点可访问，**When** 触发模型发现，**Then** 系统调用 `{baseUrl}/models`（附带 API Key 作为 Bearer Token），解析返回的模型列表并存储。
2. **Given** 已注册的 Provider 但端点不可达，**When** 触发模型发现，**Then** 显示连接失败错误信息，保留上次成功获取的模型列表（如有）。
3. **Given** 模型发现成功，**When** 查看 Provider 详情，**Then** 显示模型 ID 列表及上次刷新时间。
4. **Given** Provider 端点返回非标准格式，**When** 触发模型发现，**Then** 显示解析错误提示，不覆盖现有模型列表。

---

### User Story 3 — 创建 ChatClient 时选择 Provider 和模型 (Priority: P1)

用户创建 ChatClient 类型的 Agent 时，不再手动输入 Model ID，而是先从已注册的 Provider 列表中选择一个 Provider，然后从该 Provider 发现的可用模型列表中选择一个模型。

**Why this priority**: 这是用户描述中的核心需求，直接改善 ChatClient 创建体验。

**Independent Test**: 在新建 Agent 页面选择 ChatClient 类型后，能看到 Provider 下拉列表，选择 Provider 后模型下拉列表自动加载该 Provider 的可用模型，选择模型后提交创建成功。

**Acceptance Scenarios**:

1. **Given** 至少存在一个已注册的 Provider 且有可用模型，**When** 创建 ChatClient Agent 时选择 Provider，**Then** 模型下拉框自动加载该 Provider 的可用模型列表。
2. **Given** 没有已注册的 Provider，**When** 选择 ChatClient 类型，**Then** 显示"暂无 Provider，请先配置"的提示及跳转链接。
3. **Given** 选中的 Provider 没有已发现的模型，**When** 查看模型下拉框，**Then** 显示"该 Provider 暂无可用模型，请先刷新模型列表"的提示。
4. **Given** 用户选择了 Provider 和模型，**When** 提交创建，**Then** Agent 的 LlmConfig 中同时记录 Provider 引用和所选模型 ID。
5. **Given** 用户切换了 Provider，**When** 模型下拉框刷新，**Then** 之前选择的模型被清空，展示新 Provider 的模型列表。

---

### User Story 4 — 管理 Provider（编辑与删除）(Priority: P2)

管理员需要能编辑已有 Provider 的名称、Base URL 或 API Key，以及在不再需要时删除 Provider。

**Why this priority**: Provider 管理的完整生命周期需要编辑和删除能力，但优先级低于创建和模型发现。

**Independent Test**: 编辑 Provider 的 Base URL 后保存，验证新 URL 生效；删除无 Agent 关联的 Provider 后列表不再显示；尝试删除有关联 Agent 的 Provider 时被阻止。

**Acceptance Scenarios**:

1. **Given** 已注册的 Provider，**When** 编辑名称或 Base URL 并保存，**Then** 更新成功，详情页展示新值。
2. **Given** 已注册的 Provider，**When** 更新 API Key，**Then** 新 Key 生效，响应中仍以掩码展示。
3. **Given** Provider 没有被任何 ChatClient Agent 引用，**When** 删除该 Provider，**Then** 删除成功，列表不再显示。
4. **Given** Provider 被至少一个 ChatClient Agent 引用，**When** 尝试删除，**Then** 删除被拒绝，提示"该 Provider 仍被 N 个 Agent 使用"。

---

### User Story 5 — 编辑 ChatClient 时切换 Provider 或模型 (Priority: P2)

用户在编辑已有 ChatClient Agent 时，应能更改关联的 Provider 或切换到同一 Provider 下的不同模型。

**Why this priority**: 编辑是完整 CRUD 闭环的一部分，但优先级低于创建流程。

**Independent Test**: 打开已有 ChatClient Agent 的编辑模式，更改 Provider 选择后模型列表刷新，选择新模型保存后详情页显示新的 Provider 和模型。

**Acceptance Scenarios**:

1. **Given** 已有 ChatClient Agent 处于编辑模式，**When** 切换 Provider，**Then** 模型下拉框刷新为新 Provider 的模型列表，已选模型被清空。
2. **Given** 已有 ChatClient Agent 处于编辑模式，**When** 仅切换同一 Provider 下的模型并保存，**Then** 更新成功，详情页展示新模型。
3. **Given** ChatClient Agent 的详情只读模式，**When** 查看 LLM 配置区域，**Then** 显示关联的 Provider 名称和模型 ID。

---

### Edge Cases

- Provider 的 API Key 过期或无效时，模型发现返回 401 认证失败，系统应提示用户更新 API Key。
- 多个 Provider 使用相同 Base URL（不同 API Key）时，应允许共存。
- Provider 的 `/models` 端点返回超大列表（>1000 个模型）时，系统应正常处理并全量存储。
- 用户在创建 ChatClient 过程中，所选 Provider 被其他管理员删除时，提交应返回验证错误而非系统异常。
- API Key 仅在创建和更新时传入，响应中永远不返回明文 Key。

## Requirements

### Functional Requirements

- **FR-001**: 系统 MUST 提供 Provider 的完整注册功能，包含名称（唯一）、Base URL 和 API Key 字段。
- **FR-002**: 系统 MUST 在 Provider 列表和详情响应中以掩码形式展示 API Key（仅显示最后 4 位），永不返回明文。
- **FR-003**: 系统 MUST 支持通过 Provider 的 Base URL 调用 OpenAI 兼容的 `GET /models` 端点发现可用模型。
- **FR-004**: 模型发现 MUST 将 Provider 的 API Key 作为 `Authorization: Bearer {key}` 头发送。
- **FR-005**: 模型发现结果 MUST 持久化存储，包含模型 ID 列表和最后刷新时间。
- **FR-006**: 模型发现失败时 MUST 保留上次成功获取的模型列表，不覆盖已有数据。
- **FR-007**: 创建 ChatClient Agent 时，MUST 提供 Provider 选择（下拉列表），选择后 MUST 加载该 Provider 的可用模型供选择。
- **FR-008**: ChatClient Agent 的 LlmConfig MUST 存储 Provider 的引用标识和所选模型 ID。
- **FR-009**: 切换 Provider 选择时 MUST 清空已选模型，并重新加载新 Provider 的模型列表。
- **FR-010**: 系统 MUST 支持 Provider 的编辑（名称、Base URL、API Key 均可更新）和删除。
- **FR-011**: 删除 Provider 时，若仍有 ChatClient Agent 引用该 Provider，MUST 拒绝删除并提示关联数量。
- **FR-012**: 系统 MUST 对 Provider 名称进行唯一性校验，名称重复时返回冲突错误。
- **FR-013**: 系统 MUST 对 Base URL 进行格式校验，仅接受 `http://` 或 `https://` 开头的合法 URL。
- **FR-014**: 编辑 ChatClient Agent 时 MUST 支持切换 Provider 和模型，行为与创建时一致。
- **FR-015**: 没有可用 Provider 时，ChatClient 创建表单 MUST 显示引导提示，引导用户先配置 Provider。
- **FR-016**: Provider 详情页 MUST 显示已发现的模型列表和最后刷新时间，并提供手动刷新按钮。

### Key Entities

- **LLM Provider**: 表示一个 OpenAI 兼容的 API 服务提供方。关键属性：名称（唯一）、Base URL、API Key（加密存储）、已发现模型列表、最后刷新时间、创建时间、更新时间。
- **Discovered Model**: 从 Provider 端点发现的可用模型。关键属性：模型 ID、所属 Provider 引用、发现时间。
- **LlmConfig（扩展）**: 现有的 LLM 配置，需新增 Provider 引用。关键属性：Provider 引用、模型 ID、Instructions、Tool Refs。

## Success Criteria

### Measurable Outcomes

- **SC-001**: 用户可以在 60 秒内完成一个 Provider 的注册（填写名称、URL、Key 并提交）。
- **SC-002**: 模型发现操作在正常网络条件下 10 秒内完成并展示结果。
- **SC-003**: 创建 ChatClient Agent 时，从选择 Provider 到选择模型的交互步骤不超过 3 次点击。
- **SC-004**: API Key 在任何用户可见的界面和 API 响应中均不以明文展示。
- **SC-005**: 被 Agent 引用的 Provider 100% 不可被删除，系统返回明确的关联提示。
- **SC-006**: 模型发现失败不影响已有模型列表数据的完整性。

## Assumptions

- Provider 的 `/models` 端点遵循 OpenAI API 规范，返回 `{ data: [{ id: "model-id", ... }] }` 格式。
- API Key 的安全存储采用应用层加密（AES-256 或同等强度），具体加密机制在规划阶段确定。
- 模型发现为用户手动触发，不设自动定时刷新（可在后续迭代中添加）。
- 系统中 Provider 数量预计在数十个量级，无需分页。
- 单个 Provider 返回的模型数量通常在数百以内，极端情况不超过数千个。
- 现有 ChatClient Agent 已存储的 `modelId`（手动输入）在迁移时保留为原值，`providerId` 可为空（向后兼容）。
