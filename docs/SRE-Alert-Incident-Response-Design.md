# SRE 告警驱动故障应急自动化 — 设计方案

**文档编号**: DESIGN-022  
**版本**: 0.1 (Draft)  
**日期**: 2026-02-25  
**状态**: REVIEW  

---

## 1. 背景与目标

当 Alertmanager / PagerDuty 触发告警并通过 Webhook 推送到 CoreSRE 后，系统需要自动化地进行故障应急响应。当前 `WebhookEndpoints` 仅 ACK 告警，缺乏后续的自动化处理链路。

### 核心链路

```
                    ┌──────────────────────────────────────────────────────────────────┐
                    │                  Alertmanager Webhook                             │
                    └────────────────────────┬─────────────────────────────────────────┘
                                             │
                                    ┌────────▼────────┐
                                    │  Alert Ingestion │
                                    │  (解析 + 去重)   │
                                    └────────┬────────┘
                                             │
                                    ┌────────▼────────┐
                                    │  AlertRule 匹配  │
                                    │  (label 路由)    │
                                    └────────┬────────┘
                                             │
                              ┌──── 有匹配的 SOP? ────┐
                              │                        │
                         YES ─┤                        ├─ NO
                              │                        │
                    ┌─────────▼─────────┐    ┌────────▼──────────┐
                    │  链路 A: SOP 执行  │    │ 链路 B: 根因分析   │
                    │  单 Agent + Skill  │    │ 多 Agent Team     │
                    └─────────┬─────────┘    └────────┬──────────┘
                              │                       │
                              │              ┌────────▼──────────┐
                              │              │ 链路 C: SOP 生成   │
                              │              │ 总结 Agent 提炼    │
                              │              │ + 自动绑定工具     │
                              │              └────────┬──────────┘
                              │                       │
                    ┌─────────▼───────────────────────▼──────────┐
                    │            Incident 记录 + 通知             │
                    └────────────────────────────────────────────┘
```

---

## 2. 新增领域模型

### 2.1 实体

#### `AlertRule`（告警路由规则）

```
AlertRule : BaseEntity
├── Name: string                          # 规则名称，如 "HighErrorRate-OrderService"
├── Description: string?                  # 规则描述
├── Status: AlertRuleStatus               # Active | Inactive
├── Matchers: List<AlertMatcherVO>        # 标签匹配条件（与 Alertmanager route 对齐）
├── Severity: IncidentSeverity            # P1/P2/P3/P4
├── SopId: Guid?                          # 关联的 SOP（null = 走根因分析链路）
├── ResponderAgentId: Guid?               # SOP 链路：执行 SOP 的单 Agent
├── TeamAgentId: Guid?                    # 根因链路：负责根因分析的 Team Agent
├── SummarizerAgentId: Guid?              # 根因链路：负责生成 SOP 的总结 Agent
├── NotificationChannels: List<string>    # 通知渠道标识（预留：Slack/Teams/Email）
├── CooldownMinutes: int                  # 冷却时间（同指纹告警不重复触发）
└── Tags: Dictionary<string, string>?     # 自定义标签
```

#### `Incident`（故障事件）

```
Incident : BaseEntity
├── Title: string                         # 自动生成或从告警 annotations.summary 提取
├── Severity: IncidentSeverity            # P1-P4
├── Status: IncidentStatus                # Open → Investigating → Mitigated → Resolved → Closed
├── AlertRuleId: Guid?                    # 触发的路由规则
├── AlertFingerprint: string              # Alertmanager fingerprint（去重键）
├── AlertPayload: JsonDocument            # 原始告警 JSON（审计用）
├── AlertLabels: Dictionary<string,string> # 告警标签快照
├── Route: IncidentRoute                  # SopExecution | RootCauseAnalysis
├── ConversationId: Guid?                 # Agent 对话 ID（链接到 Conversation 实体）
├── SopId: Guid?                          # 使用的 SOP
├── RootCause: string?                    # 根因分析结论
├── Resolution: string?                   # 处置结论
├── GeneratedSopId: Guid?                 # 根因链路生成的新 SOP（如果有）
├── StartedAt: DateTime                   # 开始处理时间
├── ResolvedAt: DateTime?                 # 解决时间
├── TimeToDetectMs: long?                 # MTTD（告警触发 → 开始处理）
├── TimeToResolveMs: long?                # MTTR（开始处理 → 解决）
└── Timeline: List<IncidentTimelineVO>    # 事件时间线
```

#### `Sop`（标准操作流程）

> 利用现有的 `SkillRegistration` 建模还是新建实体？

**建议：复用 `SkillRegistration` 而非新建实体。** 理由：

1. Skill 的 `Content` 字段（Markdown 指令体）天然适合存储 SOP 步骤
2. Skill 已有 `AllowedTools` + `RequiresTools` 字段，天然支持"自动绑定工具"
3. Skill 已有 `Category` 字段，可用 `"sop"` 类别区分
4. Agent 已通过 `LlmConfigVO.SkillRefs` 绑定 Skill

