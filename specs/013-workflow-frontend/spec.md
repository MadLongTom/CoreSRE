# Feature Specification: Workflow 前端管理页面

**Feature Branch**: `013-workflow-frontend`  
**Created**: 2025-07-25  
**Status**: Draft  
**Input**: User description: "为已完成的spec-011和spec-012的功能补全前端"

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Workflow 列表页面（Priority: P1）

作为平台管理员，我需要查看系统中所有已定义的 Workflow，了解其名称、描述、状态（Draft / Published）和创建时间，以便快速定位和管理工作流。

页面以表格形式展示 Workflow 列表，支持按状态（All / Draft / Published）筛选。每行显示名称、描述（截断）、状态标签、创建时间。表格提供"查看详情"和"删除"操作按钮。页面顶部有"新建 Workflow"按钮，导航到创建页面。

**Why this priority**: 列表页面是工作流管理的入口，是所有其他页面的导航起点，也是独立可用的最小 MVP——用户可以一目了然地浏览所有工作流。

**Independent Test**: 通过后端 API 创建数个不同状态的 Workflow，打开列表页面，验证数据正确展示、状态筛选生效、操作按钮可见。

**Acceptance Scenarios**:

1. **Given** 系统中存在多个 Workflow，**When** 用户导航到 `/workflows`，**Then** 页面以表格展示所有 Workflow 的名称、描述、状态徽章（Draft 灰色、Published 绿色）、创建时间
2. **Given** 列表页面已加载，**When** 用户将状态筛选切换为"Published"，**Then** 表格仅显示状态为 Published 的 Workflow
3. **Given** 系统中没有任何 Workflow，**When** 页面加载完成，**Then** 展示空状态引导：图标、"暂无工作流"文字、"新建 Workflow"按钮
4. **Given** API 请求失败（网络错误或服务端错误），**When** 页面尝试加载数据，**Then** 展示错误信息和"重试"按钮
5. **Given** 数据加载中，**When** 页面处于等待状态，**Then** 展示加载动画（骨架屏或 Spinner）

---

### User Story 2 — Workflow 创建页面与 DAG 图编辑器（Priority: P1）

作为平台管理员，我需要通过可视化界面创建新的 Workflow——包括填写名称和描述，并使用图形化 DAG 编辑器添加节点（Agent / Tool / Condition / FanOut / FanIn）和边（Normal / Conditional），从而定义工作流的执行流程。

页面分为两部分：顶部表单区域（名称、描述）和主体 DAG 编辑器区域。DAG 编辑器提供：画布拖拽移动和缩放、从节点面板拖入新节点、点击节点编辑属性（类型、关联的 AgentId/ToolId、配置）、连线创建边、选中边设置条件表达式。用户完成编辑后点击"保存"按钮，系统提交整个 Workflow（名称 + 描述 + Graph）到后端 API。

**Why this priority**: 创建功能是 CRUD 的核心，DAG 图编辑器是工作流管理的核心差异化功能，没有它用户无法定义执行流程。与列表页面并列为 P1 因为它是列表中"新建"操作的目标页面。

**Independent Test**: 打开创建页面，输入名称和描述，从节点面板拖入 2 个 Agent 节点并连线，保存后验证后端成功创建 Workflow，刷新列表页面确认新 Workflow 出现。

**Acceptance Scenarios**:

1. **Given** 用户导航到 `/workflows/new`，**When** 页面加载完成，**Then** 展示名称输入框（必填）、描述输入框（可选）和空白 DAG 编辑器画布（含节点面板）
2. **Given** DAG 编辑器已加载，**When** 用户从节点面板拖入一个 Agent 类型节点到画布，**Then** 画布上出现一个新的 Agent 节点，自动打开属性面板供用户配置关联的 AgentId
3. **Given** 画布上有两个节点，**When** 用户从节点 A 的输出端口拖线到节点 B 的输入端口，**Then** 两个节点之间创建一条 Normal 类型的边，并以箭头可视化表示方向
4. **Given** 画布上存在一条连接边，**When** 用户选中该边并在属性面板中将类型切换为 Conditional，**Then** 显示"条件表达式"输入框，用户可输入如 `$.severity == "high"` 的 JSON Path 条件
5. **Given** 用户已填写名称并在 DAG 编辑器中添加了至少 2 个节点和 1 条边，**When** 点击"保存"按钮，**Then** 系统提交 `POST /api/workflows`，成功后导航到该 Workflow 的详情页面
6. **Given** 用户提交保存请求，**When** 后端返回验证错误（如 DAG 包含环路、孤立节点、名称重复），**Then** 页面以错误消息展示具体原因，用户可修改后重新提交
7. **Given** 用户未填写名称就点击保存，**When** 前端校验执行，**Then** 名称字段显示"名称为必填项"错误提示，不发送 API 请求

