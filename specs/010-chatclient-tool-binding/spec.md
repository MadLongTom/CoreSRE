# Feature Specification: ChatClient 工具绑定与对话调用

**Feature Branch**: `010-chatclient-tool-binding`  
**Created**: 2026-02-11  
**Status**: Draft  
**Input**: User description: "让ChatClient在配置中可以绑定我们注册的工具，并可以在对话中供LLM调用，注意需要同步更改AG-UI显示Tool调用消息以及前端展示，绑定工具时也要考虑UX设计"

## User Scenarios & Testing *(mandatory)*

### User Story 1 — 工具绑定配置（可视化工具选择器）(Priority: P1)

平台管理员在注册或编辑 ChatClient Agent 时，需要为该 Agent 绑定已注册的工具（REST API 或 MCP Server 中已发现的 Tool）。系统提供一个可搜索、可多选的工具选择器，替代当前的手工输入 GUID 方式。管理员可以浏览所有处于 Active 状态的工具，查看工具名称、描述和类型标签，通过搜索快速定位，勾选后即完成绑定。已绑定的工具以卡片/标签形式展示在配置区域，支持一键移除。

**Why this priority**: 工具绑定是整个功能链的起点。没有正确的绑定配置，后续的 LLM 工具调用和前端展示都无法实现。同时，良好的 UX 直接影响管理员的操作效率和配置正确性。

**Independent Test**: 可独立验证——在 Agent 创建/编辑页面选择工具并保存，刷新后确认绑定关系已持久化。

**Acceptance Scenarios**:

1. **Given** 管理员进入 ChatClient Agent 编辑页面，**When** 点击"添加工具"按钮，**Then** 弹出工具选择器面板，展示所有 Active 状态的工具列表（REST API 工具直接展示，MCP Server 展示其已发现的子工具项 McpToolItem）。
2. **Given** 工具选择器已打开且存在 15+ 个可用工具，**When** 在搜索框输入关键词，**Then** 列表实时过滤，仅显示名称或描述匹配的工具。
3. **Given** 管理员已勾选 3 个工具，**When** 点击确认，**Then** 已选工具以标签卡片形式展示在配置区域，每个卡片显示工具名称、类型图标和移除按钮。
4. **Given** 配置区域已有 3 个已绑定工具，**When** 点击某个工具卡片的移除按钮，**Then** 该工具从绑定列表中移除，可再次通过选择器添加。
5. **Given** 管理员已完成工具绑定配置，**When** 提交保存 Agent，**Then** `LlmConfig.toolRefs` 字段持久化绑定的工具 ID 列表，刷新页面后绑定关系仍然存在。

---

### User Story 2 — LLM 对话中的工具自动调用 (Priority: P1)

用户在 Chat 页面与已绑定工具的 ChatClient Agent 对话时，LLM 可以根据用户消息判断是否需要调用绑定的工具。当 LLM 决定调用工具时，后端自动执行工具调用（通过已有的 Tool Gateway 调用链路），将结果返回给 LLM，LLM 再基于工具返回结果生成最终回复。整个过程对用户透明，结果呈现在对话流中。

**Why this priority**: 这是功能的核心价值——让 ChatClient Agent 具备使用外部工具的能力，是 Function Calling 的标准实现。

**Independent Test**: 可独立验证——创建一个绑定了 REST API 工具（如查询服务状态）的 ChatClient Agent，发送需要工具回答的问题，验证 LLM 调用工具并返回基于工具结果的回复。

**Acceptance Scenarios**:

1. **Given** ChatClient Agent 已绑定一个 REST API 工具（如查询服务状态），**When** 用户发送需要调用该工具才能回答的消息，**Then** 后端自动执行工具调用，LLM 基于工具返回的数据生成回复。
2. **Given** ChatClient Agent 已绑定 MCP Server 的某个子工具，**When** 用户发送触发该工具的消息，**Then** 后端通过 MCP 协议调用该工具，LLM 获取结果后回复用户。
3. **Given** 绑定的工具调用失败（网络超时或目标服务不可用），**When** 工具调用返回错误，**Then** LLM 收到错误信息，在回复中向用户说明工具调用未成功，而非系统崩溃。
4. **Given** 单次对话中 LLM 决定连续调用多个不同工具，**When** 多工具调用依次完成，**Then** 所有工具结果汇总后提供给 LLM，LLM 给出综合回复。
5. **Given** ChatClient Agent 未绑定任何工具，**When** 用户发送任意消息，**Then** 对话行为与之前完全一致（纯文本 LLM 对话），不受工具功能影响。

---

### User Story 3 — AG-UI 工具调用过程可视化 (Priority: P1)

用户在 Chat 页面与 Agent 对话时，当 LLM 触发工具调用，前端实时展示工具调用的完整过程：工具调用开始（显示工具名称和调用参数）→ 等待执行中（加载动画）→ 调用完成（可折叠展示返回结果）。工具调用消息作为独立的消息块嵌入在对话流中，位于用户消息和 Assistant 最终回复之间。

