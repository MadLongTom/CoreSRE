# CoreSRE — Team Agent 编排 Spec 总览

**文档编号**: TEAM-AGENT-SPEC-INDEX  
**版本**: 1.0.0  
**创建日期**: 2026-02-17  
**关联文档**: [SPEC-INDEX](SPEC-INDEX.md) | [Agent-Framework-Analysis](../Agent-Framework-Analysis.md)  

> 本文档将 `AgentType.Team` 多 Agent 编排能力拆分为可独立交付的 Spec 清单。  
> 目标：在 CoreSRE 中新增 Team 类型 Agent，集成 Microsoft Agent Framework 内置的所有 Team 编排模式（Sequential / Concurrent / RoundRobin / Handoffs），并扩展 AutoGen 的 Selector 和 MagneticOne 模式。  
> 原则：复用 agent-framework 的 `GroupChatManager` 抽象基类和 `AgentWorkflowBuilder` API；MagneticOne 作为自定义 `GroupChatManager` 扩展。  
> 前置依赖：SPEC-001（Agent CRUD）、SPEC-006（LLM Provider 配置）均已完成。

---

## 调研背景

### agent-framework (.NET) 内置编排模式

| 模式 | API 入口 | 核心类 | 机制 |
|------|---------|--------|------|
| **Sequential** | `AgentWorkflowBuilder.BuildSequential(agents)` | `WorkflowBuilder` | A→B→C 线性管道，output 透传 |
| **Concurrent** | `AgentWorkflowBuilder.BuildConcurrent(agents, aggregator?)` | `ConcurrentEndExecutor` | Fan-out 并行 → Fan-in 聚合 |
| **RoundRobin** | `CreateGroupChatBuilderWith(agents => new RoundRobinGroupChatManager(agents))` | `RoundRobinGroupChatManager` | 索引轮转 `(i+1) % count` |
| **Handoffs** | `CreateHandoffBuilderWith(initial).WithHandoff(from, to, reason)` | `HandoffsWorkflowBuilder` | Agent 通过 `handoff_to_X` tool call 自主交接 |

### AutoGen (Python) 扩展模式

| 模式 | 源码位置 | 机制 |
|------|---------|------|
| **Selector** | `_selector_group_chat.py` | `selector_func` → `candidate_func` → LLM 从候选列表中选择下一个发言者 |
| **MagneticOne** | `_magentic_one/` | 双循环账本：外循环（收集事实→生成计划）+ 内循环（Progress Ledger JSON 评估→Agent 选择→停滞检测→重规划） |

### 扩展点：`GroupChatManager` 抽象基类

agent-framework 的 `GroupChatManager` 是所有 GroupChat 模式的基础，天然支持自定义扩展：

```csharp
public abstract class GroupChatManager
{
    public int IterationCount { get; internal set; }
    public int MaximumIterationCount { get; set; } = 40;

    // 核心抽象：选择下一个 Agent（必须实现）
    protected internal abstract ValueTask<AIAgent> SelectNextAgentAsync(
        IReadOnlyList<ChatMessage> history, CancellationToken ct);

    // 可选覆写：过滤/转换传递给 Agent 的历史
    protected internal virtual ValueTask<IEnumerable<ChatMessage>> UpdateHistoryAsync(
        IReadOnlyList<ChatMessage> history, CancellationToken ct);

    // 可选覆写：是否终止
    protected internal virtual ValueTask<bool> ShouldTerminateAsync(
        IReadOnlyList<ChatMessage> history, CancellationToken ct);

    // 重置状态
    protected internal virtual void Reset();
}
```

Selector 和 MagneticOne 将作为自定义 `GroupChatManager` 子类实现，与内置的 `RoundRobinGroupChatManager` 地位平等。

---

## Spec 拆分策略

按照**纵向切片 + 渐进式交付**原则拆分为 4 个 Spec：