因此 **SOP = `SkillRegistration` where `Category == "sop"`**，无需新建实体。`AlertRule.SopId` 实际指向 `SkillRegistration.Id`。

### 2.2 枚举

```csharp
public enum AlertRuleStatus { Active, Inactive }

public enum IncidentSeverity { P1, P2, P3, P4 }

public enum IncidentStatus { Open, Investigating, Mitigated, Resolved, Closed }

public enum IncidentRoute { SopExecution, RootCauseAnalysis }
```

### 2.3 值对象

#### `AlertMatcherVO`（标签匹配条件）

```
AlertMatcherVO
├── Label: string       # 标签名，如 "alertname", "service", "severity"
├── Operator: MatchOp   # Eq | Neq | Regex | NotRegex
└── Value: string       # 匹配值，如 "HighErrorRate", "order-service"
```

#### `IncidentTimelineVO`（事件时间线条目）

```
IncidentTimelineVO
├── Timestamp: DateTime
├── EventType: TimelineEventType   # AlertReceived | SopStarted | AgentMessage | ToolCall |
│                                  # RootCauseFound | SopGenerated | Escalated | Resolved
├── Summary: string
└── Details: string?               # JSON 或纯文本
```

---

## 3. 链路 A：SOP 执行（有标准操作流程）

### 3.1 触发条件

`AlertRule.SopId != null` → 走此链路

### 3.2 流程

```
[Webhook 告警] 
    → 匹配 AlertRule（label matchers）
    → 创建 Incident（Status=Open, Route=SopExecution）
    → 获取 AlertRule.ResponderAgentId 对应的 Agent（类型：ChatClient）
    → 确认 Agent 已绑定 SopId 对应的 Skill（LlmConfigVO.SkillRefs 包含 SopId）
    → 创建 Conversation（AgentId=ResponderAgent）
    → 发起对话：System Prompt 注入告警上下文 + SOP Skill 内容
    → Agent 按 SOP 步骤逐步使用工具执行
    → 完成后 → Incident 记录结论，Status → Resolved
```

### 3.3 消息模板（注入给 Agent 的首条消息）

```markdown
## 告警触发 — SOP 自动执行

**告警名称**: {{alertName}}
**严重等级**: {{severity}}
**触发时间**: {{startsAt}}
**关联服务**: {{service}}

**告警标签**:
{{#each labels}}
- {{key}}: {{value}}
{{/each}}

**告警描述**: {{annotations.description}}

---

请严格按照已绑定的 SOP 技能「{{sopName}}」中的步骤执行故障处置。
每一步执行后，报告执行结果和观察到的状态。
如果 SOP 步骤无法解决问题，请明确说明哪一步失败以及原因。
```

### 3.4 关键设计决策

| 决策点 | 方案 |
|--------|------|
| Agent 类型 | **ChatClient**（需要工具调用 + Skill 注入） |
| 工具绑定 | 通过 `LlmConfigVO.ToolRefs` + `DataSourceRefs` 预先配置在 Agent 上 |
| Skill 绑定 | `LlmConfigVO.SkillRefs` 包含 SOP 的 SkillRegistration ID |
| 执行模式 | 异步——创建 Conversation 后立即返回 Incident ID，后台执行 |
| 对话持久化 | 复用现有 `AgentSessionStore`，ConversationId 记录在 Incident 上 |
| 超时控制 | Agent 层面 `MaxOutputTokens` + 应用层 15 分钟超时 |

---

## 4. 链路 B：根因分析（无 SOP）

### 4.1 触发条件

`AlertRule.SopId == null` → 走此链路

### 4.2 流程

```
[Webhook 告警] 
    → 匹配 AlertRule（label matchers）
    → 创建 Incident（Status=Open, Route=RootCauseAnalysis）
    → 获取 AlertRule.TeamAgentId 对应的 Team Agent
    → 创建 Conversation（AgentId=TeamAgent）
    → 发起 Team 对话，注入告警上下文
    → Team 内多 Agent 协作分析根因（查 Metrics/Logs/Traces/K8s）
    → Team 对话完成 → 提取根因结论 → Incident.RootCause
    → 进入链路 C
```

### 4.3 推荐 Team 配置

| 参数 | 推荐值 | 理由 |
|------|--------|------|
| **TeamMode** | `MagneticOne` | 双循环 Ledger 编排，最适合开放性故障诊断 |
| **参与者** | 3-5 个专职 Agent | 各司其职覆盖可观测性全栈 |
| **MaxIterations** | 20-40 | 避免无限循环 |
| **MaxStalls** | 3 | 检测到停滞后及时终止 |

### 4.4 推荐参与 Agent 角色

