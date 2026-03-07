# Feature Specification: Agent Evaluation Framework

**Feature Branch**: `023-agent-evaluation-framework`  
**Created**: 2026-03-07  
**Status**: Draft  
**Input**: 建立系统化的 Agent 评估体系，量化 RCA 准确率、SOP 执行稳定性、各 Agent 个体表现

## Problem Statement

当前系统虽然记录了 `Incident.TimeToResolveMs`（MTTR）和 `Incident.TimeToDetectMs`（MTTD），但缺乏：

1. **Agent 个体评估**：无法衡量 Team 中各 Agent 的贡献和质量
2. **RCA 准确率度量**：根因结论没有与人工标注的 Ground Truth 对比
3. **SOP 效能追踪**：同一 SOP 在不同场景下的成功/失败率无统计
4. **全局运营仪表盘**：管理层无法看到 AIOps 系统整体运行健康度
5. **回归基线**：缺少历史场景回放能力，无法验证 Agent/Prompt 更新后效果是否退化

## Clarifications

- Q: RCA 准确率由谁标注 Ground Truth？ → A: SRE 工程师在 Incident 关闭后做 Post-mortem 标注，填写「实际根因」。
- Q: 评估数据存储在哪里？ → A: 评估结果作为 Incident 扩展字段存入 PostgreSQL，聚合统计走 PromQL/Grafana。
- Q: 是否需要支持离线批量回放评估？ → A: 是，需要支持对历史 Incident 的批量回放（Replay），使用当前配置的 Agent 重新执行。
- Q: Agent 个体评估指标有哪些？ → A: 工具调用准确率、有效信息贡献率（发言是否推动了根因发现）、Token 消耗。

## User Scenarios & Testing

### User Story 1 — Post-mortem 根因标注 (Priority: P0)

作为 SRE 工程师，我希望在 Incident 关闭后标注「实际根因」，以便系统计算 RCA 准确率。

**Acceptance Scenarios**:

1. **Given** 一个状态为 Resolved 或 Closed 的 Incident，**When** SRE 打开 Incident 详情页，**Then** 可以看到「Post-mortem」标注区域，包含：实际根因（自由文本）、RCA 是否准确（枚举：Accurate / PartiallyAccurate / Inaccurate / NotApplicable）、改进建议。
2. **Given** SRE 填写了实际根因并标记为 `Inaccurate`，**When** 提交标注，**Then** 系统记录 `PostMortemAnnotation`，并自动计算该 Incident 的 `RcaAccuracy` 指标。
3. **Given** 一个 Chain A（SOP 执行）的 Incident，**When** SRE 进行 Post-mortem，**Then** 还需标注：SOP 是否有效（Effective / PartiallyEffective / Ineffective）、无效原因。
4. **Given** 系统已有 100+ 条 Post-mortem 标注，**When** 管理员查看评估仪表盘，**Then** 可看到 RCA 准确率趋势（按周/月）、SOP 有效率排名。

---

### User Story 2 — Incident 运营仪表盘 (Priority: P0)

作为管理员，我希望看到 AIOps 系统整体运行数据的仪表盘，以便评估自动化效果和发现薄弱环节。

**Acceptance Scenarios**:

1. **Given** 管理员打开评估仪表盘页面，**When** 页面加载完成，**Then** 展示以下核心指标卡片：
   - 总 Incident 数（按时间范围过滤）
   - 自动修复率 = Chain A 成功数 / 总 Incident 数
   - 平均 MTTR（按 Severity 分组）
   - SOP 覆盖率 = 有 SOP 的 AlertRule 数 / 总 AlertRule 数
   - 人工介入率 = 有 HumanIntervention 事件的 Incident 数 / 总 Incident 数
   - 超时率 = 有 Timeout 事件的 Incident 数 / 总 Incident 数
2. **Given** 仪表盘展示 MTTR 趋势图，**When** 管理员选择最近 30 天，**Then** 图表展示逐日 MTTR 均值，按 Severity（P1-P4）分系列展示。
3. **Given** 仪表盘展示 SOP 排名表，**When** 管理员切换到"SOP 效能"Tab，**Then** 展示每个 SOP 的：使用次数、成功率、平均执行时间、人工介入次数。

