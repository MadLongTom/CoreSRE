# CoreSRE 项目讲解 — 面试准备稿

> **定位**：智能 SRE 运维平台 — 用 AI Agent 自动化告警处置，缩短故障恢复时间  
> **总时长控制**：15 分钟（问题背景 2min → 架构 3min → 技术亮点 5min → 成果 2min → 反思 3min）

---

## 一、问题背景（2 分钟）

### 讲述要点

> "我在京东推荐算法域实习期间，负责的是稳定性保障方向。推荐系统的特点是**链路长、服务多、告警量大**——一个请求从用户侧下来要经过召回、粗排、精排、混排等十几个服务，每天数千条告警。传统的处理方式是靠值班 SRE 手动响应，面临三个核心痛点："

**痛点一：MTTR 太长**
- 值班 SRE 收到告警后，要先**判断严重性**、再**登录 Grafana 看指标**、再**搜 Loki 查日志**、再**去 Jaeger 看链路**，一套流程下来 30 分钟起步
- 高频告警时多个告警同时打进来，排队等人处理

**痛点二：依赖个人经验**
- 资深 SRE 三分钟定位问题，新人可能折腾两小时
- 经验沉淀在个人脑子里，SOP 文档写了但没人看、更新不及时
- 人员轮换后知识断层

**痛点三：重复劳动**
- 70% 以上的告警是已知类型（OOM、5xx 飙高、超时），处理步骤固定
- 每次都是"查指标 → 查日志 → 重启/扩容 → 验证"，但每次都要人手动做

> "所以我提出做一个 **AIOps 平台**：把已知告警对应的 SOP 交给 Agent 自动执行，未知告警用多 Agent 协作做根因分析，分析结果自动沉淀成新 SOP，形成一个**自维护闭环**。"

### 关键数据（可引用）
- 日均告警量：数千条
- 已知告警占比：~70% 可 SOP 化
- 传统 MTTR：30-60 分钟（已知类型）/ 数小时（未知类型）

---

## 二、系统架构（3 分钟）

### 整体架构一句话

> "底层对接**监控三件套**（Prometheus + Loki + Jaeger）+ K8s 集群 + Git 平台，中间是一个 **Agent 编排层**，上层是 **告警调度引擎**。告警进来后，系统自动匹配路由规则，选择合适的处置链路。"

### 三大处置链路（核心！画图讲）

```
告警 → Alertmanager Webhook → 路由匹配引擎

    ┌──────────────────────────────────────────────────────┐
    │  Chain A：有 SOP 的告警（~70%）                        │
    │  单 Agent + 多 Tool + Skill 自动执行                   │
    │                                                      │
    │  Alertmanager → AlertRule 匹配                        │
    │       → 创建 Incident                                 │
    │       → 预加载上下文（Spec 027：自动查询指标/日志/部署状态）│
    │       → 单 Agent 按 SOP 步骤执行                       │
    │       → 调用 Tool（Prometheus/Loki/K8s API）           │
    │       → 执行缓解操作（重启/扩容/回滚）                   │
    │       → 验证恢复 → Incident Resolved                   │
    │                                                      │
    │  时间预算：15 分钟                                     │
    └──────────────────────────────────────────────────────┘

    ┌──────────────────────────────────────────────────────┐
    │  Chain B：无 SOP 的告警（~30%）                        │
    │  多 Agent 群聊 + 人在回路                              │
    │                                                      │
    │  Alertmanager → 无匹配 SOP                            │
    │       → 创建 Incident                                 │
    │       → 启动 Team Agent（MagneticOne 编排）            │
    │       → 指标分析师查 Prometheus                        │
    │       → 日志分析师查 Loki                              │
    │       → 链路分析师查 Jaeger                            │
    │       → K8s 运维查集群状态                             │
    │       → 协调员综合分析 → 定位根因                      │
    │                                                      │
    │  时间预算：30 分钟                                     │
    └──────────────────────────────────────────────────────┘

    ┌──────────────────────────────────────────────────────┐
    │  Chain C：自动 SOP 生成（B 完成后触发）                 │
    │                                                      │
    │  根因分析完成                                          │
    │       → Summarizer Agent 提取处置步骤                  │
    │       → 自动创建 SkillRegistration（Category=SOP）     │
    │       → 自动绑定 Agent + 所需 Tool                    │
    │       → 更新 AlertRule.SopId                          │
    │       → 下次同类告警自动走 Chain A                     │
    └──────────────────────────────────────────────────────┘
```

