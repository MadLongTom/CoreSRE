# Feature Specification: A2A AgentCard 自动解析

**Feature Branch**: `008-a2a-card-resolve`  
**Created**: 2026-02-10  
**Status**: Draft  
**Input**: User description: "添加A2A Agent时，不应该让用户填AgentCard，而是应该通过用户输入的endpoint使用A2ACardResolver解析，并提供一个选项，是否将AgentCard中的Url覆写为用户提供的Url"

## User Scenarios & Testing *(mandatory)*

### User Story 1 — 通过 Endpoint 自动解析 AgentCard (Priority: P1) 🎯 MVP

用户在创建 A2A Agent 时，输入远程 Agent 的 Endpoint URL 后，系统自动从该端点获取 AgentCard 信息（技能、接口、安全方案等），无需用户手动逐项填写。解析成功后，表单自动填充 AgentCard 中的所有字段，用户可在提交前审阅和微调。

**Why this priority**: 这是核心需求的最小可用单元——消除了手动填写 AgentCard 的主要痛点，直接提升 A2A Agent 注册效率。

**Independent Test**: 输入一个有效的 A2A Agent endpoint URL → 点击"解析"按钮 → AgentCard 字段自动填充 → 用户审阅后提交创建。

**Acceptance Scenarios**:

1. **Given** 用户在创建 A2A Agent 的表单页面，**When** 用户输入合法 Endpoint URL 并点击"解析"按钮，**Then** 系统从该 URL 获取 AgentCard，并将解析到的技能、接口、安全方案等信息自动填充到表单中。
2. **Given** 用户输入了 Endpoint URL，**When** 目标端点不可达或返回无效数据，**Then** 系统显示友好的错误提示（如"无法连接到该端点"或"返回数据格式不符合 A2A 规范"），表单保持空白状态，用户可修改 URL 重试。
3. **Given** AgentCard 已自动填充，**When** 用户手动修改了部分字段（如编辑技能描述），**Then** 修改后的值在提交时生效，不会被覆盖。
4. **Given** AgentCard 已自动填充，**When** 用户再次点击"解析"，**Then** 表单中的 AgentCard 字段被新解析结果覆盖。

---

### User Story 2 — Endpoint URL 覆写选项 (Priority: P1)

A2A 协议的 AgentCard 内部包含一个 URL 字段，它可能与用户输入的 Endpoint URL 不同（例如 AgentCard 中记录的是内网地址，而用户输入的是外网代理地址）。系统提供一个选项让用户选择是否用自己输入的 URL 覆盖 AgentCard 中的 URL。

**Why this priority**: 与 US1 紧密耦合且实现简单，是用户明确要求的功能，覆盖场景在代理/网关部署中非常常见。

**Independent Test**: 解析 AgentCard 后，在表单中看到一个"使用我输入的 URL 覆盖 AgentCard 中的 URL"开关 → 打开时，保存的 Endpoint 为用户输入值 → 关闭时，保存 AgentCard 原始 URL。

**Acceptance Scenarios**:

1. **Given** AgentCard 已解析成功且其内部 URL 与用户输入的 Endpoint URL 不同，**When** 用户保持"覆写 URL"选项开启（默认开启），**Then** 创建的 Agent 的 Endpoint 使用用户输入的 URL。
2. **Given** AgentCard 已解析成功且其内部 URL 与用户输入的 Endpoint URL 不同，**When** 用户关闭"覆写 URL"选项，**Then** 创建的 Agent 的 Endpoint 使用 AgentCard 中的原始 URL。
3. **Given** AgentCard 中的 URL 与用户输入的 URL 完全相同，**When** AgentCard 解析完成，**Then** 覆写选项不显示（因为没有差异）。

---

### User Story 3 — AgentCard 解析结果的名称与描述预填 (Priority: P2)

AgentCard 中通常包含 Agent 的名称和描述信息，系统可以将这些信息预填到创建表单的"基本信息"区域，减少用户重复输入。

**Why this priority**: 提升便利性，但用户可以选择不依赖此功能手动填写。

**Independent Test**: 解析 AgentCard 后，检查"基本信息"卡片中的名称和描述是否被预填（仅在字段为空时预填，不覆盖用户已填内容）。

