# Feature Specification: SOP Execution Stability

**Feature Branch**: `024-sop-execution-stability`  
**Created**: 2026-03-07  
**Status**: Draft  
**Input**: 消除 SOP Agent 执行中的不确定性，使同一 SOP 在相同条件下产生一致的工具调用序列和结论

## Problem Statement

当前 Chain A 的 SOP 执行依赖 LLM Agent「理解」SOP Markdown 后自主执行，存在以下不确定性风险：

1. **LLM 概率性输出**：即使 Temperature=0，同一输入多次推理结果可能不同（尤其在 top-p 采样下）
2. **上下文漂移**：多轮对话累积后 Agent 可能偏离 SOP 步骤顺序
3. **工具参数变异**：Agent 可能自行修改 SOP 中指定的工具参数（如改变 PromQL 表达式）
4. **缺失分步状态追踪**：无法知道 SOP 执行到了第几步，失败后无法从断点恢复
5. **无约束执行**：Agent 可以调用 SOP 未声明的工具

## Clarifications

- Q: 应该完全去除 LLM 的灵活性吗？ → A: 不是。保留 LLM 对工具返回结果的**判断能力**，但约束**动作选择**（只能调用 SOP 声明的工具、使用指定参数）。
- Q: 是否将 SOP 编译为 Workflow DAG？ → A: 分两阶段。Phase 1 在 Agent 层面加约束；Phase 2 支持 SOP → Workflow 编译（可选）。
- Q: 步骤追踪粒度到什么程度？ → A: 每个 SOP Step 有独立的 `StepExecution` 记录，包含状态、输入输出、耗时。
- Q: 断点恢复是否需要支持？ → A: Phase 1 支持从失败步骤重试，不支持从任意步骤开始。

## User Scenarios & Testing

### User Story 1 — SOP 结构化解析与步骤注册 (Priority: P0)

作为系统，我需要将 SOP Markdown 解析为结构化的步骤列表，以便精确追踪和约束每步执行。

**Acceptance Scenarios**:

1. **Given** 一个合法的 SOP Markdown（已通过 022-spec 的结构化校验），**When** 系统解析 SOP，**Then** 生成 `List<SopStepDefinition>`，每步包含：步骤编号、描述、工具名、工具参数模板、预期结果判定条件、超时时间、是否需要审批。
2. **Given** SOP Step 2 声明了工具 `query_metrics_prometheus` 和参数 `expression="rate(http_requests_total{status=~'5..'}[5m])"` ，**When** 解析完成，**Then** `SopStepDefinition.ToolName = "query_metrics_prometheus"` 且 `SopStepDefinition.ParameterTemplate` 包含该 PromQL 表达式。
3. **Given** SOP Step 中的参数包含变量占位符（如 `${service_name}`），**When** 解析完成，**Then** `SopStepDefinition.ParameterTemplate` 保留占位符，运行时从告警标签中替换。
4. **Given** SOP 格式不标准（如步骤没有声明工具），**When** 解析时，**Then** 该步骤标记为 `FreeformStep`（由 Agent 自由执行），与结构化步骤区分。

---

### User Story 2 — 受约束的 SOP 执行引擎 (Priority: P0)

作为系统，我需要确保 SOP Agent 严格按照步骤顺序执行，只调用声明的工具，使用指定的参数。

**Acceptance Scenarios**:

1. **Given** 一个 3 步 SOP 开始执行，**When** Agent 生成第一次工具调用，**Then** 系统检查该工具调用是否匹配当前步骤的 `ToolName`。如果匹配，放行执行；如果不匹配，拒绝并将 SOP 步骤信息重新注入 Agent 上下文。
2. **Given** SOP Step 2 声明了 `expression="rate(...)[5m]"` ，**When** Agent 尝试修改为 `expression="rate(...)[10m]"` ，**Then** 系统检测到参数偏差，记录 Warning 日志，但仍允许执行（参数约束为 Soft Constraint，因为 Agent 可能根据返回情况合理调整）。
3. **Given** Agent 尝试调用 SOP 未声明的工具 `delete_pod_k8s`，**When** 系统拦截该调用，**Then** 返回错误消息给 Agent："该工具不在 SOP 声明范围内，请按照 SOP 步骤执行"。记录 `ToolCallRejected` 事件到 Timeline。
4. **Given** SOP Step 1 执行完成（Agent 已收到工具返回并做出判断），**When** 准备进入 Step 2，**Then** 系统自动注入 Step 2 的上下文作为新的 system message（步骤描述 + 工具 + 参数），而不是依赖 Agent 自己"记住"下一步。
5. **Given** SOP 执行中 Agent 陷入循环（连续 3 次生成同一工具调用），**When** 循环检测触发，**Then** 自动跳到下一步并记录 `StepSkipped` 事件。