---

### User Story 3 — Agent 个体表现评估 (Priority: P1)

作为管理员，我希望看到 Team 中每个参与 Agent 的表现指标，以便优化 Prompt 和工具配置。

**Acceptance Scenarios**:

1. **Given** 一个已完成的 RCA Incident（Chain B），**When** 管理员查看该 Incident 的 Agent 分析页面，**Then** 展示每个参与 Agent 的：
   - 发言次数和 Token 消耗
   - 工具调用次数和成功率
   - 有效贡献率（发言后是否推动了新信息发现，基于后续 Agent 引用情况估算）
2. **Given** 管理员查看全局 Agent 排名页面，**When** 选择时间范围，**Then** 展示所有 Agent 的：
   - 参与 Incident 数
   - 平均工具调用准确率（调用了正确工具 / 总工具调用）
   - 平均 Token 消耗/Incident
   - 被人工介入纠正的次数
3. **Given** 一个 Agent 的工具调用准确率低于 60%，**When** 管理员查看详情，**Then** 列出该 Agent 的典型错误模式（如：重复调用同一查询、使用错误的 PromQL 语法、遗漏关键标签过滤）。

---

### User Story 4 — 历史 Incident 回放评估 (Priority: P2)

作为 SRE 工程师，我希望用当前配置的 Agent 重放历史 Incident，以验证 Prompt/工具变更是否带来改进或退化。

**Acceptance Scenarios**:

1. **Given** 一个有完整 AlertPayload 和 Post-mortem 标注的历史 Incident，**When** SRE 点击"回放"按钮，**Then** 系统使用该 Incident 的原始 AlertPayload 作为输入，用**当前配置**的 Agent 重新执行 Chain B（RCA）或 Chain A（SOP）。
2. **Given** 回放使用 Mock 数据源（与原始 Incident 时间段对齐的固定快照），**When** Agent 执行完成，**Then** 系统对比：
   - 回放根因 vs 标注的实际根因 → 语义相似度评分
   - 回放工具调用序列 vs 原始工具调用序列 → 序列匹配率
   - 回放 Token 消耗 vs 原始 Token 消耗 → 效率对比
3. **Given** SRE 选择批量回放最近 20 条 Incident，**When** 批量任务完成，**Then** 生成对比报告：整体准确率变化、MTTR 变化、Token 消耗变化。
4. **Given** 回放结果显示准确率下降超过 10%，**When** 报告生成，**Then** 自动标记为"回归风险"，通知管理员。

---

### Edge Cases

- Incident 无对话历史（如 Agent 启动即超时）→ 标注 RCA 为 NotApplicable，不计入准确率统计
- Post-mortem 标注存在主观性差异 → 提供枚举选项限制主观偏差，辅以自由文本说明
- 回放时原始数据源不可用 → 必须使用 Mock 数据，不依赖实时数据源
- Agent 配置在回放前后发生变化 → 回放记录当时的 Agent 配置快照，可追溯
- 并发回放大量 Incident → 排队执行，限制并发数（最多 5 个同时回放）

## Requirements

### Functional Requirements