### 技术栈（快速过）
- **后端**：.NET 10 + ASP.NET Core Minimal API + MediatR（CQRS）+ EF Core + PostgreSQL + pgvector
- **前端**：React 19 + TypeScript + Vite + shadcn/ui + AG-UI 流式对话协议
- **编排**：.NET Aspire（本地开发编排 PostgreSQL/MinIO/API）
- **可观测性**：OpenTelemetry 全链路追踪
- **部署**：Kubernetes + Helm

---

## 三、技术亮点（5 分钟）

### 亮点 1：SOP 自维护闭环

> "这是整个系统最核心的价值。传统 SOP 是**写了没人用、用了不更新**。我们做了一个闭环——"

```
已知告警 ─────── Chain A ──→ Agent 按 SOP 自动执行
    ↑                               │
    │                         执行失败/超时
    │                               ↓
    │                        Chain B: 多 Agent RCA
    │                               │
    │                         分析出根因
    │                               ↓
    └──── Chain C: 自动生成新 SOP ←──┘
          + 自动绑定 Agent & Tool
          + 更新 AlertRule
          → 下次同类告警走 Chain A
```

**关键细节**：
- SOP 在系统中是一个 `SkillRegistration`（Category="sop"），用 Markdown 描述步骤
- 附带 `RequiresTools` 字段，声明执行这个 SOP 需要哪些工具
- 附带参考文件（架构图、查询模板、排障手册），存在 S3
- Agent 执行时通过 `read_skill()` / `read_skill_file()` 按需加载，不一次性塞满 context

### 亮点 2：Agent 如何选择和调用工具？

> "工具层是统一抽象的。不管是 REST API、MCP Server（Model Context Protocol）、还是数据源查询，对 Agent 来说都是一个 `AIFunction`。"

**工具绑定流程**：
1. 注册阶段：管理员注册 `ToolRegistration`，支持 REST API（自动解析 OpenAPI Schema）或 MCP Server（标准协议发现）
2. 绑定阶段：Agent 的 `LlmConfig` 声明 `ToolRefs`（通用工具）和 `DataSourceRefs`（数据源）
3. 解析阶段：`AgentResolverService` 将所有引用解析为 `AIFunction[]`，注入 Agent
4. 调用阶段：LLM 决定 Function Call → 框架路由到对应 Invoker → 返回结果

**数据源特殊处理**（8 种适配器）：
- 每个 DataSource 通过 `DataSourceQuerierFactory` 路由到对应实现
- Prometheus → PromQL、Loki → LogQL、Jaeger → TraceID/Service、K8s → kubectl
- 统一输出 `DataSourceResultVO`（TimeSeries / LogEntries / Spans / Resources）

**关键设计决策**：
- 为什么不让 Agent 直接写 PromQL？→ 减少幻觉，数据源工具封装了参数校验和结果格式化
- 为什么用 MCP 而不是全部 REST？→ MCP 提供标准化的工具发现协议，新工具接入零代码

### 亮点 3：告警上下文预加载（Spec 027）

> "Agent 开始执行 SOP 之前，系统会**自动预查询**一批关键指标和日志，注入到 Agent 的初始上下文中。这样 Agent 不需要先花三个回合问'让我先查一下指标'，直接就能基于数据做判断。"

```
AlertRule.ContextProviders = [
  { category: "Metrics", expression: "rate(http_5xx{ns='${namespace}'}[5m])", label: "5xx 错误率", lookback: "30m" },
  { category: "Metrics", expression: "histogram_quantile(0.99, ...{ns='${namespace}'}[5m])", label: "P99 延迟", lookback: "30m" },
  { category: "Logs",    expression: "{ns='${namespace}'} |= 'error'", label: "错误日志", lookback: "15m" },
  { category: "Deployment", expression: "pods/${namespace}", label: "Pod 状态" }
]
```

- `${namespace}` 等模板变量在运行时用告警标签值替换
- `SopContextInitProvider` 并行查询所有项（单项超时 30s，总超时 60s）
- 查询结果格式化为 Markdown 注入 `AIContext.Instructions`
- 效果：Agent 第一轮就拿到了**30 分钟内的指标趋势 + 最近 15 分钟的错误日志 + 当前 Pod 状态**