| Agent 名称 | 职责 | 绑定数据源/工具 |
|-----------|------|----------------|
| `sre-metrics-analyst` | 查询 Prometheus 指标，分析时序异常 | Prometheus DataSource |
| `sre-logs-analyst` | 查询 Loki 日志，提取错误模式 | Loki DataSource |
| `sre-traces-analyst` | 查询 Jaeger 链路追踪，定位延迟瓶颈 | Jaeger DataSource |
| `sre-k8s-operator` | 检查 K8s 资源状态（Pod/Deployment/Events） | Kubernetes DataSource |
| `sre-coordinator` | 综合各分析结果，给出根因结论 | 无特殊工具（汇总角色） |

### 4.5 首条消息模板（注入给 Team 的任务描述）

```markdown
## 告警触发 — 根因分析

**告警名称**: {{alertName}}
**严重等级**: {{severity}}
**触发时间**: {{startsAt}}  
**关联服务**: {{service}}

**告警标签**:
{{#each labels}}
- {{key}}: {{value}}
{{/each}}

**告警描述**: {{annotations.description}}

---

请团队协作分析此告警的根本原因。
1. 指标分析师：查询相关服务最近 15 分钟的关键指标（错误率、延迟、吞吐量）
2. 日志分析师：搜索相关服务的错误日志和异常堆栈
3. 链路分析师：查看相关服务的慢请求和错误链路
4. K8s 运维：检查相关 Pod/Deployment 的状态和事件

最终由协调者汇总所有分析结果，给出：
- 根本原因（Root Cause）
- 影响范围（Blast Radius）
- 建议的修复方案（Remediation）
```

---

## 5. 链路 C：SOP 自动生成

### 5.1 触发条件

链路 B 完成后自动触发

### 5.2 流程

```
[链路 B 完成]
    → 获取 Team 对话的完整聊天记录（从 AgentSessionStore）
    → 获取 AlertRule.SummarizerAgentId 对应的总结 Agent（ChatClient）
    → 创建新 Conversation
    → 注入：告警上下文 + 完整团队对话记录 + SOP 生成指令
    → 总结 Agent 生成结构化 SOP（Markdown 格式）
    → 解析输出 → 创建 SkillRegistration（Category="sop"）
    → 解析输出中的工具引用 → 填充 RequiresTools / AllowedTools
    → 自动创建一个 ResponderAgent（ChatClient）并绑定：
        - SkillRefs → [新生成的 SOP Skill]
        - ToolRefs → SOP 中引用的工具
        - DataSourceRefs → SOP 中引用的数据源
    → 更新 AlertRule：SopId = 新 Skill ID, ResponderAgentId = 新 Agent ID
    → 更新 Incident：GeneratedSopId = 新 Skill ID
    → 后续相同告警将走链路 A
```

### 5.3 SOP 生成 Prompt

```markdown
## 任务：根据团队故障分析对话，提炼 SOP

以下是一次告警应急响应的完整对话记录。请从中提炼一份标准操作流程（SOP），
使得未来再次发生相同告警时，单个 Agent 可以按此 SOP 独立执行故障处置。

### 告警信息
- 名称: {{alertName}}
- 标签: {{labels}}
- 根因: {{rootCause}}

### 团队对话记录
{{teamConversationHistory}}

---

### 输出要求

请严格按以下 Markdown 格式输出 SOP：

```markdown
# SOP: {{alertName}} 处置流程

## 适用条件
- 告警标签匹配: (列出关键标签)

## 工具依赖
- [ ] `tool_name_1` — 用途说明
- [ ] `tool_name_2` — 用途说明
- [ ] `datasource_function_1` — 用途说明

## 操作步骤

### Step 1: 确认告警状态
**操作**: 调用 `list_alerts_xxx` 确认告警仍在 firing
**预期**: 告警状态为 active
**如果异常**: 告警已自动恢复，记录并关闭

### Step 2: ...
(逐步列出诊断和修复步骤)

### Step N: 验证修复
**操作**: 等待 2 分钟后重新查询指标
**预期**: 错误率回落到正常水平
**如果异常**: 上报人工介入

## 回滚方案
(如果修复操作导致更大问题，描述回滚步骤)
```
```

### 5.4 SOP → Skill 的字段映射

| SOP 输出 | SkillRegistration 字段 |
|----------|----------------------|
| SOP 标题 | `Name`（kebab-case 化） |
| SOP 全文 | `Content` |
| "适用条件"描述 | `Description`（供 LLM 判断适用性） |
| "工具依赖"中提取的工具名 | `RequiresTools` → 反查 ToolRegistration/DataSource ID |
| 固定值 | `Category = "sop"`, `Scope = User`, `Status = Active` |

### 5.5 自动工具绑定逻辑

```
解析 SOP Content 中引用的函数名（正则: `\`([a-z_]+_[a-z0-9-]+)\``）
    → 匹配 DataSourceRegistration.GenerateAvailableFunctionNames() 
        → 找到对应的 DataSourceId → 加入 Agent 的 DataSourceRefs
    → 匹配 ToolRegistration.Name / McpToolItem.Name
        → 找到对应的 ToolId → 加入 Agent 的 ToolRefs
    → 匹配 SkillRegistration（Category="sop"）中的 AllowedTools
        → 填充 Skill 的 RequiresTools
