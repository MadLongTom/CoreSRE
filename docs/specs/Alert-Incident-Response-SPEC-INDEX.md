# CoreSRE — SRE 告警驱动故障应急 Spec 总览

**文档编号**: Alert-Incident-Response-SPEC-INDEX  
**版本**: 1.1.0  
**创建日期**: 2026-02-25  
**关联文档**: [SPEC-INDEX](SPEC-INDEX.md) | [SRE-Alert-Incident-Response-Design](../SRE-Alert-Incident-Response-Design.md) | [INCOMPLETE-SPEC-INDEX](INCOMPLETE-SPEC-INDEX.md)  

> 将设计方案分解为 9 个可独立交付的 Spec，按依赖顺序排列。每个 Spec 对应一个 feature branch 和一个 `specs/1XX-*` 目录。  
> 编号范围 `110-118`，归属 **模块 M11: SRE Alert Incident Response**。

---

## Spec 总览

| SPEC-ID | 目录 | 标题 | 状态 | 依赖 | 优先级 |
|---------|------|------|------|------|--------|
| SPEC-110 | `110-incident-domain-model` | 告警事故领域模型 | Not Started | — | P0 |
| SPEC-111 | `111-alert-rule-crud` | AlertRule CRUD | Not Started | SPEC-110 | P0 |
| SPEC-112 | `112-webhook-alert-routing` | Webhook 告警路由改造 | Not Started | SPEC-111 | P0 |
| SPEC-113 | `113-sop-execution-chain` | 链路 A — SOP 自动执行 | Not Started | SPEC-112 | P1 |
| SPEC-114 | `114-rca-team-analysis` | 链路 B — 根因分析 Team | Not Started | SPEC-113 | P1 |
| SPEC-115 | `115-sop-auto-generation` | 链路 C — SOP 自动生成 | Not Started | SPEC-114 | P2 |
| SPEC-116 | `116-incident-realtime-ui` | 前端：Incident 实时推送 + 详情页 | Not Started | SPEC-113 | P1 |
| SPEC-117 | `117-alert-rule-ui` | 前端：AlertRule 管理 + 执行看板 | Not Started | SPEC-116 | P2 |
| SPEC-118 | `118-incident-notification` | 通知渠道集成（预留） | Not Started | SPEC-117 | P3 |

---

## 依赖关系图

```
SPEC-110 ─────▶ SPEC-111 ─────▶ SPEC-112 ─────▶ SPEC-113 ─────▶ SPEC-114 ─────▶ SPEC-115
 (Domain)       (AlertRule       (Webhook        (SOP 执行)       (RCA Team)       (SOP 生成)
                 CRUD)            路由)
                                                     │
                                                     ▼
                                                 SPEC-116 ─────▶ SPEC-117 ─────▶ SPEC-118
                                                (Incident UI)   (AlertRule UI)   (通知集成)
```

---

## SPEC-110: 告警事故领域模型

**Branch**: `110-incident-domain-model`  
**设计文档章节**: §2 新增领域模型, §2.1-2.3  
**Priority**: P0（全链路基础）

### 范围

- **`AlertRule` 实体**: Name, Status(Active/Inactive), Matchers(List\<AlertMatcherVO\>), Severity(P1-P4), SopId?, ResponderAgentId?, TeamAgentId?, SummarizerAgentId?, NotificationChannels, CooldownMinutes, Tags
- **`Incident` 实体**: Title, Severity, Status(Open→Investigating→Mitigated→Resolved→Closed), AlertRuleId?, AlertFingerprint, AlertPayload(JsonDocument), AlertLabels, Route(SopExecution/RootCauseAnalysis), ConversationId?, SopId?, RootCause?, Resolution?, GeneratedSopId?, StartedAt, ResolvedAt?, MTTD, MTTR, Timeline(List\<IncidentTimelineVO\>)
- **枚举**: AlertRuleStatus, IncidentSeverity, IncidentStatus, IncidentRoute, MatchOp, TimelineEventType
- **值对象**: AlertMatcherVO(Label, Operator, Value), IncidentTimelineVO(Timestamp, EventType, Summary, Details?)
- **SOP 建模决策**: 复用 `SkillRegistration (Category="sop")`，不新建实体
- **EF Core 配置**: 新增 `AlertRuleConfiguration` + `IncidentConfiguration`，JSONB 存储 Matchers / Timeline / AlertPayload / AlertLabels
- **数据库迁移**: 新增 `alert_rules` 和 `incidents` 表