---

### User Story 3 — Workflow 详情与编辑页面（Priority: P1）

作为平台管理员，我需要查看 Workflow 的完整信息（名称、描述、状态、DAG 图可视化）以及编辑 Draft 状态的 Workflow（修改名称、描述、DAG 图并保存更新）。

详情页面以只读模式展示 Workflow 信息：元数据区域显示名称、描述、状态徽章、创建时间和更新时间；DAG 可视化区域以只读模式展示工作流的节点和边、不同节点类型用不同颜色/图标区分。对于 Draft 状态的 Workflow 提供"编辑"按钮，点击切换为编辑模式（DAG 编辑器可交互、元数据可修改）。Published 状态的 Workflow 仅展示只读视图，同时提供"发布/取消发布"状态切换按钮。

**Why this priority**: 详情/编辑是 CRUD 闭环的关键环节。用户从列表进入详情查看 DAG 结构，对 Draft 工作流进行迭代编辑。与创建并列 P1 因为它复用 DAG 编辑器组件。

**Independent Test**: 通过 API 创建一个 Draft Workflow，打开详情页面验证只读 DAG 可视化正确展示，点击编辑按钮修改描述和 DAG 结构后保存，刷新页面确认修改持久化。

**Acceptance Scenarios**:

1. **Given** 用户导航到 `/workflows/{id}`（有效 ID），**When** 页面加载完成，**Then** 展示 Workflow 的名称、描述、状态徽章、创建/更新时间，以及 DAG 图的只读可视化（节点按类型着色、边显示箭头方向）
2. **Given** 详情页面展示一个 Draft 状态的 Workflow，**When** 用户点击"编辑"按钮，**Then** 名称和描述变为可编辑输入框，DAG 区域切换为编辑模式（可添加/删除/移动节点和边）
3. **Given** 编辑模式下用户修改了描述并新增了一个节点和连线，**When** 用户点击"保存"按钮，**Then** 系统提交 `PUT /api/workflows/{id}`，成功后切换回只读模式并展示更新后的数据
4. **Given** 编辑模式下用户点击"取消"按钮，**When** 页面处理取消操作，**Then** 恢复为只读模式，所有未保存的修改被丢弃
5. **Given** 详情页面展示一个 Published 状态的 Workflow，**When** 页面加载完成，**Then** 不显示"编辑"按钮，DAG 仅以只读模式展示
6. **Given** 用户访问不存在的 Workflow ID，**When** 后端返回 404，**Then** 展示"Workflow 未找到"提示并提供返回列表的链接
7. **Given** 详情页面展示一个 Draft Workflow，**When** 用户点击"发布"按钮，**Then** 系统发送 `PUT /api/workflows/{id}` 将状态更新为 Published，成功后页面刷新状态徽章并隐藏编辑按钮
8. **Given** 详情页面展示一个 Published Workflow，**When** 用户点击"取消发布"按钮，**Then** 系统将状态更新为 Draft，成功后页面刷新状态徽章并重新显示编辑按钮

---

### User Story 4 — Workflow 删除确认（Priority: P1）

作为平台管理员，我需要从列表或详情页面删除不再使用的 Workflow，删除前系统需要二次确认，防止误操作。仅 Draft 状态的 Workflow 可被删除。

用户点击删除按钮后，系统弹出确认对话框，显示 Workflow 名称并警告删除不可恢复。用户确认后发送 `DELETE /api/workflows/{id}`，成功后从列表中移除该行。如果尝试删除 Published 状态的 Workflow，按钮应被禁用或不显示，并提示"需先取消发布才能删除"。

