# Feature Specification: Team Agent 领域模型与 CRUD

**Feature Branch**: `018-team-agent-model`  
**Created**: 2026-02-17  
**Status**: Draft  
**Priority**: P1（Phase 1 — 1 周）  
**Depends on**: SPEC-001（Agent CRUD）, SPEC-006（LLM Provider 配置）  
**Input**: [TEAM-AGENT-SPEC-INDEX](../../docs/specs/TEAM-AGENT-SPEC-INDEX.md)

## User Scenarios & Testing *(mandatory)*

### User Story 1 — 注册 Team Agent (Priority: P1)

用户在 Agent 管理页面选择创建 Team 类型 Agent，选择编排模式（Sequential / Concurrent / RoundRobin / Handoffs / Selector / MagneticOne），配置参与者 Agent 列表和模式特定参数，提交后系统创建包含完整 TeamConfig 的 Agent 注册记录。

**Why this priority**: 这是 Team Agent 所有后续功能的领域基础——没有持久化模型就无法执行。

**Independent Test**: 通过 API 创建一个 RoundRobin Team Agent（3 个参与者，MaxIterations=5），然后查询该 Agent，验证返回的 DTO 包含完整的 TeamConfig。

**Acceptance Scenarios**:

1. **Given** 用户选择 AgentType=Team，TeamMode=RoundRobin，选择 3 个已注册的参与者 Agent，**When** 提交注册请求，**Then** Agent 成功创建，状态为 Registered，`TeamConfig` JSONB 持久化到 PostgreSQL。
2. **Given** 用户选择 TeamMode=Sequential 但仅选择 1 个参与者，**When** 提交注册请求，**Then** 返回 400 错误，提示 "Sequential Team requires at least 2 participants"。
3. **Given** 用户选择 TeamMode=Handoffs 但未设置 InitialAgentId，**When** 提交注册请求，**Then** 返回 400 错误，提示必须指定初始 Agent。
4. **Given** 已创建的 Team Agent，**When** 通过 `GET /api/agents/{id}` 查询，**Then** 返回完整 DTO 包含 `teamConfig` 及其所有子属性。

### User Story 2 — 更新和删除 Team Agent (Priority: P1)

用户可以更新 Team Agent 的配置（如增减参与者、修改 MaxIterations），但不能更改 AgentType。可以删除处于 Registered 状态的 Team Agent。

**Why this priority**: Agent 管理的基本生命周期操作。

**Independent Test**: 创建 Team Agent → 更新其 MaxIterations 为 10 → 查询验证变更生效 → 删除 → 查询确认 404。

**Acceptance Scenarios**:

1. **Given** 已创建的 RoundRobin Team Agent，**When** 更新 MaxIterations 从 40 改为 10，**Then** 更新成功，查询返回新的 MaxIterations=10。
2. **Given** 已创建的 Team Agent，**When** 尝试更新 AgentType 为 ChatClient，**Then** 返回 400 错误（AgentType 不可变）。
3. **Given** 用户尝试设置 TeamMode=MagneticOne 但不提供 OrchestratorProviderId，**When** 提交更新，**Then** 返回 400 验证错误。

### User Story 3 — 前端 Team 配置 UI (Priority: P2)

Agent 注册页面支持选择 Team 类型后展示对应的配置表单，包括 TeamMode 选择器、参与者多选列表、模式特定的配置字段。

**Why this priority**: 前端体验增强，首期可用 API 直接操作。

**Independent Test**: 在 Agent 注册页面选择 Team 类型 → 选择 Handoffs 模式 → 验证显示 InitialAgent 选择和交接关系编辑器。

**Acceptance Scenarios**:

1. **Given** Agent 注册表单，**When** 用户选择 AgentType=Team，**Then** 显示 TeamMode 选择器和通用配置区。
2. **Given** 用户选择 TeamMode=Handoffs，**When** 模式切换，**Then** 显示 InitialAgent 选择器和 Handoff 路由关系编辑器。
3. **Given** 用户选择 TeamMode=Selector，**When** 模式切换，**Then** 显示 LLM Provider/Model 选择器和自定义 Prompt 输入框。
4. **Given** 参与者选择列表，**When** 加载 Agent 列表，**Then** 排除当前 Agent 自身和已有 Team 类型 Agent（防止嵌套）。

### Edge Cases

- 参与者 Agent 列表包含已被删除的 Agent ID — 创建时验证所有 ParticipantIds 存在
- HandoffRoutes 中 SourceAgentId 不在 ParticipantIds 中 — 验证拒绝
- TeamConfig 的 JSONB 字段包含未知属性 — STJ 忽略多余字段，反序列化不报错
- 同一 Agent 重复出现在 ParticipantIds 中 — 验证去重或拒绝

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: 系统 MUST 在 `AgentType` 枚举中新增 `Team` 值。
- **FR-002**: 系统 MUST 定义 `TeamMode` 枚举，包含 6 个值：`Sequential`, `Concurrent`, `RoundRobin`, `Handoffs`, `Selector`, `MagneticOne`。
- **FR-003**: 系统 MUST 定义 `TeamConfigVO` 值对象（JSONB 持久化），包含通用配置和 4 种模式特定配置。
- **FR-004**: 系统 MUST 定义 `HandoffTargetVO` 值对象，包含 `TargetAgentId` 和可选 `Reason`。
- **FR-005**: `AgentRegistration` 聚合根 MUST 新增 `TeamConfig` 属性（nullable），仅 `AgentType.Team` 时有值。
- **FR-006**: `AgentRegistration` MUST 提供 `CreateTeam(name, description, teamConfig)` 工厂方法。
- **FR-007**: `CreateTeam` 工厂方法 MUST 对每种 `TeamMode` 执行特定验证规则。
- **FR-008**: `AgentRegistration.Update` 方法 MUST 在 `AgentType=Team` 时验证 `TeamConfig` 的完整性。
- **FR-009**: 系统 MUST 不允许 Team Agent 的 ParticipantIds 引用 `AgentType.Team` 类型的 Agent（首期禁止嵌套）。
- **FR-010**: API 端点 MUST 支持 `agentType: "Team"` + `teamConfig` 的注册/更新请求。
- **FR-011**: `AgentRegistrationDto` MUST 包含 `TeamConfigDto` 属性。
- **FR-012**: Agent 列表 API MUST 支持 `?type=Team` 过滤。
- **FR-013**: 数据库 MUST 通过 EF Core Migration 新增 `TeamConfig` JSONB 列。

### Non-Functional Requirements

- **NFR-001**: TeamConfig JSONB 反序列化 MUST 使用 STJ（与项目现有约定一致），忽略未知属性。
- **NFR-002**: ParticipantIds 验证 MUST 使用批量查询（`IN` 子句），不逐个查询。
- **NFR-003**: Migration MUST 为 nullable 列，不影响现有 Agent 数据。

### System Constraints

- **SC-001**: AgentType 不可变 — 一旦创建，不能从 Team 改为其他类型或反之。
- **SC-002**: Team 嵌套禁止 — ParticipantIds 中不允许包含 AgentType=Team 的 Agent。
- **SC-003**: EF Core 10 + PostgreSQL 17 + Npgsql JSONB 映射。

---

## Out of Scope

- Team Agent 的实际执行逻辑（见 SPEC-101）
- Selector / MagneticOne 模式的 LLM 调用实现（见 SPEC-102 / SPEC-103）
- 前端 Chat UI 与 Team Agent 的交互（见 SPEC-101）