---

### User Story 3 — 步骤级执行追踪 (Priority: P0)

作为 SRE 工程师，我希望看到 SOP 执行到了第几步、每步的结果，以便快速定位失败点。

**Acceptance Scenarios**:

1. **Given** SOP 包含 5 个步骤，**When** 执行到第 3 步，**Then** Incident 的 `StepExecutions` 列表中有 3 条记录，每条包含：`StepNumber`、`Status`（Pending | Running | Completed | Failed | Skipped）、`StartedAt`、`CompletedAt`、`ToolCallResult`、`AgentJudgment`（Agent 对工具返回的判断结论）。
2. **Given** Step 3 执行失败（工具返回错误），**When** SRE 查看 Incident 详情，**Then** 可看到 Step 3 的 Status=Failed、错误信息、Agent 的推理输出。Step 4-5 的 Status=Pending。
3. **Given** SOP 执行完成，**When** 前端展示执行结果，**Then** 以步骤进度条形式展示（类似 CI/CD Pipeline 可视化）：绿色=Completed、红色=Failed、灰色=Pending、黄色=Skipped。
4. **Given** SignalR 已连接，**When** 每步执行状态变化，**Then** 通过 `IncidentHub` 推送 `StepProgressChanged` 事件，前端实时更新进度条。

---

### User Story 4 — 失败步骤重试 (Priority: P1)

作为 SRE 工程师，我希望在 SOP 某步失败后可以重试该步骤，而不需要从头执行整个 SOP。

**Acceptance Scenarios**:

1. **Given** SOP Step 3 状态为 Failed，**When** SRE 点击"重试 Step 3"，**Then** 系统重新构造 Step 3 的 Agent 上下文（注入 Step 3 描述 + 前序步骤的输出摘要），重新执行该步骤。
2. **Given** 重试成功，**When** Step 3 状态变为 Completed，**Then** 自动继续执行 Step 4。
3. **Given** 重试前 SRE 修改了 Step 3 的参数（如调整 PromQL 的时间窗口），**When** 提交修改后重试，**Then** 使用修改后的参数执行。
4. **Given** 同一步骤已重试 3 次仍失败，**When** SRE 尝试第 4 次重试，**Then** 系统提示"建议手动介入或跳过该步骤"。

---

### User Story 5 — SOP 编译为 Workflow（Phase 2，可选） (Priority: P2)

作为系统，我可以将结构化 SOP 编译为 Workflow DAG，利用工作流引擎的确定性执行能力，进一步提升稳定性。

**Acceptance Scenarios**:

1. **Given** 一个通过校验的结构化 SOP，**When** 管理员点击"编译为 Workflow"，**Then** 系统自动生成 `WorkflowDefinition`，每个 SOP Step 成为一个 Agent/Tool 节点，步骤间的判定条件成为 Condition 边。
2. **Given** 编译生成的 Workflow，**When** 管理员查看，**Then** 可在 Workflow 画布上看到 SOP 步骤的 DAG 可视化。
3. **Given** SOP 更新后生成了 Workflow v2，**When** 管理员选择使用 Workflow 模式执行，**Then** 后续该类告警使用 Workflow Engine 而非 Agent 自由执行。

---

### Edge Cases

- SOP 中的步骤有条件分支（"如果指标 < 50% 跳到 Step 4"）→ 解析为条件边，执行时由 Agent 判断或由 ConditionEvaluator 计算
- 工具返回超大 JSON（>100KB）→ 截断后注入 Agent 上下文，完整结果存日志
- Agent 在约束模式下完全拒绝执行（如 LLM 认为操作危险）→ 记录 `AgentRefused` 事件，暂停并通知人工
- SOP 步骤间有隐式依赖（Step 3 使用 Step 1 返回的 Pod 名称）→ 通过 `StepOutputs` 字典传递上下文
- 步骤重试时数据源状态已变化（如故障已自恢复）→ 重试使用最新数据，Agent 可能判断"已恢复"并标记成功

## Requirements

### Functional Requirements