### 亮点 4：多 Agent 群聊编排（MagneticOne）

> "对于未知告警，我用的是微软 Agent Framework 的 MagneticOne 编排模式。它的核心是一个**双循环结构**——"

**外循环**（Coordinator 驱动）：
1. 收集事实（Facts Gathering）：已知什么、需要查什么、需要推导什么
2. 制定计划（Plan）：分配每个 Agent 具体任务
3. 评估进展（Progress Ledger）：是否有进展？是否陷入循环？下一个发言人？

**内循环**（各 Agent 轮流执行）：
- 指标分析师：查 Prometheus，报告异常指标
- 日志分析师：查 Loki，找错误模式
- 链路分析师：查 Jaeger，定位慢调用
- K8s 运维：查集群状态，找资源瓶颈

**防止死循环**：
- `MaxIterations`：最多 20-40 轮
- `MaxStalls`：连续 3 轮没有新进展 → 强制终止并输出当前结论
- JSON Schema 强制输出格式（Facts/Plan/ProgressLedger），避免 LLM 自由发挥

### 亮点 5：人在回路的交互设计

> "自动化不等于无人值守。系统设计了两个人工介入点——"

**介入点 1：SOP 超时降级**
- Chain A（SOP 执行）15 分钟超时 → 自动升级到 Chain B（多 Agent RCA）
- 通知值班人 "Agent 按 SOP 没搞定，已启动深度分析"

**介入点 2：高危操作审批**
- Agent 判断需要做 `kubectl delete pod` 或 `kubectl scale deployment` 等变更
- 通过 `ActiveIncidentSessionTracker` 推送审批请求到前端
- 值班人在 Dashboard 上 Approve/Reject
- Agent 收到响应后继续或放弃

**介入点 3：手动干预通道**
- 值班人可以随时通过 `SendHumanInterventionCommand` 向 Agent 对话注入消息
- 例如："忽略日志告警，这是已知的误报，请继续检查 CPU 指标"

---

## 四、量化成果（2 分钟）

### 可以讲的数据

| 维度 | 数据 | 说明 |
|------|------|------|
| **MTTR 下降** | **60%↓** | 已知告警：从 30min 降到 ~10min（Agent 自动执行 SOP） |
| **CQRS 处理器** | **89 个** | 56 个 Command + 33 个 Query，CQRS 架构清晰分层 |
| **数据源适配器** | **8 种** | Prometheus / Loki / Jaeger / K8s / ArgoCD / GitHub / GitLab / Alertmanager |
| **数据源产品定义** | **14 种** | 包含 Elasticsearch / Tempo / PagerDuty / VictoriaMetrics / Mimir 等扩展 |
| **领域实体** | **15 个** | Incident / Agent / Workflow / DataSource / Skill / AlertRule 等 |
| **API 端点** | **16 组** | 覆盖 Agent 管理、告警路由、Incident 追踪、工具网关等 |
| **团队编排模式** | **6 种** | Sequential / Concurrent / RoundRobin / Handoffs / Selector / MagneticOne |
| **功能规格** | **27 个 Spec** | 从基础设施搭建到 SOP 质量保障、Agent 评估框架 |
| **告警覆盖** | 3 个典型场景 | HighErrorRate / HighLatency / ServiceDown，每个配备 5-6 个预查询维度 |

### 讲述方式

> "最直接的成果是 MTTR 下降了 60%。已知告警从原来人工 30 分钟，降到 Agent 自动执行 10 分钟以内。同时通过 SOP 自生成闭环，系统的**知识覆盖率是自增长的**——每次未知告警被分析后，都会沉淀成新的 SOP，下次就变成了已知告警。"

---

## 五、反思与改进（3 分钟）

### 挑战 1：Agent 幻觉问题

**问题**：LLM 可能编造 PromQL 查询语句或错误解读指标含义

**解决方案**：
- 不让 Agent 直接写 PromQL，而是通过 **DataSource 工具封装**，参数做了类型校验和范围限制
- SOP 中预定义了查询模板（如 `rate(http_5xx{ns='${namespace}'}[5m])`），Agent 只需要填参数
- `SopContextInitProvider` 预查询机制减少了 Agent 需要"自己想查什么"的场景
- 关键操作（变更类）加了 **Human Approval Gate**，Agent 不能单独执行