**Why this priority**: 删除是 CRUD 闭环的必要环节。虽然是破坏性操作，但确认机制和状态限制保证安全性。

**Independent Test**: 在列表页面选中一个 Draft Workflow 并点击删除，确认对话框确认后验证该 Workflow 从列表中消失并持久化。

**Acceptance Scenarios**:

1. **Given** 列表页面展示多个 Workflow，**When** 用户点击某个 Draft Workflow 的"删除"按钮，**Then** 弹出确认对话框，显示"确认删除 Workflow '[名称]'？此操作不可恢复。"
2. **Given** 确认对话框已显示，**When** 用户点击"确认删除"，**Then** 系统发送 `DELETE /api/workflows/{id}`，成功后对话框关闭，该 Workflow 从列表中移除，展示成功提示
3. **Given** 确认对话框已显示，**When** 用户点击"取消"，**Then** 对话框关闭，列表不变
4. **Given** 列表页面中某个 Workflow 为 Published 状态，**When** 用户查看该行的操作列，**Then** 删除按钮被禁用或不显示，hover 提示"Published 工作流需先取消发布才能删除"
5. **Given** 后端返回 409 冲突（Workflow 正被执行引用），**When** 删除请求返回错误，**Then** 页面展示具体错误信息"该 Workflow 存在关联的执行记录，无法删除"

---

### User Story 5 — 工作流执行触发与状态查看（Priority: P2）

作为平台管理员，我需要在 Workflow 详情页面触发一次工作流执行——输入 JSON 数据作为初始输入，提交后查看执行列表和每次执行的状态与结果。

Workflow 详情页面（Published 状态）提供"执行"按钮。点击后弹出对话框，包含 JSON 输入编辑器（带语法高亮）和"执行"按钮。提交后系统发送 `POST /api/workflows/{id}/execute`，返回 202 并在页面下方的执行历史列表中新增一条记录（Pending 状态）。用户可定时刷新或手动刷新查看最新状态。

**Why this priority**: 执行触发是 spec-012 的核心前端入口，但依赖列表和详情页面已存在，因此排在 P2。仅 Published 工作流可执行。

**Independent Test**: 打开一个 Published Workflow 的详情页面，点击执行按钮，输入 JSON 并提交，验证执行记录出现在历史列表中且状态从 Pending 变化。

**Acceptance Scenarios**:

1. **Given** 用户在 Published Workflow 的详情页面，**When** 点击"执行"按钮，**Then** 弹出执行对话框，包含 JSON 编辑器（预填 `{}`）和"提交执行"按钮
2. **Given** 执行对话框已打开，**When** 用户输入有效 JSON 并点击提交，**Then** 系统发送 `POST /api/workflows/{id}/execute`，成功返回 202 后对话框关闭，执行历史列表顶部新增一条 Pending 状态的记录
3. **Given** 用户输入无效 JSON（语法错误），**When** 点击提交，**Then** JSON 编辑器下方显示"JSON 格式无效"错误，不发送 API 请求
4. **Given** 用户在 Draft 状态的 Workflow 详情页面，**When** 查看页面操作区域，**Then** "执行"按钮被禁用或不显示，提示"需先发布才能执行"
5. **Given** 后端返回 400（Draft 状态或引用缺失），**When** 执行请求返回错误，**Then** 对话框展示后端返回的具体错误信息

---

### User Story 6 — 执行历史列表（Priority: P2）

作为平台管理员，我需要在 Workflow 详情页面下方查看该 Workflow 的所有执行历史记录，了解每次执行的状态、开始时间和完成时间，以便追踪执行情况。

执行历史列表以表格形式展示，每行包含执行 ID（缩略）、状态徽章（Pending 灰色、Running 蓝色、Completed 绿色、Failed 红色、Canceled 黄色）、开始时间、完成时间。支持按状态筛选。点击某行可导航到执行详情页面。列表通过 `GET /api/workflows/{id}/executions` 获取数据。

**Why this priority**: 执行历史是执行功能的自然延伸，紧跟 US5。提供执行记录的浏览和导航能力。

**Independent Test**: 通过 API 为某个 Workflow 创建多个不同状态的执行记录，打开详情页面验证执行历史列表正确展示、状态筛选生效、点击行可导航。