```

---

## 6. Webhook 端点改造

### 6.1 现状

```csharp
// WebhookEndpoints.cs — 当前仅 ACK
// TODO: In future, trigger AIOps workflow based on alert content
```

### 6.2 改造方案

```
POST /api/datasources/webhook/{dataSourceId}
    │
    ├── 1. 验证 DataSource 存在且为 Alerting 类型
    ├── 2. 解析 Alertmanager payload → List<AlertVO>
    ├── 3. 对每个 alert：
    │   ├── a. 去重检查（fingerprint + cooldown）
    │   ├── b. 匹配 AlertRule（遍历 Active 规则，标签匹配）
    │   ├── c. 如无匹配规则 → 仅记录日志，不创建 Incident
    │   ├── d. 如有匹配：
    │   │   ├── SopId != null → 发布 DispatchSopExecutionCommand
    │   │   └── SopId == null → 发布 DispatchRootCauseAnalysisCommand
    │   └── e. 返回 ACK（不等待处理完成）
    └── 4. Response: { success, incidentIds[], ignoredCount }
```

### 6.3 去重策略

```
去重键 = AlertRule.Id + Alert.Fingerprint
冷却窗口 = AlertRule.CooldownMinutes（默认 15 分钟）

查询: 最近 {CooldownMinutes} 分钟内是否存在
      同 AlertRuleId + 同 Fingerprint + Status != Closed 的 Incident
如果存在 → 跳过，在已有 Incident Timeline 追加 "AlertRepeated" 事件
如果不存在 → 创建新 Incident
```

---

## 7. CQRS Commands

### 7.1 新增 Commands

| Command | 触发来源 | 行为 |
|---------|---------|------|
| `DispatchSopExecutionCommand` | Webhook 路由 | 创建 Incident → 创建 Conversation → 发起 Agent 对话（后台任务） |
| `DispatchRootCauseAnalysisCommand` | Webhook 路由 | 创建 Incident → 创建 Conversation → 发起 Team 对话（后台任务） |
| `GenerateSopFromIncidentCommand` | 链路 B 完成回调 | 获取对话历史 → 调用总结 Agent → 创建 Skill → 更新 AlertRule |
| `CreateAlertRuleCommand` | API CRUD | 创建告警路由规则 |
| `UpdateAlertRuleCommand` | API CRUD / SOP 自动绑定 | 更新告警路由规则 |
| `UpdateIncidentStatusCommand` | 各链路回调 / API | 更新 Incident 状态 + Timeline |

### 7.2 新增 Queries

| Query | 用途 |
|-------|------|
| `ListAlertRulesQuery` | 列出所有告警规则（支持过滤） |
| `GetAlertRuleByIdQuery` | 获取单条规则详情 |
| `MatchAlertRulesQuery` | 根据告警标签匹配规则（内部使用） |
| `ListIncidentsQuery` | 列出事故（支持状态/严重等级/时间范围过滤） |
| `GetIncidentByIdQuery` | 获取事故详情（含 Timeline） |
| `GetIncidentConversationQuery` | 获取事故关联的对话记录 |

---

## 8. API 端点

### 8.1 AlertRule CRUD

```
POST   /api/alert-rules                  # 创建告警规则
GET    /api/alert-rules                  # 列出所有规则
GET    /api/alert-rules/{id}             # 获取规则详情
PUT    /api/alert-rules/{id}             # 更新规则
DELETE /api/alert-rules/{id}             # 删除规则
```

### 8.2 Incident

```
GET    /api/incidents                    # 列出事故（?status=Open&severity=P1）
GET    /api/incidents/{id}               # 事故详情（含 Timeline）
GET    /api/incidents/{id}/conversation  # 事故关联的 Agent 对话
PATCH  /api/incidents/{id}/status        # 手动更新事故状态
POST   /api/incidents/{id}/escalate      # 手动上报（预留）
```

---

## 9. 后台服务

### 9.1 `IncidentDispatcherService`（Scoped Background Service）

职责：接收 `DispatchSopExecutionCommand` / `DispatchRootCauseAnalysisCommand` 后，在后台执行 Agent 对话。

```
处理 SOP 执行:
    1. 解析 Agent + Skill
    2. 创建 Conversation
    3. 构造首条消息（注入告警上下文）
    4. 调用 AgentChatService.SendMessageAsync()（非 HTTP，内部服务调用）
    5. 监控对话完成
    6. 提取结论 → 更新 Incident（Resolution, Status=Resolved）
    7. 追加 Timeline 事件

处理根因分析:
    1. 解析 Team Agent + 参与者
    2. 创建 Conversation
    3. 构造首条消息（注入告警上下文）
    4. 调用 TeamOrchestrator 执行 Team 对话
    5. 对话完成 → 提取根因 → 更新 Incident（RootCause）
    6. 发布 GenerateSopFromIncidentCommand → 触发链路 C