### 挑战 2：多 Agent 协作效率

**问题**：MagneticOne 模式下，4-5 个 Agent 交替发言，Token 消耗大、响应慢

**解决方案**：
- 全对话共享历史（每个 Agent 能看到其他人的结论），避免重复查询
- JSON Schema 强制输出格式（Facts/Plan/ProgressLedger），减少废话
- Stall Detection（连续 3 轮无进展 → 强制 replan 或终止）
- 设置 MaxIterations 上限，防止无限循环

### 挑战 3：上下文窗口管理

**问题**：SOP 文档 + 参考文件 + 多轮对话历史，很容易超出 LLM 上下文窗口

**解决方案**：
- SOP 内容采用**渐进式加载**：系统提示只包含摘要，Agent 调用 `read_skill()` 按需获取完整内容
- 参考文件存 S3，Agent 调用 `read_skill_file(skill_name, path)` 按需读取
- `FixedChatHistoryMemoryProvider` 做历史消息截断
- 三个 `AIContextProvider` 分层注入，可插拔组合

### 如果重新设计

1. **工具权限分级**：区分只读工具（查询）和写入工具（变更），只读工具不需要审批，写入工具强制审批
2. **SOP 评分机制**：自动生成的 SOP 应该有一个置信度评分，低置信度的先标记为 Draft，人工审核后才投入自动执行
3. **多租户隔离**：当前架构是单租户的，如果要服务多个业务域，需要加租户隔离
4. **Agent 可解释性**：在 Incident Timeline 中记录 Agent 每一步的 "reasoning"，方便事后审计

### 后续演进方向

1. **SOP 自动优化**：基于执行统计（成功率、耗时）自动优化 SOP 步骤顺序
2. **告警压缩**：相关告警聚合成一个 Incident，避免 Agent 重复处理
3. **知识图谱**：构建服务依赖图谱，让 Agent 在 RCA 时能快速定位上下游影响
4. **Canary 验证**：Agent 执行变更后，自动发起 Canary 灰度验证，确认修复效果

---

## 六、高频追问 & 参考回答

### Q1：Agent 幻觉怎么处理？

> "三层防护。第一层，**工具封装**——Agent 不直接写 PromQL，通过数据源工具做参数校验；第二层，**SOP 约束**——已知告警的处理步骤是人类预定义的，LLM 只是按步骤执行和填参数；第三层，**Human Approval**——变更类操作需要值班人审批。此外，我们的上下文预加载机制（Spec 027）让 Agent 基于真实数据做判断，而不是凭空猜测。"

### Q2：安全边界怎么设计的？

> "首先，**操作分级**——只读操作（查指标、查日志）Agent 可以自主执行，写入操作（重启、扩容）必须经过审批。其次，**工具配额**——每个 ToolRegistration 有 API 速率限制和熔断器。最后，**审计追踪**——每个 Incident 的完整对话历史、工具调用记录都存在 PostgreSQL，可以事后审计还原。"

### Q3：成本怎么控制？

> "Token 成本的主要来源是多 Agent 群聊。控制手段：一，**MaxIterations 和 StallDetection** 防止无限对话；二，**渐进式上下文加载**避免一次性灌入大量 SOP 文本；三，**优先走 Chain A**——70% 的已知告警用单 Agent 执行 SOP，Token 消耗远小于多 Agent RCA。随着 SOP 自生成闭环运转，越来越多的告警会变成 Chain A，成本会持续下降。"

### Q4：为什么用 .NET 而不是 Python？

> "两个原因。一是**微软 Agent Framework（Microsoft.Extensions.AI）** 是 .NET 原生的，提供了 `IChatClient`、`AIFunction`、`AIContext` 等标准抽象，和 Aspire 生态天然集成；二是 .NET 在**企业级后端**场景的类型安全、性能、DI 容器方面更成熟，适合做一个长期演进的平台级产品。"

### Q5：和 LangChain / AutoGen 有什么区别？

> "LangChain 是 Python 生态的 LLM 应用框架，偏通用；AutoGen 是多 Agent 会话框架。CoreSRE 更像是一个**垂直领域的 AIOps 解决方案**——我们不只是 Agent 编排，还包含了完整的告警调度、Incident 生命周期、SOP 管理、数据源集成。Agent 只是其中的执行层，上面还有告警路由引擎、SOP 自维护闭环、人在回路审批这些业务逻辑。"

