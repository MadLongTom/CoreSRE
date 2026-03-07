# Feature Specification: Incident Feedback Loop & Continuous Learning

**Feature Branch**: `025-incident-feedback-loop`  
**Created**: 2026-03-07  
**Status**: Draft  
**Input**: 建立「告警 → RCA → SOP → 执行 → 评估 → 改进」的闭环反馈机制，实现 AIOps 能力的持续增长

## Problem Statement

当前系统的三条链路（Chain A/B/C）是**单向流转**，缺乏以下闭环能力：

1. **SOP 失败无自动降级**：Chain A 执行失败后不会自动切换到 Chain B（RCA），需人工干预
2. **SOP 无自动迭代**：生成的 SOP v1 效果差时，无法自动触发改进生成 SOP v2
3. **Agent Prompt 无数据驱动优化**：工具调用错误模式已知（023-spec），但无自动反馈到 Prompt 改进
4. **告警规则无自动调优**：频繁误报或漏报的 AlertRule 无告警反馈闭环
5. **无金丝雀发布**：新 SOP/Agent 配置无法在生产流量下安全验证

## Clarifications

- Q: Chain A 失败后自动降级到 Chain B 是否有风险？ → A: 有。需设置降级条件（连续 N 次失败才降级），避免单次网络超时导致误降级。
- Q: SOP 自动迭代是否需要人工确认？ → A: 是。自动触发 Chain C 重新生成，但仍需经过 022-spec 的审核流程。
- Q: Prompt 优化是自动还是半自动？ → A: 半自动。系统生成优化建议，人工确认后应用。
- Q: 金丝雀验证的流量比例？ → A: 初始 0%（纯 shadow 模式，不执行工具），验证通过后可逐步切换。

## User Scenarios & Testing

### User Story 1 — SOP 执行失败自动降级 (Priority: P0)

作为系统，当 Chain A（SOP 执行）失败时，我需要自动降级到 Chain B（RCA），以确保每个 Incident 都能得到处理。

**Acceptance Scenarios**:

1. **Given** Chain A 执行中 SOP Agent 超时（15 分钟），**When** 超时事件触发，**Then** 系统检查 AlertRule 的 `TeamAgentId` 是否非空。如果有 Team Agent，自动发起 `DispatchRootCauseAnalysisCommand`，Incident 的 Route 从 `SopExecution` 变更为 `FallbackRca`，Timeline 记录 `SopFallbackToRca` 事件。
2. **Given** Chain A 执行中 Agent 明确判断"SOP 不适用于当前场景"（输出包含预定义关键词），**When** 系统检测到不适用判断，**Then** 同样降级到 Chain B。
3. **Given** 同一 SOP 在最近 N 次执行中连续失败（N 由 AlertRule 配置，默认 3），**When** 第 N 次失败，**Then** 系统自动将 AlertRule 的 `SopId` 设为 `null`（解绑 SOP），后续该类告警直接走 Chain B。记录 `SopAutoDisabled` 事件。Operator 收到通知。
4. **Given** 降级到 Chain B 后 RCA 成功，**When** Chain C 生成新 SOP，**Then** 新 SOP 作为 v(N+1) 进入 022-spec 的审核流程，不自动绑定。

---

### User Story 2 — SOP 效能自动追踪与降级阈值 (Priority: P0)

作为系统，我需要持续追踪每个 SOP 的执行成功率，自动识别效能下降的 SOP。

**Acceptance Scenarios**:

1. **Given** 系统完成一次 Chain A 执行，**When** Incident 状态转为 Resolved 或 Timeout，**Then** 更新该 SOP 的累积统计：`TotalExecutions++`，若成功 `SuccessCount++`，若超时/失败 `FailureCount++`。
2. **Given** SOP 的滚动成功率（最近 20 次执行）低于 60%，**When** 检测到效能下降，**Then** 系统生成 `SopEfficiencyAlert`，通知 Operator："SOP '{name}' 最近成功率为 {rate}%，建议审查或重新生成"。
3. **Given** SOP 的成功率持续低于 40%（最近 10 次），**When** 阈值触发，**Then** 自动将 SOP 状态标记为 `Degraded`，并建议 AlertRule 解绑。
4. **Given** SOP 的 MTTR 均值显著高于历史基线（> 2 倍标准差），**When** 统计检测到异常，**Then** 标记为"MTTR 回归"并记入 SOP 评估报告。