| Spec | 标题 | 优先级 | 交付价值 |
|------|------|--------|---------|
| SPEC-100 | Team Agent 领域模型与 CRUD | P1 | 可注册/管理 6 种 Team Agent，前端可配置 |
| SPEC-101 | Team Agent 执行引擎（内置模式） | P1 | Sequential / Concurrent / RoundRobin / Handoffs 可运行 |
| SPEC-102 | Selector 模式 — LLM 动态选择 | P1 | LLM 驱动的智能 Agent 选择 |
| SPEC-103 | MagneticOne 模式 — 双循环编排 | P2 | 复杂任务的自主规划与自动重规划 |

---

## 模块 M10：Team Agent 多 Agent 编排

### SPEC-100: Team Agent 领域模型与 CRUD

**优先级**: P1（Phase 1 — 1 周）  
**前置依赖**: SPEC-001（Agent CRUD）, SPEC-006（LLM Provider 配置）  

**简述**: 在 `AgentType` 枚举中新增 `Team` 类型，引入 `TeamMode` 枚举（6 种编排模式）和 `TeamConfigVO` 值对象。扩展 `AgentRegistration` 聚合根支持 `CreateTeam()` 工厂方法和 Team 专属验证。前端 Agent 注册页面新增 Team 类型配置表单。

**核心领域模型**:

```csharp
// Domain/Enums/TeamMode.cs
public enum TeamMode
{
    Sequential,      // 顺序管道
    Concurrent,      // 并发聚合
    RoundRobin,      // 轮询 GroupChat
    Handoffs,        // 交接/Swarm
    Selector,        // LLM 动态选择
    MagneticOne,     // 双循环账本编排
}

// Domain/ValueObjects/TeamConfigVO.cs
public record TeamConfigVO
{
    // ── 通用 ──
    public required TeamMode Mode { get; init; }
    public required List<Guid> ParticipantIds { get; init; }
    public int MaxIterations { get; init; } = 40;

    // ── Handoffs 专属 ──
    public Dictionary<Guid, List<HandoffTargetVO>>? HandoffRoutes { get; init; }
    public Guid? InitialAgentId { get; init; }

    // ── Selector 专属 ──
    public Guid? SelectorProviderId { get; init; }
    public string? SelectorModelId { get; init; }
    public string? SelectorPrompt { get; init; }
    public bool AllowRepeatedSpeaker { get; init; } = true;

    // ── MagneticOne 专属 ──
    public Guid? OrchestratorProviderId { get; init; }
    public string? OrchestratorModelId { get; init; }
    public int MaxStalls { get; init; } = 3;
    public string? FinalAnswerPrompt { get; init; }

    // ── Concurrent 专属 ──
    public string? AggregationStrategy { get; init; }
}

public record HandoffTargetVO
{
    public required Guid TargetAgentId { get; init; }
    public string? Reason { get; init; }
}
```

**AgentRegistration 变更**:

```csharp
public class AgentRegistration : BaseEntity
{
    // ... 现有字段不变 ...
    public TeamConfigVO? TeamConfig { get; private set; }  // NEW (JSONB)

    public static AgentRegistration CreateTeam(
        string name, string? description, TeamConfigVO teamConfig)
    {
        Guard.AgainstNullOrEmpty(name, nameof(name));
        Guard.AgainstNull(teamConfig, nameof(teamConfig));
        Guard.Against(teamConfig.ParticipantIds.Count == 0, "Team must have at least one participant");
        // 模式特定验证...
        return new AgentRegistration
        {
            Name = name,
            Description = description,
            AgentType = AgentType.Team,
            Status = AgentStatus.Registered,
            TeamConfig = teamConfig,
            HealthCheck = HealthCheckVO.Default(),
        };
    }
}
```

**领域验证规则（按 TeamMode）**:

| Mode | 验证规则 |
|------|---------|
| Sequential | `ParticipantIds.Count >= 2` |
| Concurrent | `ParticipantIds.Count >= 2` |
| RoundRobin | `ParticipantIds.Count >= 2` |
| Handoffs | `InitialAgentId` 必须在 `ParticipantIds` 中；`HandoffRoutes` 非空；所有 Route 的 source/target 必须在 `ParticipantIds` 中 |
| Selector | `SelectorProviderId` 和 `SelectorModelId` 不能为空；`ParticipantIds.Count >= 2` |
| MagneticOne | `OrchestratorProviderId` 和 `OrchestratorModelId` 不能为空；`ParticipantIds.Count >= 1` |

**端点变更**:

| 端点 | 变更 |
|------|------|
| `POST /api/agents` | 支持 `agentType: "Team"` + `teamConfig` 请求体 |
| `PUT /api/agents/{id}` | 支持更新 `teamConfig`（AgentType 不可变） |
| `GET /api/agents/{id}` | 返回 DTO 中包含 `teamConfig` |
| `GET /api/agents` | 支持 `?type=Team` 过滤 |

**数据库变更**:
- `AgentRegistration` 表新增 `TeamConfig` 列（JSONB，nullable）
- EF Core Migration: `AddColumn<TeamConfigVO>("TeamConfig")`

**前端变更**:
- Agent 注册表单新增 Team 类型选项
- 选择 Team 后展示 TeamMode 选择和对应配置表单
- Participant 选择器（多选 Agent 列表，排除自身和其他 Team Agent 以防递归）
- Handoffs 模式：可视化交接关系编辑器
- Selector/MagneticOne 模式：LLM Provider + Model 选择

**验收标准**:
1. **Given** 用户选择 AgentType=Team，TeamMode=RoundRobin，并选择 3 个参与者 Agent，**When** 提交注册，**Then** Agent 成功创建，`TeamConfig` JSONB 正确持久化。
2. **Given** 用户尝试创建 TeamMode=Handoffs 但未设置 `InitialAgentId`，**When** 提交注册，**Then** 返回 400 验证错误。
3. **Given** 用户尝试创建 TeamMode=MagneticOne 但未配置 OrchestratorProviderId，**When** 提交注册，**Then** 返回 400 验证错误。
4. **Given** 用户设置 Handoffs 路由引用了不在 ParticipantIds 中的 Agent，**When** 提交注册，**Then** 返回 400 验证错误。
5. **Given** 已创建的 Team Agent，**When** 通过 GET API 查询，**Then** 返回完整 DTO 包含 `teamConfig` 及所有子属性。
6. **Given** Agent 列表页，**When** 筛选 `type=Team`，**Then** 仅返回 Team 类型的 Agent。

---

### SPEC-101: Team Agent 执行引擎（内置 4 模式）

**优先级**: P1（Phase 2 — 2 周）  
**前置依赖**: SPEC-100（Team 领域模型）  

**简述**: 实现 `ITeamResolver` 接口，根据 `TeamMode` 将 `AgentRegistration(Team)` 解析为 agent-framework 的 `Workflow` 对象并包装为 `AIAgent`。直接调用 agent-framework 内置 API 实现 Sequential / Concurrent / RoundRobin / Handoffs 四种模式。扩展 `AgentResolverService` 支持 `AgentType.Team` 的解析和 Chat 交互。

**核心接口**:

```csharp
// Application/Interfaces/ITeamResolver.cs
public interface ITeamResolver
{
    /// <summary>
    /// 将 Team 类型的 AgentRegistration 解析为可运行的 AIAgent。
    /// 内部按 TeamMode 分发到 agent-framework 的不同 Builder API。
    /// </summary>
    Task<AIAgent> ResolveAsync(
        AgentRegistration teamAgent,
        string conversationId,
        CancellationToken ct = default);
}
```

**agent-framework API 映射**:

| TeamMode | 实现逻辑 |
|----------|---------|
| Sequential | 按 `ParticipantIds` 顺序解析每个子 Agent → `AgentWorkflowBuilder.BuildSequential(agents)` → `workflow.AsAIAgent()` |
| Concurrent | 解析所有子 Agent → `AgentWorkflowBuilder.BuildConcurrent(agents, aggregator)` → `workflow.AsAIAgent()` |
| RoundRobin | 解析所有子 Agent → `AgentWorkflowBuilder.CreateGroupChatBuilderWith(agents => new RoundRobinGroupChatManager(agents) { MaximumIterationCount = config.MaxIterations }).AddParticipants(agents).Build()` → `workflow.AsAIAgent()` |
| Handoffs | 解析 `InitialAgentId` 为 initialAgent → `AgentWorkflowBuilder.CreateHandoffBuilderWith(initialAgent).WithHandoff(from, to, reason)...Build()` → `workflow.AsAIAgent()` |

**AgentResolverService 扩展**:

```csharp
// 在现有 ResolveAsync 方法中增加分支
case AgentType.Team:
    var teamAgent = await _teamResolver.ResolveAsync(agent, conversationId, ct);
    return new ResolvedAgent(teamAgent, null);
```

**Chat 集成**:
- Team Agent 和 ChatClient Agent 使用相同的 Chat UI 交互方式
- 用户发送消息 → 通过 `IChatClient` → Team Workflow 内部编排子 Agent → 返回最终结果
- Chat 历史持久化到 `AgentSessionRecord`（与现有机制相同）

**执行中间事件推送（SignalR）**:
- 复用 `IWorkflowExecutionNotifier` 推送编排过程中的关键事件
- 新增 Team 专属事件：`TeamIterationStarted(agentName)`, `TeamIterationCompleted(agentName, response)`
- 前端 Chat UI 中以折叠面板展示编排过程（哪个子 Agent 在说话、说了什么）

**验收标准**:
1. **Given** 一个 Sequential Team Agent（A→B→C），**When** 用户在 Chat UI 发送消息，**Then** 消息依次经过 A、B、C 处理，返回 C 的最终输出。
2. **Given** 一个 Concurrent Team Agent（A∥B∥C），**When** 用户发送消息，**Then** A、B、C 并行处理，结果按聚合策略合并后返回。
3. **Given** 一个 RoundRobin Team Agent（MaxIterations=3），**When** 用户发送消息，**Then** A→B→C 依次发言（3 轮），最后一轮的输出作为最终结果。
4. **Given** 一个 Handoffs Team Agent（Triage→Math/History），**When** 用户问数学题，**Then** Triage 判断后 handoff 给 Math Agent，Math 回答后结果返回用户。
5. **Given** Team Agent 执行过程中，**When** 查看 Chat UI，**Then** 可看到每个子 Agent 的中间消息（折叠展示编排过程）。
6. **Given** 一个 Handoffs Team 的参与者 Agent 不存在，**When** 尝试执行，**Then** 返回明确错误：`参与者 Agent 不存在: {agentId}`。

---

### SPEC-102: Selector 模式 — LLM 动态选择

**优先级**: P1（Phase 3 — 1 周）  
**前置依赖**: SPEC-101（Team 执行引擎）  

**简述**: 实现 `SelectorGroupChatManager` 自定义 `GroupChatManager`，通过 LLM 根据对话历史动态选择下一个发言者。移植 AutoGen Python `SelectorGroupChat` 的选择逻辑（构建角色描述 + 历史 → LLM 返回 Agent 名称 → 正则验证 → 重试）。

**核心实现**:

```csharp
// Infrastructure/Services/SelectorGroupChatManager.cs
public class SelectorGroupChatManager : GroupChatManager
{
    private readonly IReadOnlyList<AIAgent> _agents;
    private readonly IChatClient _selectorClient;  // Orchestrator LLM
    private readonly string _selectorPrompt;
    private readonly bool _allowRepeatedSpeaker;
    private readonly int _maxSelectorAttempts;
    private string? _previousSpeaker;

    protected internal override async ValueTask<AIAgent> SelectNextAgentAsync(
        IReadOnlyList<ChatMessage> history, CancellationToken ct)
    {
        // 1. 构建候选列表（如 !_allowRepeatedSpeaker 则排除 _previousSpeaker）
        // 2. 如果只剩 1 个候选，直接返回
        // 3. 构建 LLM 提示词：角色描述 + 对话历史 + "选择下一个发言者"
        // 4. 调用 _selectorClient，解析响应中的 Agent 名称
        // 5. 名称验证（是否在候选列表中），失败则重试
        // 6. 更新 _previousSpeaker，返回选中的 Agent
    }
}
```

