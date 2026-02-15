# Feature Specification: 工作流实时推送（SignalR）

**Feature Branch**: `017-workflow-signalr-push`  
**Created**: 2026-02-14  
**Status**: Draft  
**Priority**: P1（Phase 3 — 1 周）  
**Fixes**: D3  
**Depends on**: SPEC-081（数据流模型，执行栈中的 Notifier 钩子）  
**Input**: User description: "为工作流执行过程引入 SignalR 实时推送能力。执行引擎在节点执行前后通过 IWorkflowExecutionNotifier 接口发送事件，SignalRWorkflowNotifier 实现将事件推送到前端观察者。前端 DagExecutionViewer 接收事件后实时更新节点颜色和状态动画，无需刷新页面。"

## User Scenarios & Testing *(mandatory)*

### User Story 1 — 实时观察工作流执行进度 (Priority: P1)

用户在前端打开某个工作流的执行页面时，页面自动通过 WebSocket 连接加入该执行的观察组。当后端开始执行工作流时，前端实时接收每个节点的状态变化事件（开始、完成、失败、跳过），DAG 图上对应的节点颜色和动画随之实时更新——用户无需手动刷新或轮询即可掌握整个执行进度。

**Why this priority**: 这是本特性的核心价值——用户能实时感知执行进度，消除了当前必须手动刷新页面才能看到状态更新的痛点。

**Independent Test**: 启动一个包含 3 个顺序节点的工作流，在执行页面上观察节点颜色是否依次从灰色 → 蓝色（运行中） → 绿色（完成）过渡，且无需任何手动刷新操作。

**Acceptance Scenarios**:

1. **Given** 前端打开工作流执行页面并通过 SignalR 加入执行观察组，**When** 后端开始执行工作流，**Then** 前端立即收到 `ExecutionStarted` 事件，页面整体状态切换为 Running。
2. **Given** 工作流包含 3 个顺序节点，**When** 各节点依次执行，**Then** 前端依次收到每个节点的 `NodeExecutionStarted` → `NodeExecutionCompleted` 事件，节点颜色实时从灰色 → 蓝色（运行中） → 绿色（完成）过渡。
3. **Given** 某节点执行失败，**When** 前端收到 `NodeExecutionFailed` 事件，**Then** 该节点显示红色并展示错误信息，无需手动刷新。
4. **Given** 工作流中存在条件分支导致某节点被跳过，**When** 前端收到 `NodeExecutionSkipped` 事件，**Then** 该节点显示为灰色虚线样式，表示已跳过。

---

### User Story 2 — 断线重连与状态恢复 (Priority: P2)

用户在观察工作流执行过程中若暂时离开页面或网络中断后重新进入，页面能够自动恢复到当前最新的执行状态。系统通过 REST API 回退加载当前执行快照，确保用户不会因错过 SignalR 事件而看到过时的状态。

**Why this priority**: 保证实时推送体验的健壮性。仅依赖 WebSocket 事件流不够可靠，必须有 REST 回退机制处理断线、延迟加入等场景。

**Independent Test**: 在工作流执行进行一半时关闭页面，等待几秒后重新打开执行页面，验证 DAG 图上所有已完成/运行中的节点颜色是否正确反映当前状态。

**Acceptance Scenarios**:

1. **Given** 用户离开执行页面后工作流继续执行，**When** 用户重新进入执行页面，**Then** 页面通过 REST API 加载当前执行快照，所有节点状态正确显示（不依赖错过的 SignalR 事件）。
2. **Given** WebSocket 连接因网络抖动断开，**When** 连接自动重建，**Then** 客户端重新加入观察组，并通过 REST API 刷新一次当前状态以弥补断线期间遗漏的事件。

---

### User Story 3 — 多用户同时观察同一执行 (Priority: P3)

多个用户同时打开同一个工作流执行页面时，所有观察者都能接收到相同的实时事件流，且不互相干扰。用户离开页面时自动从观察组移除。

**Why this priority**: 协作场景中多人同时排查工作流问题时需要此能力。建立在 US1 基础上，属于自然扩展。

**Independent Test**: 在两个不同浏览器标签页中打开同一执行页面，执行工作流后验证两个页面是否同步接收到所有节点状态变更。

**Acceptance Scenarios**:

1. **Given** 两个用户同时加入同一执行的观察组，**When** 执行引擎推送节点状态事件，**Then** 两个用户的前端同时收到完全相同的事件序列，节点状态同步更新。
2. **Given** 某个用户关闭执行页面，**When** 该用户的 WebSocket 连接断开，**Then** 该用户自动从观察组移除，不影响其他观察者继续接收事件。

