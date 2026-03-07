# Feature Specification: Pluggable AIContext & SOP Context Initialization

**Feature Branch**: `027-pluggable-aicontext`  
**Created**: 2026-03-07  
**Status**: Draft  
**Input**: 用户以及SOP对AIContext的定制化能力太弱，RCA和SOP执行初期LLM需要大量时间获取信息。设计可插拔AIContext系统，让SOP能提供初始化AIContext

## Problem Statement

当前 Agent 上下文注入存在严重瓶颈：

1. **上下文来源单一**：`SopMessageTemplates.BuildSopExecutionMessage()` 仅注入告警名称和标签字典，Agent 在对话开始后需要自行调用 6+ 个工具去查询指标、日志、K8s 状态、变更历史，浪费 2-5 轮对话（30-120 秒）
2. **无 SOP 级上下文声明**：SOP 没有能力声明 "执行我之前需要先查询哪些数据"，每次执行都是从零开始
3. **无 AlertRule 级上下文配置**：不同告警类型对初始化数据的需求不同（CPU 告警需要 metrics + pod status，部署失败需要 git history + k8s events），无法按规则定制
4. **Summarizer Agent 无法反馈上下文模板**：Chain C 生成 SOP 时（`SopGenerationPromptBuilder`），无法从 RCA 过程中提炼出 "下次遇到这个告警该预先查什么"
5. **上下文不可扩展**：如果需要从企业内部 CMDB、变更管理系统等非标准数据源获取上下文，没有扩展点

## Framework Analysis: AIContextProvider Lifecycle

### Microsoft.Agents.AI 的 AIContextProvider 抽象

根据 `.reference/codes/agent-framework` 源码分析，`AIContextProvider` 是框架官方的上下文扩展点：

```
AIContextProvider (abstract)
  ├── InvokingAsync(InvokingContext) → AIContext     // 对话开始前，注入 Instructions + Messages + Tools
  ├── InvokedAsync(InvokedContext) → void             // 对话结束后，处理结果（学习/存储）
  └── ProvideAIContextAsync(InvokingContext) → AIContext  // 子类重写此方法提供上下文
```

**AIContext** 容器包含三个维度：
- `Instructions` (string): 额外系统指令，与 Agent 原有 Instructions 拼接
- `Messages` (IEnumerable<ChatMessage>): 额外消息，注入对话历史（如 RAG 结果、记忆）
- `Tools` (IEnumerable<AITool>): 额外工具，仅在本次调用有效

**`ChatClientAgent.PrepareSessionAndMessagesAsync` 调用流程**：
```
1. ChatHistoryProvider.InvokingAsync()  → 加载会话历史
2. foreach (AIContextProvider in AIContextProviders):
     aiContext = provider.InvokingAsync(agent, session, aiContext)  → 链式叠加上下文
3. 最终 aiContext.Messages → inputMessagesForChatClient
4. 最终 aiContext.Tools → chatOptions.Tools
5. 最终 aiContext.Instructions → chatOptions.Instructions
```

**关键特性**：
- 链式组合：多个 Provider 按顺序执行，每个接收前一个的输出
- 状态隔离：Provider 通过 `AgentSession.StateBag[StateKey]` 存状态，不在实例中保存
- 消息归属：注入的 Messages 被打上 `AgentRequestMessageSourceType.AIContextProvider` 标记

### CoreSRE 现有 AIContextProvider 实例

| Provider | 注入内容 | 配置位置 |
|----------|---------|---------|
| `S3AgentSkillsProvider` | Instructions（Skill 列表广告）+ Tools（`load_skill`, `read_skill_resource`）| `AgentResolverService` |
| `FixedChatHistoryMemoryProvider` | Messages（pgvector 语义搜索的相关记忆）| `AgentResolverService` |

### 设计决策

本 Spec 的核心改造是新增 `SopContextInitProvider : AIContextProvider`，利用框架的链式上下文机制，在 Agent 每次 `RunAsync/RunStreamingAsync` 调用前：