```

### 9.2 对话完成检测

| 方案 | 实现 |
|------|------|
| **ChatClient Agent** | `SendMessageAsync` 返回即完成 |
| **Team Agent (MagneticOne)** | 监听 `RUN_FINISHED` 事件，或等待 `BuildTeamAgent().RunStreamingAsync()` 迭代结束 |
| **超时** | 配置 15 分钟硬超时，超时后 Incident 标记为 `Investigating`（需人工介入） |

---

## 10. 数据流整体示意

```
┌─────────────┐
│ Alertmanager │
└──────┬──────┘
       │ POST /api/datasources/webhook/{id}
       ▼
┌──────────────────┐     ┌──────────────┐
│ WebhookEndpoints │────▶│ MatchAlertRules│
│ (parse + dedup)  │     │ (label match) │
└──────────────────┘     └──────┬───────┘
                                │
                    ┌───────────┴───────────┐
                    │                       │
            SopId != null            SopId == null
                    │                       │
            ┌───────▼────────┐    ┌────────▼────────┐
            │ Dispatch SOP   │    │ Dispatch RCA     │
            │ Execution      │    │ (Root Cause      │
            │ Command        │    │  Analysis)       │
            └───────┬────────┘    └────────┬────────┘
                    │                      │
            ┌───────▼────────┐    ┌────────▼────────┐
            │ ChatClient     │    │ Team Agent       │
            │ Agent          │    │ (MagneticOne)    │
            │ + SOP Skill    │    │ 5 SRE Agents     │
            │ + Tools        │    │ + DataSources    │
            └───────┬────────┘    └────────┬────────┘
                    │                      │
                    │              ┌───────▼────────┐
                    │              │ Summarizer      │
                    │              │ Agent           │
                    │              │ → 生成 SOP Skill │
                    │              │ → 创建 Agent    │
                    │              │ → 绑定 AlertRule│
                    │              └───────┬────────┘
                    │                      │
            ┌───────▼──────────────────────▼────────┐
            │         Incident (Resolved)           │
            │  + ConversationId + Timeline + MTTR   │
            └───────────────────────────────────────┘
```

---

## 11. 与现有系统的集成点

| 现有组件 | 集成方式 | 改动程度 |
|---------|---------|---------|
| `WebhookEndpoints` | 改造：解析 → 匹配 → 派发 Command | **中** |
| `AgentRegistration` | 无改动，创建新的 SRE Agent 实例 | **无** |
| `SkillRegistration` | 无改动，SOP 作为 Category="sop" 的 Skill | **无** |
| `LlmConfigVO` | 无改动，Agent 绑定 Skill/Tool/DataSource 已支持 | **无** |
| `TeamConfigVO` | 无改动，MagneticOne 模式已支持 | **无** |
| `AgentSessionStore` | 无改动，对话持久化已支持 | **无** |
| `Conversation` | 无改动，Incident 关联 ConversationId | **无** |
| `DataSourceFunctionFactory` | 无改动，DataSource 函数已可绑定到 Agent | **无** |
| `AlertmanagerQuerier` | 无改动，Agent 可通过 DataSourceRefs 调用 | **无** |
| 其他 Queriers | 无改动，Prometheus/Loki/Jaeger/K8s 已可用 | **无** |

**新增实体**: `AlertRule`, `Incident`  
**新增枚举**: `AlertRuleStatus`, `IncidentSeverity`, `IncidentStatus`, `IncidentRoute`, `MatchOp`, `TimelineEventType`  
**新增值对象**: `AlertMatcherVO`, `IncidentTimelineVO`  
**新增命令/查询**: 6 Commands + 6 Queries  
**新增端点**: 7 个（AlertRule CRUD 5 + Incident 2+）  
**新增后台服务**: `IncidentDispatcherService`  

---

## 12. 开发阶段建议

| 阶段 | 范围 | 依赖 |
|------|------|------|
| **Phase 1** | Domain 模型（AlertRule + Incident + 枚举 + VO）+ EF 迁移 | 无 |
| **Phase 2** | AlertRule CRUD（Command/Query/Endpoint） | Phase 1 |
| **Phase 3** | Webhook 改造（解析 + 匹配 + 去重） | Phase 2 |
| **Phase 4** | 链路 A — SOP 执行（Dispatch + Agent 对话 + Incident 更新） | Phase 3 |
| **Phase 5** | 链路 B — 根因分析（Team 对话 + Incident 更新） | Phase 4 |
| **Phase 6** | 链路 C — SOP 生成（总结 Agent + Skill 创建 + 工具绑定 + AlertRule 更新） | Phase 5 |
| **Phase 7** | 前端：Incident 列表/详情 + 实时推送 + 内嵌对话 | Phase 4 |
| **Phase 8** | 前端：AlertRule 管理页 + 执行看板 | Phase 7 |
| **Phase 9** | 通知渠道集成（Slack/Teams，预留） | Phase 8 |

---

## 13. 前端实时体验设计

### 13.1 整体交互架构

```
┌───────────────────────────────────────────────────────────────────────────────┐
│                           Sidebar 新增导航项                                  │
│  ┌─────────┐                                                                  │
│  │ 🔔 告警  │  ← 新增，包含未读 badge 计数                                    │
│  │ 📋 事故  │  ← 新增                                                         │
│  └─────────┘                                                                  │
└───────────────────────────────────────────────────────────────────────────────┘

