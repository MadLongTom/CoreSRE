# Research: 工作流执行引擎（顺序 + 并行 + 条件分支）

**Feature**: 012-workflow-execution-engine  
**Date**: 2026-02-11

---

## R1: Agent Framework Workflow API 模式

**Decision**: 使用 `Microsoft.Agents.AI.Workflows` 包的 `WorkflowBuilder` 低级 API 构建 Workflow，而非 `AgentWorkflowBuilder` 高级便捷方法。

**Rationale**: `AgentWorkflowBuilder.BuildSequential()` 和 `BuildConcurrent()` 仅接受 `AIAgent[]`，假设所有节点均为 Agent。我们的 DAG 包含混合节点类型（Agent/Tool/Condition/FanOut/FanIn），需要使用低级 `WorkflowBuilder` 从 `ExecutorBinding` 构建任意图结构。低级 API 提供 `AddEdge()` / `AddFanOutEdge()` / `AddFanInEdge()` / `AddSwitch()` 方法，完全覆盖三种编排模式。

**Alternatives considered**:
- `AgentWorkflowBuilder.BuildSequential/BuildConcurrent` — 过于抽象，无法表达混合节点类型和条件路由
- 完全自研执行引擎不使用 Agent Framework — 重复造轮子，丧失与 Agent Framework 生态的兼容性
- `WorkflowBuilder.AddSwitch()` + `SwitchBuilder` — 用于条件分支评估，是正确的选择

**Key API Surface**:
```
WorkflowBuilder(startExecutor)
  .BindExecutor(binding)
  .AddEdge(source, target)                   // 顺序
  .AddFanOutEdge(source, targets)            // 并行分发
  .AddFanInEdge(sources, target)             // 聚合
  .AddEdge<T>(source, target, condition)     // 条件分支
  .AddSwitch(source, switchBuilder => ...)   // 多条件分支
  .WithOutputFrom(endExecutor)
  .Build()
```

---

## R2: DAG-to-Workflow 转换策略

**Decision**: 实现 `IWorkflowEngine` 接口，其 `BuildAndExecuteAsync()` 方法接受 `WorkflowGraphVO` 和输入数据，执行以下步骤：
1. 拓扑排序 DAG 获取执行顺序
2. 为每个节点创建 `ExecutorBinding`（Agent 节点 → `AIAgent.BindAsExecutor()`，Tool 节点 → `FunctionExecutor`，Condition/FanOut/FanIn → 内置 Executor）
3. 遍历边列表添加 `AddEdge()` / `AddFanOutEdge()` / `AddFanInEdge()` / `AddSwitch()`
4. `Build()` 生成 `Workflow`，通过 `InProcessExecution.RunAsync()` 执行

**Rationale**: 将 DAG 定义（持久化的静态数据）与 Agent Framework 的运行时 Workflow 对象解耦。转换逻辑集中在 `WorkflowEngine` 服务中，便于测试和扩展。

**Alternatives considered**:
- 在 Domain 层直接持有 Workflow 对象 — 违反 DDD（Domain 不依赖外部包）
- 每种编排模式用独立的 Builder — 过度抽象，增加分支复杂度；统一的图遍历更简洁

---

## R3: 异步执行架构

**Decision**: 采用 `Channel<ExecuteWorkflowRequest>` + `WorkflowExecutionBackgroundService`（继承 `BackgroundService`）模式。API 端点将执行请求写入 Channel 后立即返回 202 Accepted；后台服务消费 Channel 中的请求，通过 `IServiceScopeFactory` 创建独立作用域执行工作流。

**Rationale**: 与现有 `McpDiscoveryBackgroundService` 模式完全一致，团队已有成功经验。`Channel<T>` 提供高性能无锁队列，支持多生产者单消费者场景。`BackgroundService` 生命周期由 ASP.NET Core 主机管理。

**Alternatives considered**:
- `Task.Run()` + fire-and-forget — 无法追踪执行状态，DI 作用域管理困难
- 消息队列（RabbitMQ/Kafka）— 过重，当前规模不需要分布式队列
- Hangfire/Quartz — 引入额外中间件依赖，Channel 已足够

---

## R4: 条件表达式求值

**Decision**: 使用 `JsonPath.Net` v3.0.0 NuGet 包进行 JSON Path 求值。条件表达式格式为 `<jsonPath> == <expectedValue>`（如 `$.severity == "high"`），通过简单的字符串分割解析。

**Rationale**: `System.Text.Json` 不内置 JSON Path 查询能力。`JsonPath.Net` 符合 RFC 9535 标准，直接与 `System.Text.Json.Nodes.JsonNode` 配合使用，无需 DOM 转换。支持 AOT 编译。后续可扩展为更复杂的表达式语法而无需更换库。

**Alternatives considered**:
- 手动遍历 `JsonNode`——仅支持简单点号路径，不支持数组索引和过滤器
- `Newtonsoft.Json.SelectToken()`——引入重型依赖，与项目使用的 `System.Text.Json` 不一致
- 自研表达式解析器——初始过度设计，违反 YAGNI

**Expression parsing pattern**:
```
Input:  "$.severity == \"high\""
Split:  ["$.severity", "\"high\""]
Step 1: JsonPath.Parse("$.severity").Evaluate(jsonNode) → matches
Step 2: Compare matches[0].Value.ToString() with "high"
Result: true/false
```

