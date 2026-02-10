# Feature Specification: 前端管理页面（Agent Registry + 搜索）

**Feature Branch**: `005-frontend-pages`  
**Created**: 2026-02-10  
**Status**: Draft  
**Input**: User description: "根据之前4个spec的后端实现，写出前端对应的页面"

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Agent 列表页面（Priority: P1） 🎯 MVP

作为平台管理员，我需要一个 Agent 列表页面，在表格中查看所有已注册的 Agent（名称、类型、状态、创建时间），支持按类型筛选，以便快速掌握平台当前的 Agent 资源全貌。

页面加载时从 `GET /api/agents` 获取数据，以表格形式展示每条 Agent 的摘要信息。页面顶部提供类型筛选下拉（All / A2A / ChatClient / Workflow），切换时重新请求 `GET /api/agents?type=XXX`。表格每行右侧提供"查看详情"和"删除"操作按钮。

**Why this priority**: 列表页面是所有管理操作的入口。没有列表页面，用户无法发现 Agent、无法进入详情/编辑流程、无法执行删除。这是前端 MVP 的核心页面。

**Independent Test**: 后端已有若干 Agent 注册数据时，打开列表页面，验证所有 Agent 以表格形式正确展示，类型筛选功能正常工作。

**Acceptance Scenarios**:

1. **Given** 后端已注册 3 个 Agent（A2A、ChatClient、Workflow 各 1 个），**When** 用户打开 Agent 列表页面，**Then** 页面展示一个包含 3 行的表格，每行显示 Agent 的名称、类型标签（带颜色区分）、状态标签、创建时间
2. **Given** 列表页面已展示所有 Agent，**When** 用户在类型筛选下拉中选择"A2A"，**Then** 表格仅展示 A2A 类型的 Agent，其他类型的 Agent 被过滤隐藏
3. **Given** 筛选状态为"A2A"，**When** 用户将筛选切换回"All"，**Then** 表格重新展示所有类型的 Agent
4. **Given** 后端无任何 Agent 注册，**When** 用户打开列表页面，**Then** 页面展示空状态提示（如"暂无 Agent，点击右上角创建"），引导用户进行首次注册
5. **Given** 后端 API 不可达或返回错误，**When** 页面尝试加载 Agent 列表，**Then** 页面展示错误提示（如"加载失败，请重试"），并提供重试按钮

---

### User Story 2 — Agent 注册页面（Priority: P1）

作为平台管理员，我需要一个表单页面来注册新的 Agent。表单根据选择的 Agent 类型动态展示不同的配置字段，提交后 Agent 出现在列表中。

页面包含一个多步骤表单：第一步选择 Agent 类型（A2A / ChatClient / Workflow），第二步填写基础信息（名称、描述）和类型特有的配置字段。A2A 类型需填写 endpoint 和 AgentCard（skills、interfaces、securitySchemes）；ChatClient 类型需填写 LLM 配置（modelId、instructions、toolRefs）；Workflow 类型需填写 workflowRef。表单提交到 `POST /api/agents`。

**Why this priority**: 注册是 Agent 生命周期的起点。没有注册页面，管理员只能通过 API 工具（如 Postman）手动注册，严重影响使用体验。

**Independent Test**: 打开注册页面，选择类型、填写表单、提交，验证成功后自动跳转到列表页面并展示新 Agent。

**Acceptance Scenarios**:

1. **Given** 用户在列表页面点击"新建 Agent"按钮，**When** 进入注册页面，**Then** 页面展示类型选择卡片（A2A / ChatClient / Workflow），各类型附带简短描述
2. **Given** 用户选择 A2A 类型，**When** 进入表单填写阶段，**Then** 表单展示 name、description、endpoint 字段，以及 AgentCard 区域（skills 动态添加/删除行、interfaces 动态添加/删除行、securitySchemes 动态添加/删除行）
3. **Given** 用户选择 ChatClient 类型，**When** 进入表单填写阶段，**Then** 表单展示 name、description 字段和 LLM 配置区域（modelId、instructions 多行文本框、toolRefs）
4. **Given** 用户填写完 A2A Agent 所有必填字段并提交，**When** 后端返回 201，**Then** 页面展示成功提示并自动导航回 Agent 列表页面，新 Agent 出现在表格中
5. **Given** 用户提交表单但缺少必填字段（如 A2A 类型未填 endpoint），**When** 后端返回 400 错误，**Then** 表单在对应字段下方展示错误信息，不清除用户已填写的其他数据
6. **Given** 用户提交的 Agent 名称已被占用，**When** 后端返回 409，**Then** 页面展示"名称已被占用"的错误提示

---