1. 从 `AgentSession.StateBag` 中读取 dispatch 阶段设置的 `ContextInitItemVO[]` 和模板变量
2. 并行执行数据源预查询
3. 将结果作为 `AIContext.Instructions` 注入（结构化 Markdown 摘要）

**为什么不修改 `SopMessageTemplates` 的消息体？**
- 消息体注入 = 占用 user message token，且每轮对话都重复携带
- `AIContext.Instructions` 注入 = 系统指令级别，token 效率更高，且 Provider 可根据 Session 状态决定后续调用是否继续注入
- 框架原生支持，符合 AIContextProvider 的设计意图

## Clarifications

- Q: SopContextInitProvider 的构造时机？ → A: 在 `IncidentDispatcherService.DispatchSopExecutionAsync` 中，Resolve Agent 之后、RunAgent 之前。通过 `AgentSession.StateBag` 传递 context init 参数。Provider 本身在 AgentResolverService 中创建。
- Q: SOP 中如何声明需要的初始化上下文？ → A: 在 SOP Markdown 中新增 `## 初始化上下文` 段落，声明需要预查询的数据源类别、查询表达式、筛选条件
- Q: AlertRule 级配置与 SOP 级声明优先级？ → A: 两者取并集。AlertRule.ContextProviders 是全局默认，SOP 的初始化上下文是 SOP 特化补充
- Q: 预查询是否有超时限制？ → A: 单个查询最大 30 秒超时，总并行超时 60 秒。超时的条目返回 `"[timeout: {category} query exceeded 30s]"` 不阻塞
- Q: Summarizer Agent 如何知道该添加什么上下文？ → A: `SopGenerationPromptBuilder` 的 Prompt 中新增指令，要求总结 Agent 分析 RCA 过程中调用了哪些工具/查询了什么数据，将其提炼为 `## 初始化上下文` 段落
- Q: 为什么用 Instructions 而不是 Messages？ → A: Instructions 是系统级别，不占 user message token；Messages 会持久化到会话历史中反复携带。对于一次性注入的预查数据，Instructions 更高效。

## User Scenarios & Testing

### User Story 1 — SOP Context Initialization Declaration (Priority: P0)

作为 SOP 编写者（人工或 Summarizer Agent），我希望在 SOP 中声明执行前需要预先查询的数据，使得 Agent 首轮 LLM 调用时已具备完整诊断上下文。

**Acceptance Scenarios**:

1. **Given** 一个 SOP 的 Markdown 包含如下段落：
   ```markdown
   ## 初始化上下文
   - metrics: rate(http_requests_total{namespace="${namespace}"}[5m]) | 查看请求量变化趋势
   - metrics: histogram_quantile(0.99, rate(http_request_duration_seconds_bucket{namespace="${namespace}"}[5m])) | P99 延迟
   - logs: {namespace="${namespace}"} |~ "(?i)error|exception|panic" | 近1小时错误日志
   - k8s: pods/${namespace} | 查看 Pod 状态
   - git: commits/${repo}?since=2h | 最近 2 小时代码变更
   ```
   **When** SOP 被解析（`SopParser.Parse`），**Then** `SopParseResult` 新增 `ContextInitItems` 字段，包含 5 个 `ContextInitItemVO` 条目。

2. **Given** SOP 中 `${namespace}` 占位符，**When** `SopContextInitProvider.ProvideAIContextAsync` 执行时，**Then** 使用 `AgentSession.StateBag["alertLabels"]` 中 `namespace` 的实际值（如 `demo-app`）替换占位符后再查询。

3. **Given** SOP 中没有 `## 初始化上下文` 段落，**When** Provider 执行，**Then** 跳过预查（返回空 `AIContext`），不影响后续执行。

---

### User Story 2 — AlertRule Context Providers Configuration (Priority: P0)

作为 SRE 管理员，我希望在 AlertRule 上配置该类型告警的默认上下文查询，使得所有匹配的告警处置都能自动获取相关数据。