---

### Edge Cases

- 工作流执行在用户加入观察组之前已经完成——用户应看到最终状态（通过 REST 回退加载）。
- 执行引擎推送事件时观察组内无任何观察者——事件被静默丢弃，不影响执行流程。
- 单次执行的节点数量非常大（>100 个节点）——事件推送频率高，前端应能流畅处理批量状态更新。
- WebSocket 连接建立失败——前端应降级为仅使用 REST API 轮询获取执行状态，并向用户提示实时功能不可用。

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: 系统 MUST 提供 `IWorkflowExecutionNotifier` 接口，定义 7 个事件方法：`ExecutionStarted`、`NodeExecutionStarted`、`NodeExecutionCompleted`、`NodeExecutionFailed`、`NodeExecutionSkipped`、`ExecutionCompleted`、`ExecutionFailed`。
- **FR-002**: 系统 MUST 通过 `SignalRWorkflowNotifier` 实现 `IWorkflowExecutionNotifier`，将事件推送到与指定 `executionId` 关联的 SignalR 组。
- **FR-003**: 系统 MUST 提供 `WorkflowHub`（SignalR Hub），支持客户端通过 `JoinExecution(executionId)` 加入和 `LeaveExecution(executionId)` 离开执行观察组。
- **FR-004**: 系统 MUST 在 WebSocket 端点 `/hubs/workflow` 上暴露 `WorkflowHub`。
- **FR-005**: 执行引擎 MUST 在节点执行生命周期的关键时刻（开始前、完成后、失败、跳过）调用 `IWorkflowExecutionNotifier` 对应的事件方法。
- **FR-006**: 前端 MUST 在进入执行查看页面时自动建立 SignalR 连接并加入对应执行的观察组。
- **FR-007**: 前端 MUST 在离开执行查看页面时断开 SignalR 连接或离开观察组。
- **FR-008**: 前端 MUST 根据接收到的 SignalR 事件实时更新 DAG 图中节点的视觉状态（颜色、动画、图标）。
- **FR-009**: 前端 MUST 支持在 SignalR 连接不可用时降级为通过 REST API 获取执行状态。
- **FR-010**: `NodeExecutionCompleted` 事件 MUST 携带节点的输出数据，前端可缓存并展示。
- **FR-011**: `NodeExecutionFailed` 事件 MUST 携带错误信息字符串，前端可展示给用户。

### Key Entities

- **IWorkflowExecutionNotifier**: 通知接口，定义执行引擎与推送层之间的契约。执行引擎仅依赖此接口，不直接依赖 SignalR。
- **SignalRWorkflowNotifier**: 通知接口的 SignalR 实现，持有 `IHubContext<WorkflowHub>` 引用，按 `execution:{executionId}` 分组推送事件。
- **WorkflowHub**: SignalR Hub，管理客户端的组加入/离开操作。
- **执行观察组**: 以 `execution:{executionId}` 命名的 SignalR Group，容纳所有正在观察该执行的前端连接。

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 节点状态变更事件从后端发出到前端 DAG 图视觉更新完成，延迟不超过 500 毫秒（本地网络环境下）。
- **SC-002**: 包含 50 个节点的工作流执行过程中，前端 DAG 图刷新帧率保持在 30fps 以上，无明显卡顿。
- **SC-003**: 10 个并发观察者同时观察同一执行时，所有观察者均能收到完整的事件序列，无事件丢失。
- **SC-004**: 用户在执行进行中离开页面后重新进入，3 秒内 DAG 图恢复到当前真实执行状态。
- **SC-005**: WebSocket 连接失败时，前端在 5 秒内自动降级为 REST 轮询模式，并向用户展示提示信息。

## Assumptions

- SPEC-081 已实现数据流模型和执行栈中的 Notifier 钩子点，执行引擎已具备在节点执行生命周期调用 `IWorkflowExecutionNotifier` 的能力。
- 当前执行状态可通过现有的工作流执行 REST API 查询获得（用于断线恢复场景），无需额外开发状态快照接口。
- SignalR 使用默认的 WebSocket 传输，不需要配置 Azure SignalR Service 等云端背板；单实例部署即可满足当前需求。
- 前端 `DagExecutionViewer` 组件已存在，本特性在其基础上增加实时事件绑定，不涉及 DAG 图本身的重构。