### 交付物

| 文件 | 层 | 说明 |
|------|---|------|
| `AlertRule.cs` | Domain/Entities | 聚合根 |
| `Incident.cs` | Domain/Entities | 聚合根 |
| `AlertRuleStatus.cs` | Domain/Enums | Active / Inactive |
| `IncidentSeverity.cs` | Domain/Enums | P1-P4 |
| `IncidentStatus.cs` | Domain/Enums | Open → Closed 生命周期 |
| `IncidentRoute.cs` | Domain/Enums | SopExecution / RootCauseAnalysis |
| `MatchOp.cs` | Domain/Enums | Eq / Neq / Regex / NotRegex |
| `TimelineEventType.cs` | Domain/Enums | AlertReceived / SopStarted / … |
| `AlertMatcherVO.cs` | Domain/ValueObjects | 标签匹配条件 |
| `IncidentTimelineVO.cs` | Domain/ValueObjects | 时间线条目 |
| `AlertRuleConfiguration.cs` | Infrastructure/Persistence | EF 配置（JSONB） |
| `IncidentConfiguration.cs` | Infrastructure/Persistence | EF 配置（JSONB） |
| `YYYYMMDD_AddAlertIncident.cs` | Infrastructure/Migrations | EF 迁移 |
| `AlertRuleDomainTests.cs` | Domain.Tests 或 Application.Tests | 领域逻辑单元测试 |

### 验收条件

1. `AlertRule` 和 `Incident` 实体可正常通过 EF Core 持久化到 PostgreSQL
2. JSONB 字段（Matchers, Timeline, AlertPayload, AlertLabels）序列化/反序列化正确
3. `IncidentStatus` 生命周期状态机合法（不能从 Resolved 跳到 Open）
4. `AlertMatcherVO` 的 Regex/NotRegex 匹配逻辑正确
5. SOP 复用 `SkillRegistration`，通过 `Category == "sop"` + `AlertRule.SopId` 关联

---

## SPEC-111: AlertRule CRUD

**Branch**: `111-alert-rule-crud`  
**设计文档章节**: §7.1 Commands (Create/UpdateAlertRule), §7.2 Queries (List/GetAlertRule), §8.1 API 端点  
**Priority**: P0  
**依赖**: SPEC-110

### 范围

- **Commands**: `CreateAlertRuleCommand` + `UpdateAlertRuleCommand`（含 Handler）
- **Queries**: `ListAlertRulesQuery`（支持 Status/Severity 过滤）+ `GetAlertRuleByIdQuery`
- **API 端点**: `POST/GET /api/alert-rules`, `GET/PUT/DELETE /api/alert-rules/{id}`
- **DTO**: `AlertRuleDto`, `CreateAlertRuleRequest`, `UpdateAlertRuleRequest`
- **验证**: Matchers 非空、Severity 合法、SopId/TeamAgentId 互斥校验（有 SOP 则走链路 A，无则必须配 TeamAgent）
- **删除**: 软删除或硬删除（如有关联 Incident 则禁止删除）

### 交付物

| 文件 | 层 | 说明 |
|------|---|------|
| `CreateAlertRuleCommand.cs` | Application/Commands | + Handler |
| `UpdateAlertRuleCommand.cs` | Application/Commands | + Handler |
| `DeleteAlertRuleCommand.cs` | Application/Commands | + Handler |
| `ListAlertRulesQuery.cs` | Application/Queries | + Handler（支持过滤） |
| `GetAlertRuleByIdQuery.cs` | Application/Queries | + Handler |
| `AlertRuleDto.cs` | Application/DTOs | 响应 DTO |
| `CreateAlertRuleRequest.cs` | Application/DTOs | 请求 DTO |
| `AlertRuleEndpoints.cs` | Backend/Endpoints | MinimalAPI 端点 |
| `AlertRuleCrudTests.cs` | Application.Tests | CRUD 全流程测试 |

### 验收条件

1. `POST /api/alert-rules` 创建规则，返回 201 + AlertRuleDto
2. `GET /api/alert-rules` 列出所有规则，支持 `?status=Active` 过滤
3. `PUT /api/alert-rules/{id}` 更新规则，SopId/TeamAgentId 互斥校验通过
4. `DELETE /api/alert-rules/{id}` 删除规则，有关联 Incident 时返回 409
5. 所有端点 400/404 错误处理正确