**Acceptance Scenarios**:

1. **Given** AlertRule "HighErrorRate-OrderService" 配置了 ContextProviders：
   ```json
   [
     { "category": "metrics", "expression": "rate(http_requests_total{namespace=\"${namespace}\"}[5m])", "label": "请求量" },
     { "category": "logs", "expression": "{namespace=\"${namespace}\"} |~ \"error\"", "label": "错误日志" },
     { "category": "k8s", "expression": "pods/${namespace}", "label": "Pod 状态" }
   ]
   ```
   **When** 该规则匹配告警并创建 Incident，**Then** `DispatchSopExecutionAsync` 将这些条目存入 `AgentSession.StateBag["contextInitItems"]`，`SopContextInitProvider` 在 `ProvideAIContextAsync` 中读取并并行预查。

2. **Given** AlertRule 和 SOP 都配置了上下文查询，**When** Provider 执行初始化，**Then** 两者取并集（去重相同 Category+Expression 的条目）。

3. **Given** 某个上下文查询超时（>30s），**When** Provider 继续，**Then** 该条目的 Instructions 中附加 `[timeout]` 说明，其他查询结果正常注入。

---

### User Story 3 — Summarizer Agent Auto-Generates Context Section (Priority: P1)

作为系统，我希望 Chain C 的 Summarizer Agent 在生成 SOP 时自动提炼出 "初始化上下文" 段落。

**Acceptance Scenarios**:

1. **Given** RCA 团队在根因分析过程中调用了 `query_metrics_prometheus_main`、`query_logs_loki_main`、`list_resources_k8s_cluster`，**When** Summarizer Agent 生成 SOP，**Then** SOP 中包含 `## 初始化上下文` 段落，列出这些查询（将硬编码值替换为 `${label}` 占位符）。

2. **Given** `SopGenerationPromptBuilder.Build()` 的 Prompt 中，**When** 系统调用 Summarizer Agent，**Then** Prompt 包含附加指令要求生成 `## 初始化上下文` 段落。

3. **Given** Summarizer Agent 未生成 `## 初始化上下文` 段落，**When** SOP 通过校验，**Then** `ValidationWarning` 中记录 "SOP 缺少初始化上下文段落，建议补充以加速未来执行"。

---

### User Story 4 — AIContextProvider Lifecycle Integration (Priority: P0)

作为系统，`SopContextInitProvider` 必须正确实现框架的 `AIContextProvider` 生命周期。

**Acceptance Scenarios**:

1. **Given** Agent 已配置 `SopContextInitProvider`（在 `AIContextProviders` 列表中），**When** `ChatClientAgent.RunStreamingAsync` 被调用，**Then** 框架自动在 `PrepareSessionAndMessagesAsync` 中调用 `SopContextInitProvider.InvokingAsync` 注入预查上下文到 `AIContext.Instructions`。

2. **Given** `AgentSession.StateBag` 中设置了 `contextInitItems` 和 `alertLabels`，**When** Provider 的 `ProvideAIContextAsync` 被调用，**Then** 并行执行所有数据源查询，将结果格式化为 Instructions：
   ```markdown
   ## 📊 预加载诊断上下文 (自动查询)

   ### 请求量 (metrics)
   rate(http_requests_total{namespace="demo-app"}[5m])
   结果: [{"metric": {"namespace": "demo-app"}, "values": [[1741340100, "152.3"], ...]}]

   ### 错误日志 (logs)
   {namespace="demo-app"} |~ "error"
   结果: [{"timestamp": "2026-03-07T10:15:33Z", "message": "ERROR OrderService: Connection refused..."}]

   ### Pod 状态 (k8s)
   pods/demo-app
   结果: [{"name": "order-service-6bb647cc8-2mxvj", "status": "CrashLoopBackOff", "restarts": 5}]

   以上数据已预先查询。请在分析和执行过程中优先参考这些数据，如需获取更多信息可调用相应的数据源工具。
   ```