路由新增：
  /incidents                    → IncidentListPage
  /incidents/:id                → IncidentDetailPage（核心页面）
  /alert-rules                  → AlertRuleListPage
  /alert-rules/new              → AlertRuleCreatePage
  /alert-rules/:id              → AlertRuleDetailPage
```

### 13.2 实时推送方案：IncidentHub（SignalR）

复用现有的 SignalR 基础设施（`lib/signalr.ts` 的 `HubConnection` 工厂 + 自动重连模式），新增 `IncidentHub`：

```
后端 Hub:  /hubs/incident
前端 Hook: useIncidentSignalR(incidentId)

Server → Client 事件:
  IncidentCreated          { incidentId, title, severity, route }
  IncidentStatusChanged    { incidentId, oldStatus, newStatus, timestamp }
  SopExecutionStarted      { incidentId, agentId, conversationId, sopName }
  SopExecutionProgress     { incidentId, stepDescription, toolCalls[] }
  SopExecutionCompleted    { incidentId, resolution, mttr }
  RcaStarted               { incidentId, teamAgentId, conversationId, participants[] }
  RcaAgentHandoff          { incidentId, fromAgent, toAgent, reason }
  RcaProgress              { incidentId, agentName, summary }
  RcaLedgerUpdate          { incidentId, outerLedger }             // MagneticOne plan
  RcaCompleted             { incidentId, rootCause, blastRadius }
  SopGenerationStarted     { incidentId, summarizerAgentId }
  SopGenerationCompleted   { incidentId, newSopId, newSopName, boundTools[] }
  TimelineEvent            { incidentId, timelineEntry }

Client → Server:
  JoinIncident(incidentId)     // 订阅单个事故的实时更新
  JoinIncidentList()           // 订阅事故列表的新增/状态变更
  LeaveIncident(incidentId)
```

### 13.3 IncidentListPage（事故列表页）

```
┌──────────────────────────────────────────────────────────────────────┐
│  事故列表                                      [状态筛选 ▾] [P1-P4 ▾] │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  ┌─ 🔴 P1 ─────────────────────────────────────────────────────┐    │
│  │ INC-0042  HighErrorRate — order-service                      │    │
│  │ ⏳ Investigating · SOP 执行中 · 已持续 3m22s                  │    │
│  │ ████████░░░ Step 3/5: 检查副本数...                          │    │
│  └──────────────────────────────────────────────────────────────┘    │
│                                                                      │
│  ┌─ 🟠 P2 ─────────────────────────────────────────────────────┐    │
│  │ INC-0041  HighLatency — payment-service                      │    │
│  │ 🔍 Investigating · 根因分析中 · Team: 5 Agents               │    │
│  │ 当前发言: sre-logs-analyst · "发现大量 timeout 日志..."       │    │
│  └──────────────────────────────────────────────────────────────┘    │
│                                                                      │
│  ┌─ 🟢 Resolved ───────────────────────────────────────────────┐    │
│  │ INC-0040  PodCrashLooping — inventory-service                │    │
│  │ ✅ Resolved · SOP 自动生成完毕 · MTTR: 4m18s                 │    │
│  └──────────────────────────────────────────────────────────────┘    │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

**实时行为**：
- 通过 `JoinIncidentList()` 订阅 SignalR
- 新 Incident 触发 `IncidentCreated` → 列表顶部插入新卡片（带入场动画）
- 状态变更触发 `IncidentStatusChanged` → 卡片状态 badge 实时刷新
- **正在执行的** SOP/RCA 显示实时进度摘要（一行文字 + 进度条/当前 Agent）

### 13.4 IncidentDetailPage（事故详情页 — 核心）

采用**三栏布局**，左中右分别承载不同维度的实时信息：