**默认选择提示词**（从 AutoGen 移植并本地化）:

```
You are in a role play game. The following roles are available:
{roles}

Read the following conversation. Then select the next role from [{participants}] to play.
Only return the role name, nothing else.

{history}
```

**ITeamResolver 扩展**:

```csharp
case TeamMode.Selector:
    var selectorClient = await BuildChatClient(config.SelectorProviderId, config.SelectorModelId);
    return CreateGroupChatBuilderWith(agents => new SelectorGroupChatManager(
        agents, selectorClient,
        selectorPrompt: config.SelectorPrompt,
        allowRepeatedSpeaker: config.AllowRepeatedSpeaker,
        maxSelectorAttempts: 3
    ) { MaximumIterationCount = config.MaxIterations })
    .AddParticipants(resolvedAgents)
    .Build()
    .AsAIAgent();
```

**验收标准**:
1. **Given** 一个 Selector Team（Math Agent + History Agent + Science Agent），SelectorModelId 配置为有效 LLM，**When** 用户问数学题，**Then** LLM 选择 Math Agent 发言。
2. **Given** `AllowRepeatedSpeaker=false`，上一轮 Math Agent 发言，**When** LLM 再次选择 Math，**Then** 系统排除 Math 后从剩余候选中重新选择。
3. **Given** LLM 返回了不在候选列表中的名称，**When** 解析失败，**Then** 系统重试（最多 3 次），仍失败则抛出明确错误。
4. **Given** 自定义 `SelectorPrompt`，**When** 执行 Selector Team，**Then** 使用自定义提示词而非默认提示词。
5. **Given** Selector Team 达到 `MaxIterations` 限制，**When** 执行，**Then** 自动终止并返回最后一轮的输出。

---

### SPEC-103: MagneticOne 模式 — 双循环账本编排

**优先级**: P2（Phase 4 — 2 周）  
**前置依赖**: SPEC-102（Selector 模式，复用 LLM 调用基础设施）  

**简述**: 实现 `MagneticOneGroupChatManager` 自定义 `GroupChatManager`，将 AutoGen Python 的 `MagenticOneOrchestrator` 双循环账本逻辑移植到 C#。Orchestrator LLM 负责任务分析、事实收集、计划生成、进度评估和停滞重规划。这是最复杂的编排模式，适合探索性复杂任务。

**双循环架构**:

```
┌─── 外循环 (Task Ledger) ──────────────────────────────────┐
│  1. 收集事实 (GIVEN / TO LOOK UP / TO DERIVE / GUESS)      │
│  2. 生成计划 (bullet-point plan, 考虑团队组成)             │
│  3. 广播 Task Ledger 给所有参与者                           │
│  4. 停滞时: 更新事实 → 更新计划 → 重置 → 重启              │
│                                                            │
│  ┌─── 内循环 (Progress Ledger) ────────────────────────┐   │
│  │  每步 LLM 评估 → JSON:                              │   │
│  │  {                                                   │   │
│  │    is_request_satisfied: {reason, answer: bool},     │   │
│  │    is_in_loop: {reason, answer: bool},               │   │
│  │    is_progress_being_made: {reason, answer: bool},   │   │
│  │    next_speaker: {reason, answer: "AgentName"},      │   │
│  │    instruction_or_question: {reason, answer: "..."}  │   │
│  │  }                                                   │   │
│  │  → 停滞计数: 无进展/循环 → n_stalls++                 │   │
│  │  → n_stalls >= max_stalls → 回到外循环重新规划        │   │
│  └──────────────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────────────┘
```

**核心实现**:

```csharp
// Infrastructure/Services/MagneticOneGroupChatManager.cs
public class MagneticOneGroupChatManager : GroupChatManager
{
    private readonly IReadOnlyList<AIAgent> _agents;
    private readonly IChatClient _orchestratorClient;
    private readonly int _maxStalls;
    private readonly string _finalAnswerPrompt;

    // 外循环状态
    private string _task = "";
    private string _facts = "";
    private string _plan = "";
    private bool _needsOuterLoop = true;

    // 内循环状态
    private int _nStalls;
    private ProgressLedger? _currentLedger;

    protected internal override async ValueTask<AIAgent> SelectNextAgentAsync(
        IReadOnlyList<ChatMessage> history, CancellationToken ct)
    {
        // 外循环
        if (_needsOuterLoop)
        {
            _task = ExtractTask(history);
            _facts = await GatherFactsAsync(history, ct);
            _plan = await CreatePlanAsync(ct);
            _needsOuterLoop = false;
            _nStalls = 0;
        }

        // 内循环：评估 Progress Ledger
        _currentLedger = await EvaluateProgressLedgerAsync(history, ct);

        // 停滞检测
        if (!_currentLedger.IsProgressBeingMade || _currentLedger.IsInLoop)
            _nStalls++;
        else
            _nStalls = Math.Max(0, _nStalls - 1);

        if (_nStalls >= _maxStalls)
        {
            await UpdateTaskLedgerAsync(history, ct);
            _needsOuterLoop = true;
            // 下次调用时将重新进入外循环
        }

        return FindAgent(_currentLedger.NextSpeaker);
    }

    protected internal override async ValueTask<bool> ShouldTerminateAsync(
        IReadOnlyList<ChatMessage> history, CancellationToken ct)
    {
        if (_currentLedger?.IsRequestSatisfied == true)
            return true;
        return await base.ShouldTerminateAsync(history, ct);
    }

    protected internal override async ValueTask<IEnumerable<ChatMessage>> UpdateHistoryAsync(
        IReadOnlyList<ChatMessage> history, CancellationToken ct)
    {
        // 注入 Orchestrator 指令到历史末尾
        if (_currentLedger != null)
        {
            var instruction = new ChatMessage(ChatRole.User,
                _currentLedger.InstructionOrQuestion);
            return history.Append(instruction);
        }
        return history;
    }

    protected internal override void Reset()
    {
        base.Reset();
        _task = "";
        _facts = "";
        _plan = "";
        _nStalls = 0;
        _needsOuterLoop = true;
        _currentLedger = null;
    }
}
```

**Progress Ledger 数据模型**:

```csharp
// Infrastructure/Services/MagneticOne/ProgressLedger.cs
public record ProgressLedger
{
    public required bool IsRequestSatisfied { get; init; }
    public required string IsRequestSatisfiedReason { get; init; }
    public required bool IsInLoop { get; init; }
    public required bool IsProgressBeingMade { get; init; }
    public required string NextSpeaker { get; init; }
    public required string InstructionOrQuestion { get; init; }
}
```

**7 个 LLM 提示词模板**（从 AutoGen Python 移植）:

| 模板 | 用途 | 外/内循环 |
|------|------|----------|
| `TASK_LEDGER_FACTS_PROMPT` | 事实收集（GIVEN / TO LOOK UP / TO DERIVE / GUESS） | 外 |
| `TASK_LEDGER_PLAN_PROMPT` | 基于团队组成生成 bullet-point 计划 | 外 |
| `TASK_LEDGER_FULL_PROMPT` | 合并任务+团队+事实+计划为完整上下文 | 外 |
| `PROGRESS_LEDGER_PROMPT` | 评估进度并输出 JSON（5 个字段） | 内 |
| `TASK_LEDGER_FACTS_UPDATE_PROMPT` | 停滞时更新事实 | 外（重规划） |
| `TASK_LEDGER_PLAN_UPDATE_PROMPT` | 停滞时更新计划（避免重复错误） | 外（重规划） |
| `FINAL_ANSWER_PROMPT` | 生成最终答案 | 终止 |