---

### User Story 3 — 金丝雀验证（Shadow Execution） (Priority: P1)

作为管理员，我希望在将新 SOP 投入生产前，用真实告警进行 shadow 验证，确认其行为正确。

**Acceptance Scenarios**:

1. **Given** 一个新的 SOP（v2）已通过审核进入 `Reviewed` 状态，**When** 管理员点击"启动金丝雀验证"，**Then** AlertRule 进入 `Canary` 模式：真实告警仍走老路径（Chain B 或 SOP v1），同时启动一个 Shadow Chain A 使用 SOP v2 执行（工具调用为只读 Mock）。
2. **Given** Canary 模式下收到真实告警，**When** Shadow Chain A 执行完成，**Then** 系统记录 `CanaryResult`：Shadow 根因/结论 vs 实际处理结论，工具调用序列对比，Token 消耗。
3. **Given** 积累了 5+ 个 CanaryResult，**When** 管理员查看金丝雀报告，**Then** 展示：一致率（Shadow 结论与实际处理一致的比例）、平均 Token 差异、步骤覆盖率。
4. **Given** 金丝雀一致率 > 90%，**When** 管理员确认验证通过，**Then** 系统将 SOP v2 升级为 `Active`，AlertRule 切换绑定，SOP v1 转为 `Superseded`。
5. **Given** 金丝雀一致率 < 70%，**When** 管理员查看报告，**Then** 系统建议"不推荐发布"，并列出不一致的 Incident Case 供分析。

---

### User Story 4 — Agent Prompt 优化建议 (Priority: P1)

作为管理员，我希望系统基于历史执行数据自动生成 Agent Prompt 优化建议，减少重复错误。

**Acceptance Scenarios**:

1. **Given** 023-spec 的评估数据显示某 Agent 频繁出现特定错误模式（如"重复调用同一 PromQL 查询但参数未变"），**When** 系统运行周期性分析（每日），**Then** 生成 `PromptOptimizationSuggestion`：问题描述 + 建议的 Prompt 片段。
2. **Given** 分析发现 Agent 经常调用未声明的工具，**When** 生成建议，**Then** 建议在 Agent 的 system instruction 中增加"你只能使用以下工具：[工具列表]"约束语。
3. **Given** 分析发现 Agent 的 Token 消耗远高于同类 Agent（> 2 倍中位数），**When** 生成建议，**Then** 建议精简 system instruction 或调整输出格式要求（如"输出 JSON 而非长文"）。
4. **Given** 管理员查看优化建议列表，**When** 确认某条建议，**Then** 建议的 Prompt 变更自动应用到 Agent 的 `LlmConfig.Instructions`。修改前的 Instructions 保存为快照（可回退）。
5. **Given** Prompt 变更后，**When** 023-spec 的回放评估显示准确率下降，**Then** 自动回退到变更前的 Prompt 并通知管理员。

---

### User Story 5 — AlertRule 健康评估 (Priority: P2)

作为管理员，我希望看到每条 AlertRule 的健康评分，识别需要调优的告警规则。

**Acceptance Scenarios**:

1. **Given** 管理员打开 AlertRule 管理页面，**When** 页面加载，**Then** 每条 AlertRule 展示健康评分（0-100），基于以下因子加权计算：
   - 触发频率合理性（过高可能是误报，过低可能是漏报）
   - SOP 覆盖（有 SOP 绑定 = +20 分）
   - SOP 成功率（> 80% = +30 分）
   - 平均 MTTR（低于基线 = +20 分）
   - 人工介入率（< 10% = +30 分）