**Acceptance Scenarios**:

1. **Given** 用户尚未填写名称和描述，**When** AgentCard 解析成功且包含名称和描述，**Then** 名称和描述字段被自动填充。
2. **Given** 用户已手动填写了名称，**When** AgentCard 解析成功，**Then** 名称字段保持用户输入不变，不被覆盖。

---

### Edge Cases

- 目标端点返回 HTTP 错误（404、500 等）时，显示对应的错误信息。
- 目标端点返回有效 JSON 但不符合 A2A AgentCard 规范时，提示格式不匹配。
- 网络超时时（解析请求应有合理超时限制），显示超时提示并允许重试。
- Endpoint URL 格式不合法时（非 http/https），在前端阻止解析请求并提示。
- AgentCard 中的 Skills 列表为空时，正常处理，表单中技能区域显示为空。
- 解析过程中用户修改了 URL 文本框内容，应取消正在进行的解析请求。

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: 系统必须提供一个后端接口，接收 A2A Agent 的 Endpoint URL，从该 URL 获取并解析 AgentCard 信息，返回结构化数据给前端。
- **FR-002**: 前端在 A2A Agent 创建表单中必须提供"解析"按钮，位于 Endpoint URL 输入框旁边，点击后调用解析接口。
- **FR-003**: 解析成功后，系统必须将 AgentCard 中的技能（skills）、接口（interfaces）、安全方案（securitySchemes）自动填充到对应的表单字段中。
- **FR-004**: 系统必须提供"覆写 URL"选项（复选框或开关），当 AgentCard 中的 URL 与用户输入的 URL 不同时显示，允许用户选择保存哪个 URL 作为 Agent 的 Endpoint；默认开启（使用用户输入的 URL）。
- **FR-005**: 解析成功后，如果用户的名称和描述字段为空，系统应将 AgentCard 中的名称和描述预填到基本信息区域。
- **FR-006**: 解析失败时，系统必须向用户显示清晰的错误信息，且不影响表单的其他操作。
- **FR-007**: 解析请求必须有超时限制，防止因目标端点无响应而导致用户长时间等待。
- **FR-008**: 前端必须在解析过程中显示加载状态，防止重复点击，并在 URL 变更后重新解析时取消上一次请求。

### Key Entities

- **AgentCard（A2A 协议）**: 远程 A2A Agent 的自描述元数据，包含名称、描述、URL、版本、技能列表、支持的接口、安全要求、能力声明等。系统从远程端点的标准路径获取此 JSON 数据。
- **AgentRegistration（现有）**: 本地持久化的 Agent 注册记录，存储简化后的 AgentCard 子集（skills、interfaces、securitySchemes）以及独立的 endpoint 字段。解析后的 AgentCard 会被映射到此实体。

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 用户创建 A2A Agent 时，输入 Endpoint URL 后 10 秒内可看到自动填充的 AgentCard 信息。
- **SC-002**: 90% 以上的有效 A2A Agent Endpoint 能成功解析并填充表单（不包括目标端点本身不可用的情况）。
- **SC-003**: 用户创建 A2A Agent 的操作步骤从手动填写所有字段减少到仅输入 URL + 审阅确认，整体操作时间减少 50% 以上。
- **SC-004**: 解析失败场景中，100% 的情况向用户显示可理解的错误信息并提供重试入口。

## Assumptions

- A2A Agent 的远程端点遵循标准 A2A 协议，在 `/.well-known/agent.json` 路径（或类似约定路径）提供 AgentCard JSON。
- 系统后端能直接访问用户指定的 Endpoint URL（无需特殊网络配置或代理）。
- A2A SDK（`Microsoft.Agents.Client`）中的 `A2ACardResolver` 类可用于解析远程 AgentCard，无需自行实现 HTTP 获取和解析逻辑。
- AgentCard 中的完整字段集将映射到现有简化的 AgentCard 值对象结构，未在当前模型中建模的字段（如 `capabilities`、`provider`、`documentationUrl` 等）暂不存储。
- 本次改动范围仅影响 A2A Agent 的创建流程，现有的 Agent 编辑页面暂不纳入。