**Acceptance Scenarios**:

1. **Given** 某 Workflow 有多条执行记录，**When** 用户打开该 Workflow 的详情页面，**Then** 页面下方展示执行历史表格，每行包含执行 ID（前 8 位）、状态徽章、开始时间、完成时间
2. **Given** 执行历史列表已加载，**When** 用户将状态筛选切换为"Failed"，**Then** 列表仅显示 Failed 状态的执行记录
3. **Given** 执行历史列表中存在记录，**When** 用户点击某行，**Then** 导航到 `/workflows/{id}/executions/{execId}` 执行详情页面
4. **Given** 某 Workflow 没有任何执行记录，**When** 页面加载完成，**Then** 执行历史区域显示"暂无执行记录"提示
5. **Given** 执行历史列表已加载，**When** 用户点击"刷新"按钮，**Then** 列表重新从 API 获取数据并更新展示

---

### User Story 7 — 执行详情页面（Priority: P2）

作为平台管理员，我需要查看某次 Workflow 执行的完整详情——包括整体执行状态、输入/输出数据、每个节点的执行状态和耗时，以便了解执行过程、定位失败节点。

执行详情页面分为三个区域：（1）执行概览——整体状态徽章、开始/完成时间、执行耗时、输入 JSON、输出 JSON / 错误信息；（2）DAG 执行可视化——基于保存的 DAG 快照，以配色区分节点执行状态（Pending 灰、Running 蓝脉冲、Completed 绿、Failed 红、Skipped 黄），点击节点查看节点级输入/输出/错误信息；（3）节点执行时间线——以列表或时间轴展示各节点的执行顺序和耗时。

**Why this priority**: 执行详情是执行功能的最终消费页面。它提供深入的调试和监控视角，但依赖 US5/US6 已实现。与执行历史列表并列 P2。

**Independent Test**: 通过 API 创建一次包含多个节点的执行记录（含成功和失败节点），打开执行详情页面验证 DAG 可视化按状态着色、点击节点展示输入/输出、时间线正确排列。

**Acceptance Scenarios**:

1. **Given** 用户导航到 `/workflows/{id}/executions/{execId}`（有效 ID），**When** 页面加载完成，**Then** 展示执行概览（状态徽章、开始/完成时间、耗时）、输入 JSON（可折叠展开）、输出 JSON 或错误信息
2. **Given** 执行详情页面已加载，**When** 用户查看 DAG 执行可视化区域，**Then** DAG 图基于执行时的图快照渲染，每个节点根据 NodeExecutionStatus 着色：Completed 绿色、Failed 红色、Skipped 浅灰色、Running 蓝色脉冲、Pending 灰色
3. **Given** DAG 执行可视化中某个节点为 Failed 状态，**When** 用户点击该节点，**Then** 侧面板或弹窗展示该节点的执行详情：节点 ID、状态、输入 JSON、错误信息、开始/完成时间
4. **Given** DAG 中存在 FanOut/FanIn 节点，**When** 页面渲染 DAG 可视化，**Then** 并行分支以扇形展开布局展示，FanIn 节点汇聚所有并行分支
5. **Given** 用户访问不存在的执行 ID，**When** 后端返回 404，**Then** 展示"执行记录未找到"提示并提供返回 Workflow 详情的链接
6. **Given** 执行详情中有多个节点执行记录，**When** 用户查看节点时间线区域，**Then** 以列表展示每个节点的名称、状态徽章、开始时间、耗时，按执行顺序排列

---

### User Story 8 — 前端路由扩展（Priority: P1）

作为前端开发者和用户，我需要前端路由系统扩展以支持 Workflow 相关的所有页面，并在侧边栏导航中添加"Workflows"菜单项，使用户可以便捷地访问工作流管理功能。

在现有 React Router 配置中新增 Workflow 路由：`/workflows`（列表）、`/workflows/new`（创建）、`/workflows/:id`（详情/编辑）、`/workflows/:id/executions/:execId`（执行详情）。侧边栏导航新增"Workflows"条目，位于现有"Agents"之后。

**Why this priority**: 路由是所有 Workflow 页面的入口基础，必须与页面同时实现。