**Why this priority**: 工具调用过程的可视化是用户理解 Agent 行为的关键。没有可视化反馈，用户无法判断 AI 是否在执行工具操作、执行了哪些操作、结果是什么，体验大幅降低。

**Independent Test**: 可独立验证——在对话中触发工具调用后，观察前端是否显示工具名称、参数、执行状态和返回结果。

**Acceptance Scenarios**:

1. **Given** LLM 决定调用工具，**When** 后端开始执行工具调用，**Then** 前端实时显示工具调用卡片，含工具名称和"调用中..."状态指示器。
2. **Given** 工具调用正在进行中，**When** 后端通过 SSE 流式发送工具调用参数（TOOL_CALL_ARGS 事件），**Then** 前端在工具调用卡片中展示调用参数（以 JSON 格式，可折叠）。
3. **Given** 工具调用完成并返回结果，**When** 后端发送 TOOL_CALL_END 事件，**Then** 工具调用卡片更新为"已完成"状态，展示返回结果（可折叠），卡片从"加载中"变为"完成"视觉状态。
4. **Given** 单次回复中 LLM 调用了 2 个工具，**When** 两个工具调用均完成，**Then** 前端按调用顺序展示两个独立的工具调用卡片，最终 Assistant 文本回复在所有工具卡片之后显示。
5. **Given** 工具调用失败，**When** 后端发送工具调用错误信息，**Then** 工具调用卡片显示"调用失败"状态（红色标识），展示错误信息，对话继续正常进行。

---

### User Story 4 — Agent 详情页工具绑定概览 (Priority: P2)

在 Agent 详情页查看 ChatClient Agent 信息时，管理员可以直观地看到该 Agent 当前绑定的所有工具。已绑定工具以卡片列表形式展示，每张卡片包括工具名称、类型标签（REST API / MCP Tool）、工具状态（Active / Inactive）、简要描述。点击工具卡片可跳转到工具详情页。

**Why this priority**: 查看绑定工具是只读操作，依赖于 P1 的绑定配置功能，但能提升管理效率和信息可读性。

**Independent Test**: 可独立验证——查看一个已绑定工具的 Agent 详情页，确认工具卡片列表正确展示所有绑定工具及其状态。

**Acceptance Scenarios**:

1. **Given** ChatClient Agent 已绑定 3 个工具，**When** 管理员进入该 Agent 详情页，**Then** 工具绑定区域显示 3 张工具卡片，包含名称、类型图标、状态徽章、描述摘要。
2. **Given** Agent 详情页展示工具卡片，**When** 某个已绑定工具的状态为 Inactive，**Then** 该工具卡片显示黄色警告标识，提示工具不可用。
3. **Given** Agent 详情页展示工具卡片，**When** 点击某张工具卡片，**Then** 跳转到该工具的详情页。

---

### Edge Cases

- **工具绑定后被删除**: 当 Agent 绑定的工具被管理员删除时，该 toolRef 应在 Agent 详情页显示"工具已删除"的失效提示，不影响 Agent 的其他功能。对话时后端跳过已删除的工具，不向 LLM 注册该 AIFunction。
- **工具绑定后变为 Inactive**: 已绑定工具状态变为 Inactive 时，LLM 仍可决定调用该工具，但后端调用会返回错误，LLM 应在回复中说明工具暂时不可用。
- **MCP Server 重新发现**: MCP Server 的子工具列表变更后（工具新增/删除），已绑定的 toolRef 若指向已删除的子工具，行为同"工具被删除"。
- **并发工具调用**: LLM 在单次 Turn 中通过 parallel function calling 同时调用多个工具时，后端应并行执行所有工具调用，前端同时展示多个"调用中"卡片。
- **工具调用超时**: 工具调用超过合理时限未返回时，后端应中断调用并向 LLM 返回超时错误信息，前端工具卡片显示超时状态。
- **空工具列表**: Agent 编辑页面打开工具选择器时如果系统中没有任何 Active 工具，显示空状态提示"暂无可用工具，请先注册工具"。
- **大量工具绑定**: 当 Agent 绑定超过 20 个工具时，LLM 的 function calling 上下文可能溢出。系统应在保存时提示建议绑定工具数量上限（不阻止保存）。

## Requirements *(mandatory)*

### Functional Requirements

**后端 — 工具解析与 IChatClient 管道集成**

- **FR-001**: 系统 MUST 在创建 ChatClient Agent 的 `IChatClient` 管道时，根据 `LlmConfig.ToolRefs` 加载对应的工具注册记录，将每个工具转换为 `AIFunction` 定义（包含名称、描述、输入 Schema）。
- **FR-002**: 系统 MUST 通过 `FunctionInvocationChatClient`（或等效装饰器）包装 `IChatClient`，启用自动 Function Calling 循环——当 LLM 返回工具调用请求时，自动执行工具并将结果回传 LLM。
- **FR-003**: 系统 MUST 根据工具类型选择正确的调用协议：REST API 工具通过 HTTP 调用目标端点，MCP 子工具通过 MCP `tools/call` 方法调用。
- **FR-004**: 系统 MUST 在工具调用失败时（网络错误、超时、目标服务错误），将错误信息作为工具返回结果传回 LLM，而非中断对话流。
- **FR-005**: 系统 MUST 支持单次 LLM Turn 中的多工具调用（串行或并行，取决于 LLM 的 function calling 行为）。