**SignalR 事件推送**:
- MagneticOne 模式在执行过程中推送额外事件：
  - `TeamPlanCreated(executionId, facts, plan)` — 外循环完成规划
  - `TeamProgressEvaluated(executionId, ledger)` — 内循环进度评估
  - `TeamReplanning(executionId, reason)` — 停滞触发重规划
  - `TeamFinalAnswer(executionId, answer)` — 生成最终答案

**验收标准**:
1. **Given** 一个 MagneticOne Team（Coder + Researcher + Writer），**When** 用户提交复杂任务，**Then** Orchestrator LLM 首先收集事实、生成计划，然后逐步选择子 Agent 执行。
2. **Given** 内循环中连续 3 轮无进展（`is_progress_being_made=false`），**When** `n_stalls >= max_stalls`，**Then** 自动进入外循环重新收集事实和规划。
3. **Given** Progress Ledger 返回 `is_request_satisfied=true`，**When** Orchestrator 检测到任务完成，**Then** 生成 Final Answer 并终止编排。
4. **Given** LLM 返回的 Progress Ledger JSON 格式异常，**When** 解析失败，**Then** 系统重试（最多 10 次），仍失败则终止并返回明确错误。
5. **Given** MagneticOne Team 达到 `MaxIterations(20)` 限制，**When** 任务未完成，**Then** 自动终止并返回当前最佳结果。
6. **Given** 执行过程中，**When** 前端通过 SignalR 接收事件，**Then** Chat UI 实时展示规划进度、当前选中的 Agent 和评估状态。

---

## 依赖关系图

```
SPEC-001 (Agent CRUD) ──────┐
                             ├──→ SPEC-100 (Team 领域模型 & CRUD)
SPEC-006 (LLM Provider) ────┘           │
                                        ▼
                              SPEC-101 (执行引擎: 内置 4 模式)
                                        │
                                        ▼
                              SPEC-102 (Selector: LLM 选择)
                                        │
                                        ▼
                              SPEC-103 (MagneticOne: 双循环)
```

---

## 与主 SPEC-INDEX 的关系

本 SPEC-INDEX 编号范围 `100-103`，归属新模块 **M10: Team Agent 多 Agent 编排**。应在主 [SPEC-INDEX](SPEC-INDEX.md) 中新增以下条目：

```
P1-Team (Team Agent 多 Agent 编排)
  │   详见 [TEAM-AGENT-SPEC-INDEX](TEAM-AGENT-SPEC-INDEX.md)
  ├── SPEC-100: Team Agent 领域模型与 CRUD
  ├── SPEC-101: Team Agent 执行引擎（内置 4 模式）
  ├── SPEC-102: Selector 模式 — LLM 动态选择
  └── SPEC-103: MagneticOne 模式 — 双循环账本编排
```

---

## 技术风险与缓解

| 风险 | 影响 | 缓解策略 |
|------|------|---------|
| **MagneticOne LLM 调用成本** | 每轮内循环 1 次 LLM 调用，外循环 2 次；20 轮 ≈ 25+ 次 | `MaxIterations` 默认 20，`MaxStalls` 默认 3，可配置 |
| **Progress Ledger JSON 解析失败** | LLM 输出非严格 JSON | 重试最多 10 次 + `extract_json_from_str` 正则兜底 + Structured Output |
| **子 Agent 解析循环引用** | Team 的 Participant 引用另一个 Team → 递归 | 首期禁止 Team 嵌套 Team（`CreateTeam` 验证） |
| **Handoffs 死循环** | Agent 之间互相 handoff 无终止条件 | agent-framework 内置 `MaximumIterationCount` 兜底 |
| **并发 Team 执行的 Agent 实例隔离** | 共享 `IChatClient` 实例可能有状态污染 | 每次 `ResolveAsync` 创建新的 Agent 实例（非单例） |
| **agent-framework Workflow.AsAIAgent() API 变更** | 依赖未稳定 API | 抽象为 `ITeamResolver`，隔离框架变更影响面 |