2. **Given** 一条 AlertRule 健康评分 < 40，**When** 管理员点击查看原因，**Then** 展示扣分明细和改进建议（如"误报率高，建议调整 Matcher 条件"或"无 SOP 覆盖，建议生成 SOP"）。
3. **Given** 一条 AlertRule 在 30 天内触发了 200+ 次但 SOP 成功率仅 30%，**When** 系统检测到，**Then** 自动标记为"高频低效"，建议：(a) 调整告警阈值减少误报 (b) 重新生成 SOP (c) 分配更强的 Team Agent 做 RCA。

---

### Edge Cases

- Chain A 降级到 Chain B 后 Chain B 也失败 → 状态标记为 `Escalated`，通知所有 Operator
- 金丝雀验证期间老 SOP 也失败了 → 不影响金丝雀评估，但 Incident 本身走正常降级流程
- Prompt 优化建议与人工编辑冲突 → 人工编辑优先，系统标记"该 Agent 已手动定制，自动建议暂停"
- 降级、金丝雀、Prompt 变更同时发生 → 每种操作独立记录在 Timeline 中，互不干扰
- AlertRule 被删除但还有在飞 Incident → 在飞 Incident 继续执行，不受删除影响

## Requirements

### Functional Requirements

- **FR-001**: Chain A 执行超时或失败后，系统 MUST 检查 AlertRule 是否配置了 `TeamAgentId`，如有则自动降级到 Chain B。
- **FR-002**: Incident 的 `IncidentRoute` 枚举 MUST 新增 `FallbackRca` 值，标记降级的 Incident。
- **FR-003**: 系统 MUST 追踪每个 SOP 的滚动执行统计（最近 N 次的成功率），存储在 `SkillRegistration` 上。
- **FR-004**: 当 SOP 滚动成功率低于配置阈值时，系统 MUST 自动标记 SOP 状态为 `Degraded`。
- **FR-005**: 连续 N 次失败（N 可配置，默认 3）后，系统 MUST 自动解绑 AlertRule 的 `SopId`。
- **FR-006**: 系统 MUST 支持 AlertRule 进入 `Canary` 模式，同时执行 Shadow Chain A 和正常处理链路。
- **FR-007**: Shadow Chain A MUST 使用只读 Mock Tool Invoker，不产生任何副作用。
- **FR-008**: 系统 MUST 记录 `CanaryResult` 并提供一致率计算。
- **FR-009**: 系统 MUST 提供 GET `/api/alert-rules/{id}/health` API，返回健康评分和扣分明细。
- **FR-010**: 系统 MUST 每日运行 Agent Prompt 分析任务，基于近 7 天的执行数据生成优化建议。
- **FR-011**: Prompt 优化变更 MUST 保存变更前快照，支持一键回退。
- **FR-012**: 降级、金丝雀启停、Prompt 变更、SOP 解绑 MUST 全部通过 SignalR 通知相关 Operator。
- **FR-013**: 系统 MUST 提供 GET `/api/evaluation/feedback-summary` API，返回闭环运行状态（降级次数、金丝雀通过率、Prompt 优化采纳率）。

### Non-Functional Requirements

- **NFR-001**: 降级决策 MUST 在 Chain A 超时后 5 秒内触发。
- **NFR-002**: Shadow Chain A 执行 MUST 与正常处理链路完全隔离（独立上下文、独立会话）。
- **NFR-003**: 每日 Prompt 分析任务 MUST 在 10 分钟内完成（覆盖所有活跃 Agent）。
- **NFR-004**: AlertRule 健康评分 MUST 基于最近 30 天数据，每小时刷新一次。
- **NFR-005**: Prompt 回退 MUST 在 10 秒内生效。

### Key Entities (New/Modified)

**IncidentRoute (Modified Enum)**:
- 新增 `FallbackRca`（降级 RCA）

**Incident (Modified)**:
- 新增 `FallbackFrom: IncidentRoute?`（如果是降级，记录原始链路）
- 新增 `FallbackReason: string?`（降级原因描述）

**SkillRegistration (Modified)**:
- 新增 `ExecutionStats: SopExecutionStatsVO?`（JSONB 存储）