---

## SPEC-112: Webhook 告警路由改造

**Branch**: `112-webhook-alert-routing`  
**设计文档章节**: §6 Webhook 端点改造, §6.1-6.3  
**Priority**: P0  
**依赖**: SPEC-111

### 范围

- **Webhook 端点改造**: `POST /api/datasources/webhook/{dataSourceId}` — 现仅 ACK，改为：解析 → 匹配 → 去重 → 派发 Command
- **`AlertmanagerPayloadParser`**: 解析 Alertmanager JSON → `List<AlertVO>`
- **`MatchAlertRulesQuery` + Handler**: 遍历 Active AlertRule，逐条匹配标签（支持 Eq/Neq/Regex/NotRegex）
- **去重逻辑**: 去重键 = AlertRuleId + Fingerprint，冷却窗口 = CooldownMinutes，重复告警追加 Timeline "AlertRepeated"
- **Command 派发**: SopId != null → `DispatchSopExecutionCommand`; SopId == null → `DispatchRootCauseAnalysisCommand`
- **响应**: `{ success, incidentIds[], ignoredCount }`

### 交付物

| 文件 | 层 | 说明 |
|------|---|------|
| `AlertmanagerPayloadParser.cs` | Infrastructure/Services | 解析 Alertmanager JSON → `List<AlertVO>` |
| `MatchAlertRulesQuery.cs` | Application/Queries | + Handler（遍历 Active AlertRule，逐条 match labels） |
| `AlertVO.cs` | Application/DTOs | 解析后的告警值对象 |
| `DispatchSopExecutionCommand.cs` | Application/Commands | 仅定义，Handler 在 SPEC-113 |
| `DispatchRootCauseAnalysisCommand.cs` | Application/Commands | 仅定义，Handler 在 SPEC-114 |
| `WebhookRoutingTests.cs` | Application.Tests | 解析 + 匹配 + 去重单元测试 |
| `AlertmanagerPayloadParserTests.cs` | Infrastructure.Tests | payload 解析单元测试 |

### 验收条件

1. Alertmanager 格式的 JSON payload 被正确解析为 `AlertVO` 列表
2. 标签匹配：Eq/Neq 精确匹配，Regex/NotRegex 正则匹配
3. 去重：相同 fingerprint 在冷却窗口内不重复创建 Incident
4. 无匹配规则的告警仅记录日志，不创建 Incident
5. 有匹配规则时正确派发 `DispatchSopExecutionCommand` 或 `DispatchRootCauseAnalysisCommand`
6. 返回 `{ success: true, incidentIds: [...], ignoredCount: N }`

---

## SPEC-113: 链路 A — SOP 自动执行

**Branch**: `113-sop-execution-chain`  
**设计文档章节**: §3 链路 A, §9 后台服务, §9.1 IncidentDispatcherService  
**Priority**: P1  
**依赖**: SPEC-112

### 范围

- **`DispatchSopExecutionCommand` Handler**: 创建 Incident(Route=SopExecution) → 获取 ResponderAgent → 确认 Agent 已绑定 SOP Skill → 创建 Conversation → 后台执行
- **`IncidentDispatcherService`**: 后台服务，接收 Command 后：构造首条消息（注入告警上下文 + SOP 指令） → 调用 `AgentChatService.SendMessageAsync()` → 监控完成 → 提取结论 → 更新 Incident(Resolution, Status=Resolved) → 追加 Timeline
- **首条消息模板**: 参见设计文档 §3.3（告警上下文 + SOP 步骤执行指令）
- **超时控制**: 15 分钟硬超时，超时后 Incident 保持 Investigating，标记需人工介入
- **对话持久化**: 复用现有 `AgentSessionStore`，`Incident.ConversationId` 关联对话

### 交付物

| 文件 | 层 | 说明 |
|------|---|------|
| `DispatchSopExecutionCommandHandler.cs` | Application/Commands | Handler 实现 |
| `IncidentDispatcherService.cs` | Infrastructure/Services | 后台执行服务（SOP 部分） |
| `SopMessageTemplates.cs` | Application/Services | 首条消息模板构建 |
| `UpdateIncidentStatusCommand.cs` | Application/Commands | + Handler（更新状态 + Timeline） |
| `SopExecutionChainTests.cs` | Application.Tests | SOP 执行链路集成测试 |