```
┌────────────────────────────────────────────────────────────────────────┐
│  ← 返回   INC-0042 · HighErrorRate — order-service         🔴 P1     │
│  状态: Investigating   路由: SOP 执行   MTTR: --            ⏱ 3m22s   │
├──────────────┬─────────────────────────────────┬───────────────────────┤
│              │                                 │                       │
│   Timeline   │      Agent 对话（实时）          │    Context Panel      │
│   (左栏)     │      (中栏，主区域)              │    (右栏)             │
│              │                                 │                       │
│ 14:32:01     │  ┌─ system ─────────────────┐   │  告警详情              │
│ 📥 告警接收   │  │ ## 告警触发 — SOP 自动执行 │   │  alertname: ...      │
│              │  │ 告警名称: HighErrorRate   │   │  severity: critical  │
│ 14:32:02     │  │ ...                      │   │  service: order-svc  │
│ 🚀 SOP启动   │  └─────────────────────────┘   │                       │
│              │                                 │  ─── SOP 内容 ──────  │
│ 14:32:05     │  ┌─ assistant ──────────────┐   │  Step 1: 确认告警 ✅  │
│ 🔧 工具调用   │  │ 正在执行 Step 1...       │   │  Step 2: 检查指标 ✅  │
│ list_alerts  │  │ 告警状态: active (firing) │   │  Step 3: 检查副本 ⏳  │
│              │  │ ✅ Step 1 完成            │   │  Step 4: 扩容 ⬜     │
│ 14:32:08     │  └─────────────────────────┘   │  Step 5: 验证 ⬜     │
│ 📊 Step 2    │                                 │                       │
│ 查询指标     │  ┌─ assistant ──────────────┐   │  ─── 关联资源 ──────  │
│              │  │ Step 2: 查询错误率        │   │  Agent: sre-executor │
│ 14:32:15     │  │ [ToolCall: query_metrics] │   │  Skill: high-error.. │
│ ⏳ Step 3    │  │ 当前错误率: 23.5%         │   │  Conversation: ...   │
│              │  │ Step 3: 检查副本数...      │   │                      │
│              │  │ █ (token 流式输出中)       │   │                      │
│              │  └─────────────────────────┘   │                       │
│              │                                 │                       │
│              │  ┌─ 输入框（人工介入）─────────┐ │                       │
│              │  │ 可选：手动发消息给 Agent    │ │                       │
│              │  └─────────────────────────┘   │                       │
├──────────────┴─────────────────────────────────┴───────────────────────┤
│ [标记已解决]  [上报]  [手动接管]  [查看完整对话]  [导出 Timeline]       │
└────────────────────────────────────────────────────────────────────────┘
```

#### 三栏职责

| 栏位 | 内容 | 实时数据源 |
|------|------|----------|
| **左栏 — Timeline** | 事件时间线（告警接收→SOP启动→工具调用→...→解决） | SignalR `TimelineEvent` |
| **中栏 — 对话区** | Agent/Team 的完整对话流（token-by-token 流式） | SSE（AG-UI 协议，复用 `use-agent-chat` hook） |
| **右栏 — Context** | 告警详情 + SOP 步骤进度 / RCA Ledger + 关联资源链接 | SignalR `SopExecutionProgress` / `RcaLedgerUpdate` |

#### 针对三种执行模式的中栏差异

**模式 1 — SOP 执行（链路 A）**：

```
中栏 = 单 Agent 对话流（与现有 ChatPage 一致）
  - 复用 MessageBubble + ToolCallCard 组件
  - token 流式渲染
  - 工具调用展开/折叠

右栏 = SOP 步骤清单
  ✅ Step 1: 确认告警状态
  ✅ Step 2: 查询相关指标
  ⏳ Step 3: 检查 Pod 副本数     ← 当前步骤高亮 + 脉冲动画
  ⬜ Step 4: 执行扩容操作
  ⬜ Step 5: 验证恢复
```

步骤进度的判断方式：
- 后端 `IncidentDispatcherService` 解析 Agent 回复中的 "Step N" 关键字
- 或通过 SOP Skill 的结构化步骤标题匹配
- 每完成一步发 `SopExecutionProgress` 事件

**模式 2 — 根因分析 RCA（链路 B）**：

```
中栏 = Team 多 Agent 对话流（与现有 ChatPage Team 模式一致）
  - 复用 HandoffNotification（Agent 交接通知）
  - 复用 TeamProgressIndicator（多 Agent 进度条）
  - 每个 Agent 的消息用不同颜色/头像区分
  - 工具调用结果内联展示

右栏 = MagneticOne Ledger + Agent 状态
  ┌─ Orchestrator Plan ───────────┐
  │ 1. 指标分析师查 error rate  ✅ │
  │ 2. 日志分析师查 error logs  ✅ │
  │ 3. 链路分析师查慢请求        ⏳ │
  │ 4. 协调者汇总根因           ⬜ │
  └───────────────────────────────┘
  ┌─ Agent 状态 ──────────────────┐
  │ 🟢 sre-metrics-analyst  完成  │
  │ 🟢 sre-logs-analyst     完成  │
  │ 🔵 sre-traces-analyst   发言中│
  │ ⚪ sre-k8s-operator     等待  │
  │ ⚪ sre-coordinator      等待  │
  └───────────────────────────────┘
```

复用组件：`MagneticOneLedger.tsx`（现有 Ledger 展示）、`HandoffNotification.tsx`、`TeamProgressIndicator.tsx`

**模式 3 — SOP 生成（链路 C）**：

