# Feature Specification: SOP Quality Assurance Pipeline

**Feature Branch**: `022-sop-quality-assurance`  
**Created**: 2026-03-07  
**Status**: Draft  
**Input**: 保证 Chain C 自动生成的 SOP 质量可控、可审查、可验证

## Problem Statement

当前 Chain C（`GenerateSopFromIncidentCommandHandler`）自动生成 SOP 后，直接存入 `SkillRegistration` 并绑定到 `AlertRule`，缺少以下关键环节：

1. **无结构化校验**：生成的 SOP Markdown 可能缺少必要段落（前置检查、回退计划、工具参数）
2. **无人工审核**：SOP 立即生效，如果包含错误操作参数可能造成二次故障
3. **无干运行验证**：SOP 在真实告警触发前未经过任何模拟执行验证
4. **无版本管理**：SOP 被覆盖后无法回退

## Clarifications

- Q: SOP 生成后是否应该立即投入生产使用？ → A: 不应该。应经历 Draft → Reviewed → Validated → Active 的生命周期。
- Q: 结构化校验要求多严格？ → A: 必须包含适用条件、处置步骤、回退计划三个核心段落，每个步骤必须声明工具调用和预期结果。
- Q: 谁负责审核？ → A: 具有 Admin 或 Operator 角色的用户。
- Q: 版本管理是否需要 Git 级别的 diff？ → A: 初版只需记录版本号和历史快照，不需要 diff 视图。

## User Scenarios & Testing

### User Story 1 — SOP 结构化校验 (Priority: P0)

作为系统，我需要确保自动生成的 SOP 符合标准结构，以便下游 Agent 能够可靠解析和执行。

**Acceptance Scenarios**:

1. **Given** Chain C 生成了一段 SOP Markdown，**When** 系统执行结构化校验，**Then** 检查以下必需段落是否存在：`## 适用条件`、`## 处置步骤`（至少 1 步）、`## 回退计划`。若缺失任一段落，SOP 状态标记为 `Invalid` 并附带 `ValidationErrors` 列表。
2. **Given** SOP 中每个步骤声明了工具调用（如 `query_metrics_prometheus`），**When** 校验器检查工具引用，**Then** 确认每个引用的工具名在系统中存在已注册的 `ToolRegistration`。未匹配的工具名记入 `ValidationWarnings`。
3. **Given** SOP 中某步骤缺少 `预期结果` 或 `超时` 声明，**When** 校验器运行，**Then** 输出 Warning 级别提示（不阻塞，但记录改进建议）。
4. **Given** SOP 引用了需要人工审批的危险工具（如 `scale_deployment_k8s`、`delete_pod_k8s`），**When** 校验器检测到，**Then** 自动标记步骤为 `RequiresApproval = true`。

---

### User Story 2 — SOP 生命周期与人工审核 (Priority: P0)

作为 SRE 管理员，我希望自动生成的 SOP 需经人工审核后才正式绑定到告警规则，以防止错误的 SOP 自动执行。

**Acceptance Scenarios**:

1. **Given** Chain C 生成了一个 SOP，**When** SOP 创建完成，**Then** SOP 状态为 `Draft`（而非当前的 `Active`），AlertRule 的 `SopId` 暂不更新。
2. **Given** SOP 处于 `Draft` 状态且通过了结构化校验（无 Error 级别问题），**When** Operator/Admin 在 UI 中点击"审核通过"，**Then** SOP 状态转为 `Reviewed`。
3. **Given** SOP 处于 `Reviewed` 状态，**When** 管理员点击"发布"，**Then** SOP 状态转为 `Active`，AlertRule 的 `SopId` 和 `ResponderAgentId` 更新绑定。
4. **Given** SOP 处于 `Draft` 状态，**When** 审核者点击"驳回"并填写原因，**Then** SOP 状态转为 `Rejected`，驳回原因记录在 `ReviewComment` 字段。
5. **Given** 一个 `Active` 状态的 SOP 需要下线，**When** 管理员点击"归档"，**Then** SOP 状态转为 `Archived`，AlertRule 的绑定解除。

---

### User Story 3 — SOP 版本历史 (Priority: P1)

作为 SRE 管理员，我希望查看 SOP 的历史版本,以便在新版 SOP 出问题时回退到旧版。

**Acceptance Scenarios**:

1. **Given** 一个已存在的 SOP（v1），**When** Chain C 为同一类告警生成了新的 SOP 内容，**Then** 系统创建新的 `SkillRegistration` 记录（v2），而不是覆盖 v1。v1 自动转为 `Superseded` 状态。
2. **Given** SOP 有 v1（Superseded）和 v2（Active），**When** 管理员查看 SOP 详情，**Then** 页面展示版本列表和每个版本的创建时间、来源 Incident ID。
3. **Given** SOP v2 运行效果不佳，**When** 管理员选择"回退到 v1"，**Then** v2 转为 `Archived`，v1 恢复为 `Active`，AlertRule 绑定更新。

---

### User Story 4 — SOP 干运行验证 (Priority: P1)

作为 SRE 管理员，我希望在审核 SOP 时可以对其进行干运行（不实际执行工具），以验证步骤逻辑是否通顺。

**Acceptance Scenarios**:

1. **Given** 一个 Draft/Reviewed 状态的 SOP，**When** 管理员点击"干运行"，**Then** 系统使用 Mock 数据源（返回预定义正常值/异常值）模拟 Agent 执行该 SOP。
2. **Given** 干运行执行中，**When** Agent 调用工具，**Then** 工具调用不实际发出，而是返回 Mock 响应（可配置正常/异常两种 fixture）。
3. **Given** 干运行完成，**When** 系统展示结果，**Then** 显示每步执行状态（通过/跳过/失败）、Agent 的推理过程、总耗时。
4. **Given** 干运行中某步骤 Agent 无法继续（如陷入循环或无法解析工具返回），**When** 超时触发，**Then** 标记该步骤为失败并终止干运行，返回失败位置和 Agent 输出。

---

### Edge Cases

- SOP Markdown 格式严重损坏（如纯文本无标题）→ 校验返回 `Critical` 错误，不允许进入 Draft 状态
- 同一类告警同时触发多个 Chain C（并发）→ 使用 `AlertFingerprint` + 乐观锁避免创建重复 SOP
- 审核中 SOP 被重新生成 → 旧 Draft 自动标记为 `Superseded`，审核人收到通知
- 干运行时 LLM 返回与 SOP 不一致的操作 → 记录偏差日志，视为干运行失败

## Requirements

### Functional Requirements

- **FR-001**: 系统 MUST 在 SOP 创建时执行结构化校验（`SopValidator`），检查必需段落和工具引用合法性。
- **FR-002**: SOP MUST 支持 `Draft → Reviewed → Active` 生命周期状态机，以及 `Rejected`、`Archived`、`Superseded` 终态。
- **FR-003**: Chain C 生成 SOP 后 MUST 将其状态设为 `Draft`，NOT `Active`。
- **FR-004**: AlertRule 的 `SopId` 绑定 MUST 仅在 SOP 达到 `Active` 状态时更新。
- **FR-005**: SOP MUST 支持版本追踪。同一类告警的多版本 SOP 通过 `AlertRuleId` 关联，按版本号排序。
- **FR-006**: 系统 MUST 提供干运行 API（`POST /api/skills/{id}/dry-run`），使用 Mock 数据源执行 SOP 并返回步骤级结果。
- **FR-007**: 干运行 MUST 使用 `MockDataSourceQuerier` 替代真实数据源，不产生任何副作用。
- **FR-008**: SOP 审核操作 MUST 记录审核人 ID 和审核意见。
- **FR-009**: 当自动生成 SOP 检测到同类 SOP 已存在时，MUST 创建新版本而不是覆盖。
- **FR-010**: 危险工具步骤（delete、scale、restart 等动作前缀）MUST 在校验阶段自动标记为需审批。

### Non-Functional Requirements

- **NFR-001**: 结构化校验 MUST 在 500ms 内完成。
- **NFR-002**: 干运行 MUST 在 2 分钟内完成或超时终止。
- **NFR-003**: SOP 版本历史 MUST 保留至少最近 10 个版本。

### Key Entities (New/Modified)

**SkillRegistration (Modified)**:
- 新增 `Version: int`（版本号，从 1 递增）
- 新增 `SourceIncidentId: Guid?`（生成该 SOP 的 Incident ID）
- 新增 `SourceAlertRuleId: Guid?`（来源 AlertRule，用于版本关联）
- 新增 `ReviewedBy: string?`（审核人）
- 新增 `ReviewComment: string?`（审核意见）
- 新增 `ReviewedAt: DateTime?`（审核时间）
- 新增 `ValidationErrors: List<string>`（校验错误列表）
- 新增 `ValidationWarnings: List<string>`（校验警告列表）
- `SkillStatus` 枚举扩展：`Draft | Reviewed | Active | Rejected | Archived | Superseded | Invalid`

**SopValidationResult (New Value Object)**:
- `IsValid: bool`
- `Errors: List<SopValidationError>`（Error 级：阻塞发布）
- `Warnings: List<SopValidationWarning>`（Warning 级：不阻塞但记录）
- `DangerousSteps: List<int>`（标记需审批的步骤序号）

**DryRunResult (New DTO)**:
- `OverallStatus: DryRunStatus` (Passed | PartiallyPassed | Failed)
- `Steps: List<DryRunStepResult>`（每步状态、Agent 输出、耗时）
- `TotalDurationMs: long`
- `AgentReasoningLog: string`（完整推理日志）

## Success Criteria

- **SC-001**: 100% 的自动生成 SOP 经过结构化校验，校验结果持久化。
- **SC-002**: 生产环境中 0 例未经人工审核的 SOP 自动执行。
- **SC-003**: SOP 版本可追溯，管理员可在 10 秒内完成版本回退操作。
- **SC-004**: 干运行覆盖率 > 80%（已审核 SOP 中经过干运行的比例）。

## Assumptions

- `SkillRegistration` 实体可以安全扩展新字段（数据库迁移无破坏性变更）。
- Mock 数据源提供固定的正常/异常两套 fixture 即可满足初期干运行需求。
- 审核流程不需要多级审批（单人审核通过即可）。
- 干运行使用与生产相同的 LLM Provider，但 Temperature=0 以提高确定性。