---

## R5: 节点执行器类型映射

**Decision**: 每种 DAG 节点类型映射到不同的 Agent Framework Executor 类型：

| DAG NodeType | Executor 类型 | 实现方式 |
|---|---|---|
| Agent | `AIAgent.BindAsExecutor()` | 通过 `IAgentResolver.ResolveAsync()` 获取 AIAgent 实例 |
| Tool | `FunctionExecutor<JsonElement>` | 委托给 `IToolInvokerFactory.Invoke()` 调用 Tool Gateway |
| Condition | `FunctionExecutor<JsonElement>` | 透传输入，下游通过条件边路由 |
| FanOut | `ChatForwardingExecutor` | 使用 `AddFanOutEdge()` 构建分发 |
| FanIn | `AggregateTurnMessagesExecutor` | 使用 `AddFanInEdge()` 构建聚合 |

**Rationale**: 直接利用 Agent Framework 内置的 Executor 类型，避免重新实现并行调度和消息路由逻辑。对于 Agent 节点，复用已有的 `IAgentResolver` 服务解析 AIAgent 实例（支持 ChatClient、A2A 两种类型）。

**Alternatives considered**:
- 所有节点统一用 `FunctionExecutor` 包装 — Agent 节点会丢失 Agent Framework 的会话管理能力
- 自研并行调度 — `Task.WhenAll()` 虽然可行，但 Agent Framework 的 FanOut/FanIn 已集成消息路由和错误处理

---

## R6: 执行状态持久化策略

**Decision**: WorkflowExecution 聚合根负责状态管理，执行引擎在每个节点状态变更时调用 `UpdateAsync()` 持久化到数据库。采用乐观更新（每次状态变更直接写入），不使用事件溯源。

**Rationale**: 与现有仓储模式一致（`IRepository<T>.UpdateAsync()`）。节点状态变更频率较低（每个节点仅 2-3 次状态变更），无需事件溯源的额外复杂性。JSONB 存储的 `List<NodeExecutionVO>` 整体更新，与 `WorkflowGraphVO` 的存储模式一致。

**Alternatives considered**:
- 事件溯源（EventSourcing）— 过度设计，当前场景无需回放
- 仅在执行完成后一次性持久化 — 不满足"实时更新执行状态"的 FR-010 要求
- 每个 NodeExecution 独立表 — 增加 JOIN 复杂度，JSONB 嵌套更简洁

---

## R7: IWorkflowEngine 接口位置

**Decision**: `IWorkflowEngine` 接口定义在 `CoreSRE.Domain/Interfaces/`，实现在 `CoreSRE.Infrastructure/Services/`。

**Rationale**: 执行引擎的实现依赖 Agent Framework 外部包（`Microsoft.Agents.AI.Workflows`），不能放在 Domain 层。按 DDD 规则，接口在 Domain/Application 层定义，实现在 Infrastructure 层。与已有的 `IAgentResolver`（接口在 Application/Interfaces，实现在 Infrastructure/Services）模式一致。考虑到 `IWorkflowEngine` 的方法签名涉及到 Domain 实体（`WorkflowExecution`、`WorkflowGraphVO`），将接口放在 Domain 层更合适。

**Alternatives considered**:
- 接口放在 Application 层 — 可行但 IWorkflowEngine 操作的是 Domain 实体类型，放 Domain 更自然
- 不抽象接口直接在 Handler 中实现 — 违反 Constitution V（Interface-Before-Implementation）

---

## R8: NuGet 包依赖变更

**Decision**: 需要为以下项目添加新的 NuGet 包引用：

| Project | Package | Version | Purpose |
|---|---|---|---|
| CoreSRE.Infrastructure | `Microsoft.Agents.AI.Workflows` | 1.0.0-preview.260209.1 | WorkflowBuilder, Executor 类型 |
| CoreSRE.Application | `JsonPath.Net` | 3.0.0 | 条件表达式 JSON Path 求值 |

**Rationale**: `Microsoft.Agents.AI.Workflows` 已在本地 NuGet 缓存中，无需额外下载。`JsonPath.Net` 是轻量级 RFC 9535 实现，无额外传递依赖。

---

## R9: 图快照存储

**Decision**: `WorkflowExecution` 实体包含一个 `GraphSnapshot` 属性，类型为 `WorkflowGraphVO`，在启动执行时从 `WorkflowDefinition.Graph` 深拷贝。以 JSONB 格式存储在 `workflow_executions` 表的 `graph_snapshot` 列中。

**Rationale**: `WorkflowGraphVO` 是 `sealed record`，其 `with` 表达式或直接赋值即可创建副本（记录类型的值语义保证不可变性——record 的 `init` 属性确保赋值后不可修改）。EF Core 的 `OwnsOne().ToJson()` 已有成功模式（参见 WorkflowDefinitionConfiguration）。快照与定义使用相同的 VO 类型，无需额外的映射。

**Alternatives considered**:
- 存储 WorkflowDefinitionId + 版本号，执行时重新查询定义 — 无法防止定义删除后执行记录孤立
- 将 Graph 序列化为 `string` 存储 — 丢失类型安全和 JSONB 查询能力