3. **Given** `AgentSession.StateBag` 中没有 `contextInitItems`（如普通用户聊天场景），**When** Provider 执行，**Then** 返回空 `AIContext()`（无操作），不影响正常对话。

4. **Given** Provider 链: [S3AgentSkillsProvider, SopContextInitProvider, FixedChatHistoryMemoryProvider]，**When** Agent 执行，**Then** 三个 Provider 按顺序链式执行，Instructions 逐步拼接，Tools 逐步合并。

---

### User Story 5 — Custom AIContext Provider Extension Point (Priority: P2)

作为平台开发者，我希望能注册自定义 `AIContextProvider`（如从 CMDB、变更管理系统、工单系统获取数据），使上下文来源可扩展。

**Acceptance Scenarios**:

1. **Given** 开发者实现了 `CmdbContextProvider : AIContextProvider`，**When** 在 DI 中注册，**Then** 可在 `AgentResolverService` 中加入 Agent 的 `AIContextProviders` 列表。
2. **Given** `CmdbContextProvider` 从 `AgentSession.StateBag["alertLabels"]` 中读取服务名，**When** Agent 执行，**Then** 自动查询 CMDB 获取拓扑、变更、负责人信息并注入 Instructions。

## Architecture / Design

### 1. SopContextInitProvider : AIContextProvider

核心新增类，继承 `Microsoft.Agents.AI.AIContextProvider`：

```csharp
public sealed class SopContextInitProvider : AIContextProvider
{
    private readonly IDataSourceQuerierFactory _querierFactory;
    private readonly IDataSourceRegistrationRepository _dsRepo;
    
    // StateBag keys
    public const string ContextInitItemsKey = "contextInitItems";   // List<ContextInitItemVO>
    public const string AlertLabelsKey = "alertLabels";             // Dictionary<string, string>
    
    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        // 1. 从 Session.StateBag 读取 context init 参数
        var items = context.Session?.StateBag.TryGetValue<List<ContextInitItemVO>>(ContextInitItemsKey);
        if (items is null or { Count: 0 })
            return new AIContext();  // 无操作
        
        var labels = context.Session?.StateBag.TryGetValue<Dictionary<string, string>>(AlertLabelsKey)
            ?? new();
        
        // 2. 模板变量替换
        var resolvedItems = items.Select(i => ResolveTemplateVariables(i, labels)).ToList();
        
        // 3. 去重
        resolvedItems = resolvedItems
            .DistinctBy(i => $"{i.Category}:{i.Expression}")
            .ToList();
        
        // 4. 并行执行查询 (单项 30s, 总 60s)
        var results = await ExecuteQueriesAsync(resolvedItems, cancellationToken);
        
        // 5. 格式化为 Instructions markdown
        var instructions = FormatAsInstructions(results);
        
        // 6. 标记已执行（防止多轮重复查询）
        context.Session?.StateBag.Remove(ContextInitItemsKey);
        
        return new AIContext { Instructions = instructions };
    }
}
```

**关键设计点**：
- 数据通过 `AgentSession.StateBag` 传入，不耦合 dispatch 层
- 执行完成后从 StateBag 移除 key，确保多轮对话不重复预查
- 仅注入 `Instructions`，不注入 `Messages`（避免持久化到会话历史）

### 2. IncidentDispatcherService 集成

在 dispatch 中将上下文参数存入 Session：

```csharp
// DispatchSopExecutionAsync — 在创建/加载 Session 后
session.StateBag[SopContextInitProvider.AlertLabelsKey] = alertLabels;

// 合并 AlertRule.ContextProviders + SOP.ContextInitItems
var contextItems = MergeContextItems(alertRule.ContextProviders, sop.GetContextInitItems());
if (contextItems.Count > 0)
    session.StateBag[SopContextInitProvider.ContextInitItemsKey] = contextItems;
```