**后端 — AG-UI 工具调用事件流**

- **FR-006**: 系统 MUST 在工具调用过程中通过 SSE 向前端发送标准 AG-UI 工具调用事件：`TOOL_CALL_START`（含 toolCallId, toolName）→ `TOOL_CALL_ARGS`（含调用参数 JSON）→ `TOOL_CALL_END`（含返回结果）。
- **FR-007**: 系统 MUST 在工具调用完成后、LLM 生成最终回复前，继续以 `TEXT_MESSAGE_START` → `TEXT_MESSAGE_CONTENT` → `TEXT_MESSAGE_END` 发送文本回复，确保工具调用和文本回复在同一个 Run 内有序发送。
- **FR-008**: 系统 MUST 在工具调用失败时，通过 `TOOL_CALL_END` 事件将错误信息作为结果发送给前端，而非发送 `RUN_ERROR`（对话仍可继续）。

**前端 — 工具选择器组件**

- **FR-009**: 系统 MUST 在 ChatClient Agent 创建/编辑表单中提供工具选择器组件，替代现有 GUID 手工输入方式。
- **FR-010**: 工具选择器 MUST 展示所有 Active 状态的可用工具，包括 REST API 工具和 MCP Server 已发现的子工具（McpToolItem），每项显示工具名称、描述、类型标签。
- **FR-011**: 工具选择器 MUST 提供实时搜索/过滤功能，支持按名称和描述模糊匹配。
- **FR-012**: 已选工具 MUST 以可视化标签或卡片形式展示在表单中，每个标签支持单独移除操作。

**前端 — 对话中工具调用展示**

- **FR-013**: 前端 MUST 在接收到 `TOOL_CALL_START` 事件时，在对话流中插入工具调用卡片，显示工具名称和"调用中"加载状态。
- **FR-014**: 前端 MUST 在接收到 `TOOL_CALL_ARGS` 事件时，在工具调用卡片中展示调用参数（JSON 格式），默认折叠，可点击展开。
- **FR-015**: 前端 MUST 在接收到 `TOOL_CALL_END` 事件时，更新工具调用卡片为"已完成"或"失败"状态，展示返回结果或错误信息，默认折叠。
- **FR-016**: 前端 MUST 支持在单次 Assistant 回复中展示多个工具调用卡片（多工具调用场景）。

### Key Entities

- **LlmConfig（值对象，已有）**: Agent 的 LLM 配置，`toolRefs: Guid[]` 字段存储绑定的工具 ID 列表。工具 ID 可指向 `ToolRegistration.Id`（REST API 工具）或 `McpToolItem.Id`（MCP 已发现子工具）。
- **ToolRegistration（聚合根，已有）**: 已注册的外部工具，包含连接配置和认证信息。作为工具绑定的目标实体。
- **McpToolItem（实体，已有）**: MCP Server 已发现的子工具项，包含工具名称、输入/输出 Schema。可作为更细粒度的绑定目标。
- **ChatMessage（前端类型，需扩展）**: 对话消息模型，需新增工具调用相关字段（toolCalls 数组），每个 toolCall 包含 toolCallId、toolName、args、result、status。

## Assumptions

- LLM 模型支持 Function Calling / Tool Use 能力（如 OpenAI GPT-4, Claude 3+）。不支持 function calling 的模型绑定工具后，LLM 将忽略工具定义，退化为纯文本对话。
- 工具调用的认证凭据复用 `ToolRegistration` 中已配置的 `AuthConfig`，无需用户在对话时额外提供凭据。
- 工具调用超时设定为 30 秒（默认值），超过此时间后中断调用并返回超时错误。
- `FunctionInvocationChatClient` 管理工具调用循环的最大迭代次数为 10 次（防止无限工具调用循环）。
- AG-UI 工具调用事件格式遵循 AG-UI Protocol Tool Events 规范（TOOL_CALL_START / TOOL_CALL_ARGS / TOOL_CALL_END）。
- REST API 工具的输入 Schema 取自 `ToolRegistration.ToolSchema.InputSchema`；MCP 子工具的输入 Schema 取自 `McpToolItem.InputSchema`。

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 管理员可在 60 秒内通过工具选择器完成 Agent 的工具绑定配置（从打开选择器到保存完成）。
- **SC-002**: 用户与绑定了工具的 ChatClient Agent 对话时，LLM 触发的工具调用从发起到结果返回全程在对话流中可见，工具调用卡片在 500 毫秒内出现在界面上。
- **SC-003**: 单次对话 Turn 中支持至少 5 个并行工具调用，所有工具调用结果均在对话中正确展示。
- **SC-004**: 工具调用失败时，对话不中断，用户在 3 秒内看到失败提示，可继续发送新消息。
- **SC-005**: 未绑定工具的 ChatClient Agent 对话行为完全不受影响，无性能退化。
- **SC-006**: 工具选择器支持管理 50+ 已注册工具而无明显性能退化或 UI 卡顿。