```
中栏 = 总结 Agent 对话流（单 Agent 模式）
  - 输入：已折叠的"团队对话摘要"（可展开查看原始记录）
  - 输出：结构化 SOP Markdown（实时渲染，用 react-markdown）

右栏 = 生成状态
  ⏳ 正在分析团队对话记录...
  ⏳ 正在提炼操作步骤...
  ⏳ 正在识别工具依赖...
  ──── 完成后 ────
  ✅ SOP 已生成: "high-error-rate-order-svc"
  ✅ 绑定工具: query_metrics_prometheus, list_pods_k8s
  ✅ 已创建 ResponderAgent: sre-sop-executor
  ✅ 已更新 AlertRule: HighErrorRate-OrderService
  [查看 SOP →]  [查看 Agent →]  [查看 AlertRule →]
```

### 13.5 状态流转在 UI 中的体现

```
┌─────────┐    ┌──────────────┐    ┌───────────┐    ┌──────────┐    ┌────────┐
│  Open   │───▶│Investigating │───▶│ Mitigated │───▶│ Resolved │───▶│ Closed │
│ 🔴 红色  │    │ 🟠 橙色/脉冲  │    │ 🟡 黄色    │    │ 🟢 绿色   │    │ ⚪ 灰色 │
└─────────┘    └──────────────┘    └───────────┘    └──────────┘    └────────┘
 告警刚接收     Agent/Team 正在      问题已缓解       根因已确认       事后复盘
               活跃处理中           等待确认恢复      SOP 已生成       完结归档
```

- `Investigating` 状态的卡片和详情页有**脉冲动画**（pulse animation），表示正在活跃处理
- 状态变更时 badge 有过渡动画（颜色渐变）

### 13.6 组件复用清单

| 新组件 | 复用自 | 改动说明 |
|--------|-------|---------|
| `IncidentCard` | 新建 | 列表页卡片，含实时进度摘要 |
| `IncidentTimeline` | 参考 `NodeExecutionTimeline` | 左栏时间线（事件类型 icon + 时间戳 + 摘要） |
| `IncidentChatPanel` | 复用 `MessageArea` + `MessageBubble` + `ToolCallCard` | 直接嵌入现有对话组件，传入 conversationId |
| `IncidentContextPanel` | 新建 | 右栏：告警 JSON + SOP 步骤进度 / Ledger |
| `SopStepProgress` | 新建 | SOP 步骤清单（✅⏳⬜ + 当前步骤高亮） |
| `RcaAgentStatus` | 参考 `TeamProgressIndicator` | Agent 列表 + 当前状态 |
| `RcaLedgerPanel` | 复用 `MagneticOneLedger` | 直接嵌入 |
| `SopGenerationResult` | 新建 | 链路 C 结果展示（SOP 链接 + 工具绑定 + Agent 链接） |
| `IncidentSeverityBadge` | 参考 `ExecutionStatusBadge` | P1-P4 颜色 badge |
| `IncidentStatusBadge` | 参考 `ExecutionStatusBadge` | 5 状态颜色 badge |
| `useIncidentSignalR` | 克隆 `useWorkflowSignalR` | 换事件名 + 加 `JoinIncidentList` 支持 |
| `AlertRuleForm` | 参考现有 CRUD 表单模式 | 标签匹配器编辑器 + Agent/SOP 选择器 |

### 13.7 人工介入入口

在自动化执行过程中，用户可以在 IncidentDetailPage 随时：

| 操作 | 行为 |
|------|------|
| **手动发消息** | 中栏底部输入框，向正在执行的 Agent/Team 追加消息（如"请也检查一下 Redis 连接"） |
| **手动接管** | 暂停自动执行，切换为人工对话模式（Agent 等待用户指令） |
| **上报** | 创建通知，标记为需要高级 SRE 介入（预留通知渠道） |
| **标记已解决** | 手动将 Incident 状态改为 Resolved |
| **查看完整对话** | 跳转到 ChatPage 并加载同一 ConversationId（复用现有对话页） |

---

## 14. 开放问题

| # | 问题 | 建议 |
|---|------|------|
| 1 | **无匹配规则的告警如何处理？** | 仅记录日志 + 创建一条 "Unmatched" Incident 供人工查看？还是直接忽略？ |
| 2 | **SOP 自动绑定后是否需要人工审核？** | 建议 Phase 1 先自动绑定但标记为 `NeedsReview`，人工确认后激活 |
| 3 | **总结 Agent 生成的 SOP 质量如何保证？** | 可加一个 LLM 自评环节（让另一个 Agent 验证 SOP 完整性），或接受"先粗后细"的迭代策略 |
| 4 | **Team 对话超时后如何处置？** | 建议标记为需人工介入，同时保留部分对话结论 |
| 5 | **告警恢复（resolved）通知如何处理？** | Alertmanager 也会推送 resolved 状态，可用来自动关闭 Incident |
| 6 | **SOP 版本管理？** | 同一个 SkillRegistration 可通过 `UpdateContent()` 更新，但无历史版本。是否需要版本表？ |
| 7 | **ResponderAgent 是否每条 AlertRule 独享？** | 建议共享——一个 "SOP Executor" Agent 可绑定多个 SOP Skill，运行时根据 Incident 选择对应 Skill |