### 验收条件

1. `DispatchSopExecutionCommand` 正确创建 Incident(Route=SopExecution) + Conversation
2. Agent 按 SOP 步骤执行工具调用
3. 执行完成 → Incident.Status = Resolved，记录 Resolution + MTTR
4. 超时 → Incident 保持 Investigating，Timeline 记录 "Timeout — 需人工介入"
5. Incident.ConversationId 关联正确，可通过 API 查询对话

---

## SPEC-114: 链路 B — 根因分析 Team

**Branch**: `114-rca-team-analysis`  
**设计文档章节**: §4 链路 B, §4.1-4.5, §9.1 IncidentDispatcherService  
**Priority**: P1  
**依赖**: SPEC-113

### 范围

- **`DispatchRootCauseAnalysisCommand` Handler**: 创建 Incident → 获取 Team Agent → 创建 Conversation → 后台执行
- **`IncidentDispatcherService`（RCA 部分）**: 注入告警上下文 → 调用 TeamOrchestrator（MagneticOne） → 提取根因 → 更新 Incident.RootCause
- **首条消息模板**: 参见设计文档 §4.5
- **推荐 Team 配置**: MagneticOne, 5 参与者, MaxIterations=20-40, MaxStalls=3
- **完成检测**: 等待 `RunStreamingAsync()` 迭代结束

### 交付物

| 文件 | 层 | 说明 |
|------|---|------|
| `DispatchRootCauseAnalysisCommandHandler.cs` | Application/Commands | Handler 实现 |
| `IncidentDispatcherService.cs` | Infrastructure/Services | 扩展 RCA 部分（同文件） |
| `RcaMessageTemplates.cs` | Application/Services | RCA 消息模板 |
| `RcaTeamAnalysisTests.cs` | Application.Tests | RCA 链路测试 |

### 验收条件

1. 无 SOP 的告警触发 → 创建 Incident(Route=RootCauseAnalysis) + Team Conversation
2. Team Agent 以 MagneticOne 模式执行多 Agent 协作
3. 完成后 Incident.RootCause 非空
4. 完成后自动触发 `GenerateSopFromIncidentCommand`（链路 C 入口）
5. 超时 → 标记需人工介入

---

## SPEC-115: 链路 C — SOP 自动生成

**Branch**: `115-sop-auto-generation`  
**设计文档章节**: §5 链路 C, §5.1-5.5  
**Priority**: P2  
**依赖**: SPEC-114

### 范围

- **`GenerateSopFromIncidentCommand` Handler**: 获取团队对话记录 → 创建 Summarizer Conversation → 调用总结 Agent → 解析输出
- **SOP → Skill 映射**: 解析 Agent 输出的 Markdown → 创建 `SkillRegistration(Category="sop")`
- **自动工具绑定**: 正则提取函数名 → 反查 DataSource/ToolRegistration → 填充 RequiresTools/AllowedTools
- **自动创建 ResponderAgent**: ChatClient Agent + SkillRefs + ToolRefs + DataSourceRefs
- **自动更新 AlertRule**: `SopId = 新 Skill ID`, `ResponderAgentId = 新 Agent ID`
- **SOP 生成 Prompt**: 参见设计文档 §5.3

### 交付物

| 文件 | 层 | 说明 |
|------|---|------|
| `GenerateSopFromIncidentCommandHandler.cs` | Application/Commands | Handler |
| `SopParserService.cs` | Infrastructure/Services | 解析 LLM 输出 → Skill + Tools |
| `ToolBindingResolver.cs` | Infrastructure/Services | 函数名 → DataSource/Tool ID 反查 |
| `SopGenerationPromptBuilder.cs` | Application/Services | SOP 生成 Prompt 模板 |
| `SopGenerationTests.cs` | Application.Tests | 输出解析 + 工具绑定测试 |
| `SopParserServiceTests.cs` | Infrastructure.Tests | Markdown 解析单元测试 |

### 验收条件

1. 链路 B 完成后自动触发 SOP 生成
2. 总结 Agent 输出的 Markdown 被正确解析为 `SkillRegistration`
3. 工具依赖被正确提取并绑定到 Skill + Agent
4. 新建的 ResponderAgent 绑定了正确的 SkillRefs / ToolRefs / DataSourceRefs
5. AlertRule 被更新为 SopId + ResponderAgentId
6. Incident.GeneratedSopId 被记录
7. 后续相同告警走链路 A（验证 SopId 非空）