**Independent Test**: 启动前端应用，验证所有 Workflow 路由路径正确加载页面，侧边栏 Workflows 导航正常工作，浏览器直链访问各路由路径正确渲染。

**Acceptance Scenarios**:

1. **Given** 应用已加载，**When** 用户点击侧边栏的"Workflows"导航项，**Then** 导航到 `/workflows` 路由，主内容区域展示 Workflow 列表页面
2. **Given** 用户在 Workflow 列表页面点击"新建 Workflow"，**Then** 导航到 `/workflows/new`
3. **Given** 用户在列表页面点击某 Workflow 的"查看详情"，**Then** 导航到 `/workflows/{id}`
4. **Given** 用户在执行历史列表点击某执行记录，**Then** 导航到 `/workflows/{id}/executions/{execId}`
5. **Given** 用户直接在浏览器地址栏输入 `/workflows/new`，**When** 页面加载，**Then** 正确展示创建页面（支持直链访问）

---

### Edge Cases

- 网络请求超时（超过 10 秒）时，页面展示超时错误并提供重试按钮
- 表单提交过程中网络断开，页面展示网络错误，保留用户已输入的表单数据（名称、描述），允许重试
- Workflow 名称中包含 HTML 特殊字符（如 `<script>`）时，页面正确转义显示，不触发 XSS
- 快速连续点击保存、删除或执行按钮时，系统防止重复请求（按钮禁用 + 请求去重）
- 列表页面在加载过程中展示骨架屏或加载动画，避免空白闪烁
- 详情页面编辑模式下，用户未保存就试图导航离开时，弹出确认提示（"有未保存的修改，确认离开？"）
- DAG 编辑器中用户尝试创建自环（同一节点连向自身），提示"不允许自环连线"并拒绝创建
- DAG 编辑器中用户拖入第一个节点时，自动标记为起始节点
- 执行对话框中 JSON 编辑器支持基本的语法高亮和错误提示（括号匹配、逗号遗漏）
- 执行详情页面在执行仍在 Running 状态时，提供手动刷新按钮获取最新节点状态
- DAG 可视化中节点数量较多时（超过 20 个），画布支持缩放和平移以保持可读性
- 条件表达式输入框提供格式提示，显示"格式: $.path == \"value\""作为 placeholder

## Requirements *(mandatory)*

### Functional Requirements

**路由与布局**

- **FR-001**: 前端必须扩展 React Router 配置，支持以下路由：`/workflows`（列表）、`/workflows/new`（创建）、`/workflows/:id`（详情/编辑）、`/workflows/:id/executions/:execId`（执行详情）
- **FR-002**: 前端必须在侧边栏导航中新增"Workflows"菜单项（lucide-react 图标），位于已有导航项之后，点击导航到 `/workflows`

**Workflow 列表页面**

- **FR-003**: Workflow 列表页面必须从 `GET /api/workflows` 获取数据并以表格形式展示，每行包含名称、描述（截断至 50 字符）、状态徽章、创建时间
- **FR-004**: Workflow 列表页面必须支持按状态筛选（All / Draft / Published），使用查询参数 `?status=XXX` 或前端过滤
- **FR-005**: Workflow 列表页面必须在无数据时展示空状态引导，在加载中展示加载动画，在出错时展示错误提示和重试按钮
- **FR-006**: Workflow 列表每行操作列必须包含"查看详情"按钮（导航到详情页）和"删除"按钮（仅 Draft 启用）

**Workflow 创建页面**

- **FR-007**: 创建页面必须提供名称输入框（必填、最大 200 字符）和描述输入框（可选、最大 2000 字符）
- **FR-008**: 创建页面必须提供可视化 DAG 编辑器，支持节点类型面板、画布拖放、连线、节点/边属性编辑
- **FR-009**: DAG 编辑器必须支持以下节点类型：Agent（需选择关联 AgentId）、Tool（需选择关联 ToolId）、Condition、FanOut、FanIn，不同类型以不同颜色和图标区分
- **FR-010**: DAG 编辑器必须支持两种边类型：Normal（默认）和 Conditional（需填写条件表达式 `<jsonPath> == <value>`），边类型可在属性面板中切换
- **FR-011**: 创建表单必须执行前端校验（名称非空且长度 ≤ 200、DAG 至少包含 2 个节点和 1 条边），校验不通过时展示错误信息
- **FR-012**: 创建表单提交后，必须处理后端返回的验证错误（400，如 DAG 环路、孤立节点、名称重复）并以用户可理解的错误消息展示