### User Story 3 — Agent 详情与编辑页面（Priority: P1）

作为平台管理员，我需要查看单个 Agent 的完整详情，并能编辑其配置信息（名称、描述、类型特有配置），以便在 Agent 服务升级或配置变更时保持注册信息的准确性。

详情页面从 `GET /api/agents/{id}` 获取完整数据，以只读视图展示 Agent 的所有信息。页面提供"编辑"按钮，点击后切换为编辑模式（相同页面，字段变为可编辑）。编辑完成后通过 `PUT /api/agents/{id}` 提交更新。Agent 类型字段在编辑模式下始终只读（不可变更）。

**Why this priority**: 详情和编辑是 CRUD 生命周期的核心环节。管理员需要查看 Agent 的完整配置（尤其是 AgentCard 中的复杂嵌套数据）并能在线修改。

**Independent Test**: 从列表页面点击某个 Agent，进入详情页面验证完整数据展示。点击编辑，修改描述后提交，返回详情页面验证更新生效。

**Acceptance Scenarios**:

1. **Given** 列表页面展示一个 A2A Agent，**When** 用户点击该行的"查看详情"按钮，**Then** 导航到详情页面，展示 Agent 的完整信息：名称、描述、类型、状态、endpoint、AgentCard（skills 列表、interfaces 列表、securitySchemes 列表）、创建时间、更新时间
2. **Given** 详情页面处于只读模式，**When** 用户点击"编辑"按钮，**Then** 页面切换为编辑模式，可编辑字段变为表单输入控件，类型字段保持只读并显示禁用样式
3. **Given** 编辑模式下用户修改了 Agent 描述和 skills 列表，**When** 点击"保存"按钮，**Then** 系统发送 `PUT /api/agents/{id}` 请求，成功后切换回只读模式并展示更新后的数据
4. **Given** 编辑模式下用户点击"取消"按钮，**When** 页面处理取消操作，**Then** 页面恢复为只读模式，所有未保存的修改被丢弃
5. **Given** 用户访问一个不存在的 Agent ID，**When** 后端返回 404，**Then** 页面展示"Agent 未找到"提示并提供返回列表的链接

---

### User Story 4 — Agent 删除确认（Priority: P1）

作为平台管理员，我需要从列表或详情页面删除不再使用的 Agent，删除前系统需要二次确认，防止误操作。

用户点击删除按钮后，系统弹出确认对话框，显示 Agent 名称并警告删除不可恢复。用户确认后发送 `DELETE /api/agents/{id}`，成功后从列表中移除该行（无需刷新页面）。

**Why this priority**: 删除是 CRUD 闭环的最后一环。必须实现确认机制防止误删。

**Independent Test**: 在列表页面点击某个 Agent 的删除按钮，确认删除后验证该 Agent 从列表中消失，再次刷新列表确认持久化删除。

**Acceptance Scenarios**:

1. **Given** 列表页面展示多个 Agent，**When** 用户点击某行的"删除"按钮，**Then** 弹出确认对话框，显示"确认删除 Agent '[名称]'？此操作不可恢复。"
2. **Given** 确认对话框已显示，**When** 用户点击"确认删除"，**Then** 系统发送 `DELETE /api/agents/{id}`，成功后对话框关闭，该 Agent 从表格中移除，展示成功提示
3. **Given** 确认对话框已显示，**When** 用户点击"取消"，**Then** 对话框关闭，Agent 列表不变
4. **Given** 删除请求发送中，**When** 后端处理中，**Then** 删除按钮显示加载状态，防止重复点击

---

### User Story 5 — Agent 技能搜索页面（Priority: P2）

作为平台管理员或 Orchestrator，我需要在页面上通过搜索框输入自然语言描述搜索 Agent 的技能，快速找到最匹配的 Agent。搜索结果以卡片或列表形式展示，每个结果附带匹配到的 skill 信息。

页面顶部提供搜索输入框，用户输入查询文本后触发 `GET /api/agents/search?q={query}`。搜索结果展示每个匹配 Agent 的名称、状态，以及匹配到的 skills 列表（skill name + description 高亮匹配词）。搜索模式标签指示当前使用的是关键词匹配还是语义搜索。

**Why this priority**: 搜索是高级发现功能。列表页面的类型筛选已能满足基本浏览需求，搜索提供更精确的按技能查找能力。依赖 SPEC-003 后端搜索 API。

**Independent Test**: 注册含不同 skills 的多个 A2A Agent，在搜索页面输入关键词，验证搜索结果仅包含匹配的 Agent 及其匹配 skill。

**Acceptance Scenarios**:

1. **Given** 搜索页面已加载，**When** 用户在搜索框中输入"customer"并提交，**Then** 页面展示包含"customer"相关 skill 的 A2A Agent 列表，每个结果显示 Agent 名称和匹配的 skills
2. **Given** 搜索结果已展示，**When** 搜索结果中某个 Agent 的 skill description 包含搜索词，**Then** 匹配部分文字高亮显示
3. **Given** 用户搜索一个无匹配结果的关键词，**When** 后端返回空结果，**Then** 页面展示"未找到匹配的 Agent 技能"提示
4. **Given** 用户未输入任何文本就点击搜索，**When** 提交空查询，**Then** 页面在搜索框下方展示"请输入搜索内容"的验证提示，不发送 API 请求
5. **Given** 搜索结果展示中，**When** 用户点击某个 Agent 结果卡片，**Then** 导航到该 Agent 的详情页面

---

### User Story 6 — 前端路由与布局框架（Priority: P1）

作为前端开发者和用户，我需要前端应用具备完整的路由系统和统一的布局框架——包含侧边栏导航、页面标题区域和内容区域，使不同页面（列表、注册、详情、搜索）通过 URL 可直接访问，且具有一致的视觉体验。

应用使用 React Router 实现客户端路由。布局包含固定侧边栏（导航菜单：Agent 列表、Agent 搜索）和主内容区域。路由配置映射各页面到对应的 URL 路径。

**Why this priority**: 路由和布局是所有页面的基础骨架。没有路由系统，多个页面无法共存；没有统一布局，用户体验碎片化。这是前端架构的第一步。

**Independent Test**: 启动前端应用，验证各路由路径正确加载对应页面，侧边栏导航点击正常切换页面，浏览器刷新保持当前路由。

**Acceptance Scenarios**:

1. **Given** 用户访问根路径 `/`，**When** 页面加载，**Then** 自动重定向到 `/agents` Agent 列表页面
2. **Given** 应用已加载，**When** 用户点击侧边栏的"Agent 列表"导航项，**Then** 导航到 `/agents` 路由，主内容区域展示 Agent 列表页面
3. **Given** 应用已加载，**When** 用户点击侧边栏的"Agent 搜索"导航项，**Then** 导航到 `/agents/search` 路由，主内容区域展示搜索页面
4. **Given** 用户在列表页面点击"新建 Agent"按钮，**When** 导航到注册页面，**Then** URL 变为 `/agents/new`
5. **Given** 用户在列表页面点击某 Agent 的"查看详情"，**When** 导航到详情页面，**Then** URL 变为 `/agents/{id}`
6. **Given** 用户直接在浏览器地址栏输入 `/agents/new`，**When** 页面加载，**Then** 正确展示 Agent 注册页面（支持直链访问）
7. **Given** 用户访问不存在的路由路径（如 `/unknown`），**When** 页面加载，**Then** 展示 404 页面并提供返回首页的链接

---

### Edge Cases