**流程变化**：
```
Before: Resolve Agent → Build message(labels) → Run Agent
After:  Resolve Agent → Load AlertRule.ContextProviders → Load SOP.ContextInitItems 
        → Merge & Store to Session.StateBag → Build message(labels) → Run Agent
        → (framework calls SopContextInitProvider.InvokingAsync → queries & injects)
```

注意：预查发生在框架内部（`PrepareSessionAndMessagesAsync`），不在 dispatch 流程的显式代码中。

### 3. AgentResolverService 注册 Provider

修改 `ResolveChatClientAgent`，在 AIContextProviders 列表中新增 `SopContextInitProvider`：

```csharp
// ── AIContextProviders 汇总 ── Skills + ContextInit + Memory ──
var aiContextProviders = new List<AIContextProvider>();
if (skillsProvider is not null)
    aiContextProviders.Add(skillsProvider);

// SopContextInitProvider — 始终注入，通过 StateBag 有无 key 决定是否执行
aiContextProviders.Add(new SopContextInitProvider(
    _dataSourceQuerierFactory, _dataSourceRepo, _loggerFactory));

if (memProvider is not null)
    aiContextProviders.Add(memProvider);

options.AIContextProviders = aiContextProviders;
```

### 4. ContextInitItemVO & SOP Parsing

**值对象**（已在 Domain 层创建）：

```csharp
public sealed record ContextInitItemVO
{
    public string Category { get; init; } = string.Empty;  // metrics, logs, k8s, git, alerting, tracing
    public string Expression { get; init; } = string.Empty; // PromQL, LogQL, resource selector
    public string? Label { get; init; }                      // 段落标题
    public string? Lookback { get; init; }                   // "1h", "30m"
    public Dictionary<string, string>? ExtraParams { get; init; }
}
```

**SOP Markdown 格式**：

```markdown
## 初始化上下文
- metrics: rate(http_requests_total{namespace="${namespace}"}[5m]) | 请求量趋势
- logs: {namespace="${namespace}"} |~ "(?i)error" | 近1小时错误日志
- k8s: pods/${namespace} | Pod 状态
```

**解析规则**（`SopParserService` 扩展）：
- 匹配 `## 初始化上下文` 段落
- 每行 `- {category}: {expression} | {label}` 解析为一个 `ContextInitItemVO`
- `{expression}` 中的 `${varname}` 在运行时由 alertLabels 替换

**SkillRegistration 存储**：
- `SopParseResult` 新增 `ContextInitItems` 字段
- 存储在 `SkillRegistration.Metadata["contextInitItems"]` (JSON)
- 新增辅助方法 `GetContextInitItems()` / `SetContextInitItems()`

### 5. AlertRule 扩展

`AlertRule` 新增字段：

```csharp
public List<ContextInitItemVO> ContextProviders { get; private set; } = [];
```

EF Core JSONB 配置，AlertRule API 的 Create/Update DTO 扩展。

### 6. SOP Generation Prompt 增强

修改 `SopGenerationPromptBuilder.Build()` 的输出要求模板，新增：

```markdown
## 初始化上下文
（分析 RCA 过程中使用的数据源查询，提炼为以下格式。
  将硬编码的 namespace/service 值替换为 ${label_name} 占位符。）
- category: expression | description
```

### 7. SOP Validation 增强

`SopValidatorService.Validate()` 新增校验规则：
- 如果 SOP 缺少 `## 初始化上下文` 段落 → Warning: "SOP 缺少初始化上下文段落，建议补充以加速未来自动执行"
- 如果 `## 初始化上下文` 中的表达式使用硬编码值（不含 `${}`）→ Warning: "建议使用 ${label} 模板变量替代硬编码值"

## Entities & Value Objects

### New: `ContextInitItemVO`

已在 Domain/ValueObjects 中定义。

### New: `ContextInitResultVO`