**SopExecutionStatsVO (New Value Object)**:
- `TotalExecutions: int`
- `SuccessCount: int`
- `FailureCount: int`
- `TimeoutCount: int`
- `RecentResults: List<bool>`（最近 20 次执行结果，滚动窗口）
- `RollingSuccessRate: double`（滚动成功率）
- `AverageMttrMs: long`
- `LastExecutedAt: DateTime?`

**AlertRule (Modified)**:
- 新增 `CanaryMode: bool`（是否处于金丝雀模式）
- 新增 `CanarySopId: Guid?`（金丝雀验证中的新 SOP）
- 新增 `MaxConsecutiveFailures: int = 3`（降级阈值）
- 新增 `HealthScore: int?`（健康评分，0-100）
- 新增 `HealthDetails: AlertRuleHealthVO?`（评分明细）

**CanaryResult (New Entity)**:
- `Id: Guid`
- `AlertRuleId: Guid`
- `IncidentId: Guid`（真实 Incident）
- `CanarySopId: Guid`（验证的 SOP）
- `ShadowRootCause: string?`（Shadow 执行结论）
- `ActualRootCause: string?`（真实处理结论）
- `IsConsistent: bool`（结论是否一致）
- `ShadowToolCalls: List<string>`（Shadow 工具调用序列）
- `ShadowTokenConsumed: int`
- `ShadowDurationMs: long`
- `CreatedAt: DateTime`

**PromptOptimizationSuggestion (New Entity)**:
- `Id: Guid`
- `AgentId: Guid`
- `IssueType: PromptIssueType`（RepeatedToolCalls | UndeclaredToolUsage | HighTokenUsage | LowAccuracy | Other）
- `Description: string`
- `SuggestedPromptPatch: string`（建议修改的 instruction 片段）
- `BasedOnIncidentIds: List<Guid>`（依据的 Incident 列表）
- `Status: SuggestionStatus`（Pending | Applied | Rejected | AutoReverted）
- `PreviousInstructionSnapshot: string?`（变更前快照）
- `CreatedAt: DateTime`
- `AppliedAt: DateTime?`

**AlertRuleHealthVO (New Value Object)**:
- `Score: int`（0-100）
- `Factors: List<HealthFactor>`
- `Recommendations: List<string>`

**HealthFactor (New Value Object)**:
- `Name: string`（如"SOP成功率"、"触发频率"）
- `Weight: int`（权重分值）
- `Earned: int`（实际得分）
- `Detail: string`（说明）

## Success Criteria

- **SC-001**: Chain A 失败后自动降级延迟 < 10 秒，降级成功率 100%。
- **SC-002**: SOP 自动解绑准确率 > 95%（不该解绑的不误解绑）。
- **SC-003**: 金丝雀验证覆盖率 > 50%（新 SOP 中经过金丝雀验证的比例）。
- **SC-004**: Prompt 优化建议采纳后，目标 Agent 的工具调用准确率提升 > 10%。
- **SC-005**: AlertRule 健康评分与人工判断的相关性 > 0.7（Pearson 相关系数）。

## Assumptions

- 022-spec 的 SOP 生命周期管理已实现（Draft → Reviewed → Active 流程）。
- 023-spec 的评估指标采集已实现（Post-mortem 标注、Agent 个体统计）。
- 024-spec 的步骤级追踪已实现（SopStepExecutionVO）。
- 降级到 Chain B 使用原有 AlertRule 配置的 TeamAgentId，无需额外配置。
- Shadow Chain A 的 Mock Tool Invoker 可复用 022-spec 的干运行基础设施。
- Prompt 分析任务使用系统内置的 LLM（不额外消耗用户的 LLM 配额）。

## Dependencies

- **022-sop-quality-assurance**: SOP 生命周期状态机、版本管理
- **023-agent-evaluation-framework**: Post-mortem 标注、Agent 个体统计、回放基础设施
- **024-sop-execution-stability**: 步骤级追踪、工具调用约束

## Implementation Priority

建议实施顺序：US1（降级）→ US2（效能追踪）→ US3（金丝雀）→ US4（Prompt 优化）→ US5（AlertRule 健康）。其中 US1 和 US2 可并行开发。