- 网络请求超时（超过 10 秒）时，页面展示超时错误并提供重试按钮
- 表单提交过程中网络断开，页面展示网络错误，保留用户输入的表单数据，允许重试
- Agent 名称中包含 HTML 特殊字符（如 `<script>`）时，页面正确转义显示，不触发 XSS
- 快速连续点击删除按钮或提交按钮时，系统防止重复请求（按钮禁用 + 请求去重）
- 列表页面在加载过程中展示骨架屏或加载动画，避免空白闪烁
- 详情页面编辑模式下，用户未保存就试图导航离开时，弹出确认提示（"有未保存的修改，确认离开？"）
- 搜索输入支持防抖（300ms），避免每次按键都触发 API 请求
- 移动端视口下侧边栏自动收起，可通过汉堡菜单展开

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: 前端必须使用 React Router 实现客户端路由，支持以下路由：`/agents`（列表）、`/agents/new`（注册）、`/agents/:id`（详情/编辑）、`/agents/search`（搜索）
- **FR-002**: 前端必须提供统一布局框架，包含固定侧边栏导航和主内容区域，所有页面共享该布局
- **FR-003**: Agent 列表页面必须从 `GET /api/agents` 获取数据并以表格形式展示，每行包含名称、类型、状态、创建时间
- **FR-004**: Agent 列表页面必须支持按 Agent 类型筛选（All / A2A / ChatClient / Workflow），使用 `GET /api/agents?type=XXX` 查询参数
- **FR-005**: Agent 列表页面必须在无数据时展示空状态引导，在加载中展示加载动画，在出错时展示错误提示和重试按钮
- **FR-006**: Agent 注册页面必须提供分步表单——先选择 Agent 类型，再填写类型特有的配置字段
- **FR-007**: 注册表单必须根据选择的 Agent 类型动态展示不同字段：A2A（endpoint、agentCard）、ChatClient（llmConfig）、Workflow（workflowRef）
- **FR-008**: 注册表单中 AgentCard 的 skills、interfaces、securitySchemes 必须支持动态添加和删除条目
- **FR-009**: 注册表单必须执行前端校验（必填字段非空、名称长度 ≤ 200、endpoint 格式合法），校验不通过时在字段旁展示错误信息
- **FR-010**: 注册表单提交后，必须处理后端返回的验证错误（400）和名称冲突错误（409），将错误信息展示给用户
- **FR-011**: Agent 详情页面必须从 `GET /api/agents/{id}` 获取完整数据，以只读视图展示所有字段，包括 AgentCard / LlmConfig 等嵌套数据
- **FR-012**: Agent 详情页面必须支持切换到编辑模式，编辑后通过 `PUT /api/agents/{id}` 提交更新
- **FR-013**: 编辑模式下 Agent 类型字段必须为只读不可变更
- **FR-014**: Agent 删除操作必须显示确认对话框，确认后发送 `DELETE /api/agents/{id}`，成功后从列表中移除（乐观更新或重新请求）
- **FR-015**: 技能搜索页面必须提供搜索输入框，提交后调用 `GET /api/agents/search?q={query}`，展示匹配 Agent 和匹配 skill 列表
- **FR-016**: 搜索输入必须支持防抖（300ms 延迟触发），并在客户端校验查询文本非空且长度 ≤ 500 字符
- **FR-017**: 搜索结果中匹配的 skill 关键词必须高亮显示
- **FR-018**: 所有 API 请求在进行中时，对应的按钮/表单必须展示加载状态并禁用，防止重复提交
- **FR-019**: 前端必须使用 shadcn/ui 组件库和 Tailwind CSS 构建 UI，保持一致的设计风格
- **FR-020**: 所有列表和搜索结果中的 Agent 类型必须以颜色标签区分（如 A2A 蓝色、ChatClient 绿色、Workflow 紫色）

### Key Entities

- **Agent（前端视图模型）**: 对应后端 AgentDetail 和 AgentSummary DTO。列表视图使用摘要信息（id、name、agentType、status、createdAt），详情视图使用完整信息（含 agentCard、llmConfig、workflowRef 等嵌套数据）。
- **AgentSearchResult（前端视图模型）**: 对应后端 AgentSearchResultDto。包含 Agent 摘要信息和匹配的 skills 列表（matchedSkills）。用于搜索结果页面展示。
- **RegisterAgentForm（前端表单模型）**: 注册表单的数据结构。包含通用字段（name、description、agentType）和类型条件字段（endpoint、agentCard、llmConfig、workflowRef）。根据 agentType 动态校验必填项。

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 管理员可通过前端完成完整的 Agent CRUD 生命周期（注册→列表查看→详情查看→编辑→删除）而不依赖任何 API 调试工具
- **SC-002**: 页面首次加载（列表页）在 2 秒内完成渲染，用户可看到数据或明确的状态反馈
- **SC-003**: 表单提交后，所有后端返回的错误信息 100% 展示给用户，用户无需查阅 API 文档即可理解并修正错误
- **SC-004**: 按类型筛选 Agent 列表的交互响应在 500 毫秒内完成
- **SC-005**: 搜索操作从用户停止输入到结果展示不超过 2 秒
- **SC-006**: 所有页面在 1280px 及以上宽度的桌面浏览器中布局正确，无元素溢出或遮挡

## Assumptions

- 后端 API（SPEC-001 Agent CRUD、SPEC-003 Agent 搜索）已实现并可用，前端通过 Vite dev server 的 proxy 配置访问后端
- SPEC-000（Aspire AppHost）和 SPEC-004（AgentSession 持久化）无前端页面需求——SPEC-000 是基础设施编排（开发者通过终端启动），SPEC-004 是内部 Framework 集成（无外部 API 端点）
- 前端技术栈使用现有的 React 19 + Vite 7 + TypeScript + Tailwind CSS v4 + shadcn/ui（new-york 风格），不引入新的 UI 框架
- React Router 作为路由库（需新增依赖），使用最新稳定版
- 不实现用户认证/鉴权——所有页面无需登录即可访问，认证由后续安全模块统一处理
- 不实现国际化（i18n）——界面语言为中英文混合（UI 标签使用英文，描述可使用中文）
- 不实现分页——与后端 SPEC-001/003 的决策一致，预期 Agent 数量 < 100
- Agent 列表页面使用完整的表格组件（shadcn/ui Table），不使用虚拟滚动（数据量可控）