```csharp
public sealed record ContextInitResultVO
{
    public List<ContextInitEntry> Entries { get; init; } = [];
    public TimeSpan TotalDuration { get; init; }
    public bool HasAnySuccess => Entries.Any(e => e.Success);
}
public sealed record ContextInitEntry
{
    public string Label { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? Result { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan Duration { get; init; }
}
```

### Modified: `AlertRule`

- 新增 `ContextProviders` 属性: `List<ContextInitItemVO>` (JSONB)
- 新增 `SetContextProviders(List<ContextInitItemVO>)` 方法

### Modified: `SkillRegistration` (via Metadata)

- 新增 `GetContextInitItems()` → `List<ContextInitItemVO>`
- 新增 `SetContextInitItems(List<ContextInitItemVO>)`

### Modified: `SopParseResult`

- 新增 `ContextInitItems` 属性: `List<ContextInitItemVO>`

## API Endpoints

### `PUT /api/alert-rules/{id}` (Modified)

Request body 扩展 `contextProviders` 字段。

### `GET /api/alert-rules/{id}` (Modified)

Response body 新增 `contextProviders` 字段。

### `POST /api/context/preview`

预览上下文初始化结果（用于调试/测试），接收 `{ items: ContextInitItemVO[], templateVariables: {} }`，返回 `ContextInitResultVO`。

## Data Flow

```
Alert Fired
    ↓
MatchAlertRules → AlertRule (with ContextProviders)
    ↓
Resolve SOP → SkillRegistration.GetContextInitItems()
    ↓
IncidentDispatcherService:
    1. Merge AlertRule.ContextProviders ∪ SOP.ContextInitItems (deduplicate)
    2. session.StateBag["contextInitItems"] = mergedItems
    3. session.StateBag["alertLabels"] = alertLabels
    ↓
Agent.RunStreamingAsync(messages, session)
    ↓
[Framework] ChatClientAgent.PrepareSessionAndMessagesAsync:
    ├── ChatHistoryProvider.InvokingAsync()   → 会话历史
    ├── S3AgentSkillsProvider.InvokingAsync() → Skill Instructions + Tools
    ├── SopContextInitProvider.InvokingAsync()                         ← NEW
    │     ├── Read StateBag["contextInitItems"] + StateBag["alertLabels"]
    │     ├── Template variable substitution (${namespace} → "demo-app")
    │     ├── Parallel data source queries (30s/item, 60s total)
    │     ├── Format as Instructions markdown
    │     └── Remove StateBag key (prevent re-query on subsequent rounds)
    └── MemoryProvider.InvokingAsync()        → 语义记忆
    ↓
LLM receives: Instructions(agent + skills + preloaded context + memory) + Messages(user + history)
    ↓
Agent starts with full diagnostic context → fewer tool calls → faster resolution
```

## Notes

- **与 Spec 026 互补关系**：本 Spec 的 `SopContextInitProvider` 提供"启动时一次性预查"；Spec 026 的 `query_correlated_context` 工具提供"运行时按需关联查"
- **模板变量**仅从 `alertLabels` 中提取，语法 `${key}`，不支持嵌套或表达式
- **SOP 的 `## 初始化上下文` 段落**位于 `## 适用条件` 之后、`## 操作步骤` / `## 处置步骤` 之前
- **数据路由**：Provider 根据 `ContextInitItemVO.Category` 找到匹配的 `DataSourceRegistration`（按 Category enum 映射），再通过 `IDataSourceQuerierFactory` 获取对应 Querier 执行查询
- **多数据源选择**：如果同一 Category 下注册了多个 DataSource（如两个 Prometheus 实例），默认使用第一个 `Connected` 状态的数据源。后续可扩展 `ContextInitItemVO.DataSourceName` 字段精确指定
- **Provider 无状态**：`SopContextInitProvider` 实例在多个 Agent 间共享，所有 session 特定数据通过 `StateBag` 传递，符合框架设计约束
- **预查结果 token 截断**：与 Spec 026 的 `ResultTruncator` 共用同一截断策略（默认 4000 token/条目）