- **FR-001**: Incident 实体 MUST 新增 `PostMortemAnnotation` 值对象，包含实际根因、准确性评级、SOP 有效性评级、改进建议。
- **FR-002**: 系统 MUST 提供 POST `/api/incidents/{id}/post-mortem` API，仅允许 Operator/Admin 角色调用。
- **FR-003**: 系统 MUST 提供 GET `/api/evaluation/dashboard` API，返回指定时间范围内的聚合指标（自动修复率、MTTR、SOP 覆盖率、人工介入率、超时率）。
- **FR-004**: 系统 MUST 提供 GET `/api/evaluation/agents` API，返回各 Agent 的参与次数、工具准确率、Token 消耗统计。
- **FR-005**: 系统 MUST 提供 GET `/api/evaluation/sops` API，返回各 SOP 的使用次数、成功率、平均执行时间排名。
- **FR-006**: Incident Timeline 中的每个 Agent 发言 MUST 记录 `AgentId`、`TokenCount`、`ToolCalls` 信息，支撑个体评估。
- **FR-007**: 系统 MUST 提供 POST `/api/incidents/{id}/replay` API，使用原始 AlertPayload + Mock 数据源重新执行 Agent 链路。
- **FR-008**: 回放结果 MUST 与原始结果和 Post-mortem 标注对比，计算语义相似度和序列匹配率。
- **FR-009**: 系统 MUST 提供 POST `/api/evaluation/batch-replay` API，支持批量回放最多 50 条 Incident。
- **FR-010**: 前端 MUST 提供评估仪表盘页面，展示核心指标卡片、趋势图、SOP/Agent 排名表。
- **FR-011**: 前端 MUST 在 Incident 详情页提供 Post-mortem 标注表单。
- **FR-012**: RCA 准确率 MUST 按以下规则计算：Accurate=1.0, PartiallyAccurate=0.5, Inaccurate=0.0, NotApplicable=排除。

### Non-Functional Requirements

- **NFR-001**: 仪表盘查询 MUST 在 3 秒内返回（100,000 条 Incident 量级）。
- **NFR-002**: 单次回放 MUST 在 10 分钟内完成或超时终止。
- **NFR-003**: 批量回放 MUST 限制并发数最多 5，排队执行。
- **NFR-004**: Post-mortem 数据 MUST 持久化且不可删除（审计要求）。

### Key Entities (New/Modified)

**Incident (Modified)**:
- 新增 `PostMortem: PostMortemAnnotationVO?`（值对象）

**PostMortemAnnotationVO (New Value Object)**:
- `ActualRootCause: string`（实际根因描述）
- `RcaAccuracy: RcaAccuracyRating`（Accurate | PartiallyAccurate | Inaccurate | NotApplicable）
- `SopEffectiveness: SopEffectivenessRating?`（Effective | PartiallyEffective | Ineffective，仅 Chain A）
- `ImprovementNotes: string?`（改进建议）
- `AnnotatedBy: string`（标注人）
- `AnnotatedAt: DateTime`

**IncidentTimelineVO (Modified)**:
- 新增 `AgentId: Guid?`（发言 Agent ID）
- 新增 `TokenCount: int?`（该发言的 Token 数）
- 新增 `ToolCallDetails: List<ToolCallDetailVO>?`（工具调用详情）

**ToolCallDetailVO (New Value Object)**:
- `ToolName: string`
- `ToolId: Guid`
- `Parameters: JsonElement?`（调用参数，脱敏后存储）
- `Result: ToolCallResult`（Success | Failed | Timeout）
- `DurationMs: long`

**ReplayResult (New DTO)**:
- `ReplayIncidentId: Guid`（原始 Incident ID）
- `ReplayRootCause: string?`（回放得到的根因）
- `SemanticSimilarity: double?`（与实际根因的语义相似度, 0.0-1.0）
- `ToolCallSequenceMatch: double?`（工具调用序列匹配率, 0.0-1.0）
- `TokenConsumed: int`
- `DurationMs: long`
- `ComparedToOriginal: ComparisonSummary`（与原始执行的对比摘要）

## Success Criteria

- **SC-001**: 80%+ 已关闭 Incident 有 Post-mortem 标注（运营纪律指标）。
- **SC-002**: 评估仪表盘能在 3 秒内呈现最近 30 天的核心指标。
- **SC-003**: Agent 个体表现排名可识别出表现最差的 20% Agent。
- **SC-004**: 回放准确率与在线准确率偏差 < 5%（验证 Mock 数据源的保真度）。

## Assumptions

- 语义相似度计算使用 pgvector 的 Embedding 对比（复用已有的嵌入生成能力）。
- Timeline 事件已记录了足够的 Agent 发言信息，只需扩展字段。
- Mock 数据源的 Fixture 数据可从原始 Incident 时间段的数据源快照中获取。
- 前端仪表盘复用已有的 shadcn/ui 组件（Card、Chart、Table），无需引入新 UI 库。