**Workflow 详情与编辑页面**

- **FR-013**: 详情页面必须从 `GET /api/workflows/{id}` 获取完整数据，以只读视图展示名称、描述、状态徽章、创建/更新时间
- **FR-014**: 详情页面必须以只读模式渲染 DAG 可视化——节点按类型着色、边显示箭头方向、不可编辑
- **FR-015**: Draft 状态的 Workflow 详情页面必须提供"编辑"按钮，切换编辑模式后名称/描述可修改、DAG 编辑器可交互
- **FR-016**: 编辑模式下"保存"操作通过 `PUT /api/workflows/{id}` 提交更新，成功后切换回只读模式
- **FR-017**: 详情页面必须提供状态切换按钮——Draft 显示"发布"按钮、Published 显示"取消发布"按钮，操作通过 `PUT /api/workflows/{id}` 更新状态

**Workflow 删除**

- **FR-018**: 删除操作必须弹出确认对话框，确认后发送 `DELETE /api/workflows/{id}`，成功后从列表中移除
- **FR-019**: Published 状态的 Workflow 删除按钮必须禁用或隐藏，附 tooltip 提示"需先取消发布"

**工作流执行触发**

- **FR-020**: Published Workflow 详情页面必须提供"执行"按钮，点击弹出执行对话框
- **FR-021**: 执行对话框必须包含 JSON 输入编辑器（支持语法高亮）和"提交执行"按钮
- **FR-022**: 执行提交前必须校验 JSON 格式合法性，不合法时展示错误提示
- **FR-023**: 执行提交成功（202 Accepted）后对话框关闭，执行历史列表刷新

**执行历史列表**

- **FR-024**: Workflow 详情页面下方必须展示执行历史列表，数据来源为 `GET /api/workflows/{id}/executions`
- **FR-025**: 执行历史每行展示执行 ID（前 8 位）、状态徽章（Pending 灰色 / Running 蓝色 / Completed 绿色 / Failed 红色 / Canceled 黄色）、开始时间、完成时间
- **FR-026**: 执行历史列表必须支持按状态筛选和手动刷新

**执行详情页面**

- **FR-027**: 执行详情页面必须从 `GET /api/workflows/{id}/executions/{execId}` 获取完整数据
- **FR-028**: 执行详情必须展示执行概览区域——整体状态徽章、开始/完成时间、执行耗时、输入 JSON（可折叠）、输出 JSON 或错误信息
- **FR-029**: 执行详情必须展示 DAG 执行可视化——基于图快照渲染，节点根据 NodeExecutionStatus 着色（Completed 绿 / Failed 红 / Skipped 浅灰 / Running 蓝脉冲 / Pending 灰）
- **FR-030**: DAG 执行可视化中点击节点必须展示节点执行详情（节点 ID、状态、输入 JSON、输出 JSON / 错误信息、开始/完成时间）
- **FR-031**: 执行详情必须展示节点执行时间线，以列表展示各节点的名称、状态、开始时间、耗时，按执行顺序排列

**通用 UI 要求**

- **FR-032**: 所有 API 请求在进行中时，对应的按钮/表单必须展示加载状态并禁用，防止重复提交
- **FR-033**: 前端必须使用 shadcn/ui 组件库和 Tailwind CSS 构建 UI，与现有页面保持一致的设计风格
- **FR-034**: 所有状态徽章必须使用颜色区分——Workflow 状态：Draft 灰色、Published 绿色；Execution 状态：Pending 灰色、Running 蓝色、Completed 绿色、Failed 红色、Canceled 黄色
- **FR-035**: DAG 编辑器和可视化必须支持画布缩放和平移操作

### Key Entities