---

## SPEC-116: 前端 — Incident 实时推送 + 详情页

**Branch**: `116-incident-realtime-ui`  
**设计文档章节**: §13 前端实时体验设计, §13.1-13.7  
**Priority**: P1  
**依赖**: SPEC-113（后端 Incident 数据可用即可开发前端）

### 范围

- **后端 `IncidentHub`（SignalR）**: 13 个 Server→Client 事件 + `JoinIncident` / `JoinIncidentList`
- **前端路由**: `/incidents`, `/incidents/:id`
- **Sidebar 导航**: 新增"事故"入口
- **`IncidentListPage`**: 事故列表 + 实时状态刷新 + 进度摘要
- **`IncidentDetailPage`**: 三栏布局（Timeline / 对话区 / Context Panel）
- **三种模式差异化渲染**: SOP 执行 / RCA Team / SOP 生成
- **新增组件**: `IncidentCard`, `IncidentTimeline`, `IncidentChatPanel`, `IncidentContextPanel`, `SopStepProgress`, `RcaAgentStatus`, `RcaLedgerPanel`, `SopGenerationResult`, `IncidentSeverityBadge`, `IncidentStatusBadge`
- **新增 Hook**: `useIncidentSignalR`
- **Incident REST API 端点**: `GET /api/incidents`, `GET /api/incidents/{id}`, `GET /api/incidents/{id}/conversation`, `PATCH /api/incidents/{id}/status`, `POST /api/incidents/{id}/escalate`
- **CQRS Queries**: `ListIncidentsQuery`, `GetIncidentByIdQuery`, `GetIncidentConversationQuery`
- **人工介入**: 手动发消息 / 手动接管 / 上报 / 标记已解决

### 交付物

| 文件 | 层 | 说明 |
|------|---|------|
| `IncidentHub.cs` | Backend/Hubs | SignalR Hub（13 事件） |
| `IncidentEndpoints.cs` | Backend/Endpoints | Incident REST 端点 |
| `ListIncidentsQuery.cs` | Application/Queries | + Handler |
| `GetIncidentByIdQuery.cs` | Application/Queries | + Handler |
| `GetIncidentConversationQuery.cs` | Application/Queries | + Handler |
| `IncidentDto.cs` | Application/DTOs | 响应 DTO |
| `IncidentListPage.tsx` | Frontend/pages | 事故列表页 |
| `IncidentDetailPage.tsx` | Frontend/pages | 三栏详情页 |
| `IncidentCard.tsx` | Frontend/components | 列表卡片 |
| `IncidentTimeline.tsx` | Frontend/components | 左栏时间线 |
| `IncidentChatPanel.tsx` | Frontend/components | 中栏对话 |
| `IncidentContextPanel.tsx` | Frontend/components | 右栏上下文 |
| `SopStepProgress.tsx` | Frontend/components | SOP 步骤进度 |
| `RcaAgentStatus.tsx` | Frontend/components | RCA Agent 状态 |
| `RcaLedgerPanel.tsx` | Frontend/components | RCA Ledger |
| `SopGenerationResult.tsx` | Frontend/components | SOP 生成结果 |
| `IncidentSeverityBadge.tsx` | Frontend/components | P1-P4 badge |
| `IncidentStatusBadge.tsx` | Frontend/components | 状态 badge |
| `use-incident-signalr.ts` | Frontend/hooks | SignalR hook |
| `incident.ts` | Frontend/types | TypeScript 类型定义 |

### 验收条件

1. `/incidents` 列表页实时显示新增/状态变更的 Incident
2. `/incidents/:id` 三栏布局正确——Timeline、对话流、Context Panel
3. SOP 执行模式：中栏单 Agent 对话 + 右栏 SOP 步骤进度
4. RCA 模式：中栏 Team 多 Agent 对话 + 右栏 Ledger + Agent 状态
5. SOP 生成模式：中栏总结 Agent 对话 + 右栏生成结果
6. SignalR 连接稳定，断线自动重连
7. 人工介入操作（发消息/接管/上报/标记已解决）可用

---

## SPEC-117: 前端 — AlertRule 管理 + 执行看板