- **FR-001**: 系统 MUST 提供 `SopStructuredParser`，将 SOP Markdown 解析为 `List<SopStepDefinition>` 结构。
- **FR-002**: `SopStepDefinition` MUST 包含：StepNumber、Description、ToolName?、ParameterTemplate?、ExpectedOutcome?、TimeoutSeconds、RequiresApproval、StepType（Structured | Freeform）。
- **FR-003**: SOP 执行引擎 MUST 在每步执行前注入当前步骤的完整上下文到 Agent 的 system message。
- **FR-004**: SOP 执行引擎 MUST 检查 Agent 的工具调用是否匹配当前步骤声明的工具。未声明的工具调用 MUST 被拒绝。
- **FR-005**: 参数偏差检测为 Soft Constraint：记录 Warning 但不阻断执行。
- **FR-006**: 循环检测 MUST 在连续 3 次相同工具调用后触发步骤跳过。
- **FR-007**: Incident MUST 新增 `StepExecutions: List<SopStepExecutionVO>` 字段，记录每步状态和结果。
- **FR-008**: 每步状态变更 MUST 通过 SignalR `IncidentHub` 推送 `StepProgressChanged` 事件。
- **FR-009**: 系统 MUST 提供 POST `/api/incidents/{id}/steps/{stepNumber}/retry` API，支持从失败步骤重试。
- **FR-010**: 重试次数 MUST 限制为每步最多 3 次。
- **FR-011**: Agent 的 `AllowedTools` MUST 在 SOP 执行期间自动限制为 SOP 声明的工具列表。
- **FR-012**: SOP 执行过程中每步输出 MUST 存入 `StepOutputs` 字典，供后续步骤引用。

### Non-Functional Requirements

- **NFR-001**: 步骤上下文注入 MUST 在 100ms 内完成。
- **NFR-002**: 工具调用校验（名称匹配 + 参数偏差检测）MUST 在 50ms 内完成。
- **NFR-003**: 步骤级状态推送延迟 MUST < 500ms。
- **NFR-004**: SOP → Workflow 编译 MUST 在 5 秒内完成。

### Key Entities (New/Modified)

**SopStepDefinition (New Value Object)**:
- `StepNumber: int`
- `Description: string`
- `StepType: SopStepType`（Structured | Freeform）
- `ToolName: string?`（结构化步骤必填）
- `ParameterTemplate: JsonElement?`（工具参数模板，可含 `${variable}` 占位符）
- `ExpectedOutcome: string?`（预期结果描述，供 Agent 判断）
- `TimeoutSeconds: int = 300`
- `RequiresApproval: bool = false`

**SopStepExecutionVO (New Value Object)**:
- `StepNumber: int`
- `Status: StepExecutionStatus`（Pending | Running | Completed | Failed | Skipped）
- `StartedAt: DateTime?`
- `CompletedAt: DateTime?`
- `ToolCallResult: JsonElement?`（工具返回结果）
- `AgentJudgment: string?`（Agent 对结果的判断结论）
- `RetryCount: int`
- `ParameterDeviations: List<string>?`（参数偏差记录）
- `ErrorMessage: string?`

**Incident (Modified)**:
- 新增 `SopSteps: List<SopStepDefinition>?`（解析后的 SOP 步骤定义）
- 新增 `StepExecutions: List<SopStepExecutionVO>`（步骤执行记录）
- 新增 `StepOutputs: Dictionary<int, JsonElement>?`（步骤间输出传递）

**IncidentDispatcherService (Modified)**:
- `DispatchSopExecutionAsync` 重构为分步执行模式，每步独立循环

## Success Criteria

- **SC-001**: 同一 SOP + 同一告警输入，连续 10 次执行的工具调用序列一致率 > 95%。
- **SC-002**: 未声明工具调用的拦截率 = 100%。
- **SC-003**: 100% 的 SOP 执行有步骤级追踪记录。
- **SC-004**: 前端步骤进度条在状态变更后 1 秒内更新。
- **SC-005**: 失败步骤重试成功后，后续步骤可自动继续执行。

## Assumptions

- 所有进入 Chain A 执行的 SOP 已通过 022-spec 的结构化校验。
- SOP Markdown 遵循标准模板（022-spec 中定义的格式），解析器不需要处理任意格式。
- Agent 使用的 LLM Provider 支持 Temperature=0 配置。
- 步骤间的输出传递通过 JSON 序列化，不需要类型安全保证。
- 工具调用拦截在 `IncidentDispatcherService` 层面实现，不修改底层 `IChatClient`。