- **WorkflowSummary（前端视图模型）**: 用于列表页面。包含 id、name、description、status（Draft / Published）、createdAt、updatedAt。对应后端 GET /api/workflows 的响应项。
- **WorkflowDetail（前端视图模型）**: 用于详情/编辑页面。包含 WorkflowSummary 的所有字段加上 graph（WorkflowGraph 对象，包含 nodes 数组和 edges 数组）。对应后端 GET /api/workflows/{id} 的完整响应。
- **WorkflowGraph（前端视图模型）**: DAG 图数据结构。包含 nodes 数组（WorkflowNode[]）和 edges 数组（WorkflowEdge[]）。映射到后端的 WorkflowGraphVO。
- **WorkflowNode（前端视图模型）**: DAG 中单个节点。包含 id、name、nodeType（Agent / Tool / Condition / FanOut / FanIn）、config（JSON 对象，Agent 节点含 agentId、Tool 节点含 toolId）、position（x, y 坐标，前端 DAG 编辑器用）。
- **WorkflowEdge（前端视图模型）**: DAG 中单条边。包含 id、sourceNodeId、targetNodeId、edgeType（Normal / Conditional）、condition（条件表达式字符串，仅 Conditional 类型）。
- **WorkflowExecutionSummary（前端视图模型）**: 用于执行历史列表。包含 id、status（Pending / Running / Completed / Failed / Canceled）、startedAt、completedAt。
- **WorkflowExecutionDetail（前端视图模型）**: 用于执行详情页面。包含 summary 所有字段加上 input（JSON）、output（JSON）、graphSnapshot（WorkflowGraph）、nodeExecutions（NodeExecution 数组）。
- **NodeExecution（前端视图模型）**: 单个节点的执行记录。包含 nodeId、status（Pending / Running / Completed / Failed / Skipped）、input（JSON）、output（JSON）、error（字符串）、startedAt、completedAt。
- **CreateWorkflowForm（前端表单模型）**: 创建/编辑表单的数据结构。包含 name（必填）、description（可选）、graph（WorkflowGraph，由 DAG 编辑器生成）。

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 管理员可通过前端完成完整的 Workflow CRUD 生命周期（创建→列表查看→详情查看→编辑→发布→取消发布→删除）而不依赖任何 API 调试工具
- **SC-002**: 管理员可通过前端触发 Workflow 执行并查看执行结果（执行→查看执行列表→查看执行详情→定位失败节点），全流程在前端完成
- **SC-003**: 页面首次加载（列表页）在 2 秒内完成渲染，用户可看到数据或明确的状态反馈
- **SC-004**: 用户可在 DAG 编辑器中 30 秒内完成一个包含 3 个节点、2 条边的简单工作流定义
- **SC-005**: 表单提交后，所有后端返回的错误信息 100% 展示给用户，用户无需查阅 API 文档即可理解并修正错误
- **SC-006**: DAG 可视化中执行状态配色让用户在 3 秒内识别出失败节点的位置
- **SC-007**: 所有页面在 1280px 及以上宽度的桌面浏览器中布局正确，无元素溢出或遮挡

## Assumptions

- 后端 API（spec-011 Workflow CRUD、spec-012 Workflow Execution Engine）已实现并可用，前端通过 Vite dev server 的 proxy 配置访问后端
- 前端技术栈使用现有的 React 19 + Vite 7 + TypeScript + Tailwind CSS v4 + shadcn/ui（new-york 风格），不引入新的 UI 框架
- DAG 可视化和编辑器使用 React Flow（@xyflow/react）库实现，该库是 React 生态中最成熟的图编辑器方案，需新增为项目依赖
- JSON 编辑器使用 textarea + 前端 JSON.parse 校验的简单方案，不引入 Monaco Editor 等重量级依赖
- 不实现用户认证/鉴权——所有页面无需登录即可访问
- 不实现国际化（i18n）——界面标签使用英文，与现有页面保持一致
- 不实现分页——预期 Workflow 数量 < 100，与后端 API 的决策一致
- 执行状态更新采用手动刷新方式（用户点击刷新按钮），不实现 WebSocket 或轮询自动刷新，以保持实现简单性
- DAG 编辑器中节点的位置坐标信息存储在前端 Graph 数据中，后端 WorkflowGraphVO 的 NodeVO 需扩展 position 字段支持（或前端 localStorage 持久化位置）
- Agent 和 Tool 节点配置时，AgentId / ToolId 的选择列表从现有 `GET /api/agents` 和 `GET /api/tools` 端点获取