**Branch**: `117-alert-rule-ui`  
**设计文档章节**: §13.1 路由（alert-rules 相关）, §8.1 AlertRule CRUD API  
**Priority**: P2  
**依赖**: SPEC-116

### 范围

- **前端路由**: `/alert-rules`, `/alert-rules/new`, `/alert-rules/:id`
- **Sidebar 导航**: 新增"告警规则"入口
- **`AlertRuleListPage`**: 规则列表 + 状态 toggle（Active/Inactive）
- **`AlertRuleFormPage`**: 创建/编辑表单（标签匹配器编辑器 + Agent/SOP 选择器 + 通知渠道）
- **`AlertRuleDetailPage`**: 规则详情 + 关联 Incident 历史
- **执行看板**: Incident 统计（按 Severity 分布、MTTR 趋势、SOP 覆盖率）

### 交付物

| 文件 | 层 | 说明 |
|------|---|------|
| `AlertRuleListPage.tsx` | Frontend/pages | 规则列表 |
| `AlertRuleFormPage.tsx` | Frontend/pages | 创建/编辑表单 |
| `AlertRuleDetailPage.tsx` | Frontend/pages | 规则详情 |
| `AlertRuleForm.tsx` | Frontend/components | 表单组件（Matcher 编辑器） |
| `MatcherEditor.tsx` | Frontend/components | 标签匹配条件编辑器 |
| `IncidentDashboard.tsx` | Frontend/components | 执行看板（统计图表） |
| `alert-rule.ts` | Frontend/types | TypeScript 类型定义 |

### 验收条件

1. AlertRule CRUD 全流程可用（创建/编辑/删除/列表/详情）
2. 标签匹配器编辑器支持 Eq/Neq/Regex/NotRegex
3. Agent 和 SOP（Skill）选择器正确关联
4. 执行看板显示 Incident 统计数据

---

## SPEC-118: 通知渠道集成（预留）

**Branch**: `118-incident-notification`  
**设计文档章节**: §12 Phase 9  
**Priority**: P3  
**依赖**: SPEC-117

### 范围

- **通知渠道抽象**: `INotificationChannel` 接口
- **Slack 集成**: Slack Webhook → Incident 摘要卡片
- **Teams 集成**: Teams Webhook → Adaptive Card
- **Email 集成**: SMTP → 邮件通知（预留）
- **AlertRule.NotificationChannels**: 按规则配置通知目标
- **通知触发时机**: Incident 创建、状态变更、超时需人工介入

### 交付物

| 文件 | 层 | 说明 |
|------|---|------|
| `INotificationChannel.cs` | Application/Interfaces | 通知渠道抽象 |
| `SlackNotificationChannel.cs` | Infrastructure/Services | Slack 实现 |
| `TeamsNotificationChannel.cs` | Infrastructure/Services | Teams 实现 |
| `NotificationDispatcher.cs` | Infrastructure/Services | 按 AlertRule 配置分发通知 |
| `NotificationTests.cs` | Infrastructure.Tests | 通知发送单元测试 |

### 验收条件

1. `INotificationChannel` 抽象可扩展
2. Slack Webhook 发送成功
3. Teams Adaptive Card 发送成功
4. 按 AlertRule.NotificationChannels 正确路由
5. Incident 创建/状态变更/超时 三种场景均触发通知

---

## 开放问题（跨 Spec）

| # | 问题 | 影响 Spec | 建议 |
|---|------|----------|------|
| 1 | 无匹配规则的告警如何处理？ | SPEC-112 | 仅记录日志，不创建 Incident |
| 2 | SOP 自动绑定后是否需人工审核？ | SPEC-115 | 先自动绑定标记 `NeedsReview`，人工确认后激活 |
| 3 | 总结 Agent 生成的 SOP 质量保证？ | SPEC-115 | LLM 自评或"先粗后细"迭代 |
| 4 | Team 对话超时后如何处置？ | SPEC-114 | 标记需人工介入，保留部分结论 |
| 5 | 告警恢复（resolved）通知如何处理？ | SPEC-112 | resolved 状态自动关闭 Incident |
| 6 | SOP 版本管理？ | SPEC-115 | 暂无版本表，视需求追加 |
| 7 | ResponderAgent 是否每条 AlertRule 独享？ | SPEC-113, 115 | 共享——一个 Agent 绑定多个 SOP Skill |