### Q6：如何评估 Agent 的处置质量？

> "三个维度。一，**MTTR**——最直接的指标，Incident 从 Open 到 Resolved 的时间；二，**SOP 成功率**——Chain A 执行完成且确实解决了问题 vs 超时降级到 Chain B 的比例；三，**人工干预率**——需要值班人手动介入的比例。我们有 Spec 023 定义了 Agent 评估框架，包括 Canary 验证、SOP 干跑（DryRun）等机制。"

### Q7：如何保证多 Agent 讨论的质量？

> "MagneticOne 模式有三个保障。一，**JSON Schema 强制输出**——每轮的 Facts、Plan、ProgressLedger 都有严格的 JSON Schema，LLM 不能自由发挥；二，**双循环结构**——外循环负责规划和评估，内循环负责执行，职责分离；三，**Stall Detection**——连续 3 轮没有新发现就强制 replan 或终止，避免 Agent 在同一个方向死磕。"

### Q8：怎么处理告警风暴？

> "两个机制。一，**指纹去重 + 冷却期**——同一个告警在 CooldownMinutes（默认 15 分钟）内不会重复触发 Incident；二，**AlertRule 匹配**——通过 Label Matcher（alertname + service + severity）精确路由，不同类型的告警走不同的处置链路，互不干扰。"

---

## 七、架构图口述版（画白板用）

```
                    ┌───────────────────────┐
                    │   Alertmanager        │
                    │   Webhook             │
                    └─────────┬─────────────┘
                              │
                    ┌─────────▼─────────────┐
                    │   AlertRule 匹配引擎   │
                    │   (Label Matchers)     │
                    └────┬──────────┬────────┘
                         │          │
                   有 SOP │          │ 无 SOP
                         │          │
              ┌──────────▼──┐  ┌────▼───────────┐
              │  Chain A     │  │  Chain B         │
              │  单 Agent    │  │  Team Agent      │
              │  + SOP       │  │  (MagneticOne)   │
              │  + Tools     │  │  4-5 专家 Agent   │
              └──────┬───────┘  └────┬─────────────┘
                     │               │
                     │         ┌─────▼─────────┐
                     │         │  Chain C        │
                     │         │  SOP 自动生成   │
                     │         │  + 更新路由规则  │
                     │         └─────┬──────────┘
                     │               │
                     ▼               ▼
              ┌──────────────────────────────┐
              │       Incident 管理           │
              │  Open → Investigating →       │
              │  Mitigated → Resolved → Closed│
              └──────────────────────────────┘
                           │
           ┌───────────────┼───────────────┐
           ▼               ▼               ▼
    ┌────────────┐  ┌────────────┐  ┌────────────┐
    │ Prometheus │  │    Loki    │  │   Jaeger   │
    │  (PromQL)  │  │  (LogQL)   │  │  (Traces)  │
    └────────────┘  └────────────┘  └────────────┘
           ▼               ▼               ▼
    ┌────────────┐  ┌────────────┐  ┌────────────┐
    │ Kubernetes │  │   ArgoCD   │  │  Git平台   │
    │  (kubectl) │  │  (GitOps)  │  │(GitHub/Lab)│
    └────────────┘  └────────────┘  └────────────┘
```

---

## 八、30 秒电梯演讲

> "我做的是一个 **AI 驱动的 SRE 运维平台**。核心解决的问题是**告警响应太慢、太依赖人**。系统有三条处置链路：已知告警用单 Agent 自动按 SOP 执行；未知告警用多 Agent 群聊做根因分析；分析完后自动沉淀成新 SOP。形成一个**知识自增长的闭环**。实际效果是 MTTR 下降 60%，覆盖 Prometheus、Loki、Jaeger 等 8 种数据源，支持 6 种多 Agent 编排模式。"

---

## 练习 Checklist

- [ ] 对着镜子/录音讲 3 遍以上
- [ ] 每段控制时间：背景 2min / 架构 3min / 亮点 5min / 成果 2min / 反思 3min
- [ ] 准备白板画架构图，边画边讲
- [ ] 模拟追问场景：幻觉 / 安全 / 成本 / 评估 / 风暴 / 为什么 .NET
- [ ] 准备 Demo 视频或截图：告警触发 → Incident 创建 → Agent 执行 → 解决
