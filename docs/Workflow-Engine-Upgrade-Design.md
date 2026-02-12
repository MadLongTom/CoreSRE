# 工作流引擎升级设计报告

> 目标：将 CoreSRE 工作流从「能编译但跑不通」升级为 n8n 级别的可用工作流引擎。
> 原则：**渐进式升级**，保持 C# / .NET 技术栈和现有 Clean Architecture 分层，借鉴 n8n 的设计理念但不照搬 TypeScript 代码。

---

## 一、现状诊断

### 当前已有能力

| 层 | 已完成 |
|----|--------|
| Domain | `WorkflowDefinition` 聚合根、`WorkflowExecution` 状态机、5 种节点类型、DAG 校验（环/孤立/自环/重复） |
| Application | CRUD Command/Query Handler（创建/查询/更新/删除工作流 + 引用校验）、ExecuteWorkflowCommand + Channel 入队 |
| Infrastructure | `WorkflowEngine`（Kahn 拓扑排序 + 顺序/并行/条件执行）、`BackgroundService` 消费者、`ConditionEvaluator`（仅 `==`） |
| API | 8 个端点（5 CRUD + 3 执行） |
| Frontend | ReactFlow DAG 编辑器/查看器/执行状态可视化、5 种自定义节点、执行历史表格 |

### 核心缺陷

| # | 缺陷 | 影响 |
|---|------|------|
| D1 | **节点间数据模型太弱** — 仅 `string? lastOutput` 线性传递 | 不支持批量数据、多输出端口、非线性数据引用 |
| D2 | **执行模型太僵** — 一次性拓扑排序，按固定顺序执行 | 不支持动态路由、暂停/恢复、部分执行 |
| D3 | **无实时推送** — 前端只能刷新页面 | 用户无法看到节点逐个执行的进度 |
| D4 | **无表达式引擎** — 条件仅支持 `==` | 无法做复杂条件、无法引用上游节点数据 |
| D5 | **NodeExecutionVO.Input 从未写入** | 执行记录缺乏可追溯性 |
| D6 | **Config 字段被忽略** | 节点参数配置无法影响执行行为 |
| D7 | **错误处理一刀切** — FanOut 任一分支失败 → 全部失败 | 缺少 continueOnFail、错误路由 |
| D8 | **Agent 调用无状态** — 无对话历史 | 多步 Agent 协作无上下文 |
| D9 | **Channel 串行消费** — 同一时刻只跑 1 个工作流 | 吞吐量瓶颈 |
| D10 | **FanIn 没有聚合数据给后续节点** - 聚合数据后 lastOutput 更新，但类型是 JSON Array 字符串 | 下游解析困难 |

---

## 二、升级总体架构

### 2.1 分层设计（保持 Clean Architecture）

```
┌─────────────────────────────────────────────────────────────────┐
│ Frontend (React + ReactFlow)                                    │
│  ├─ DagEditor（画布编辑器）                                       │
│  ├─ DagExecutionViewer（执行时实时状态）                           │
│  └─ SignalR Client（接收实时事件）                                 │
└──────────────────────┬──────────────────────────────────────────┘
                       │ HTTP + SignalR
┌──────────────────────▼──────────────────────────────────────────┐
│ API Layer (CoreSRE)                                             │
│  ├─ WorkflowEndpoints（REST CRUD + Execute）                     │
│  └─ WorkflowHub（SignalR Hub — 推送执行事件）                      │
└──────────────────────┬──────────────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────────────┐
│ Application Layer (CoreSRE.Application)                         │
│  ├─ Commands / Queries（不变）                                    │
│  ├─ IWorkflowExecutionNotifier（推送抽象）                        │
│  └─ WorkflowExpressionEngine（表达式求值）                        │
└──────────────────────┬──────────────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────────────┐
│ Domain Layer (CoreSRE.Domain)                                   │
│  ├─ WorkflowDefinition / WorkflowExecution（聚合根）              │
│  ├─ ExecutionContext（运行时数据容器 — 新增）                       │
│  ├─ WorkflowNodeVO + WorkflowEdgeVO（增强 Port 模型）             │
│  └─ NodeExecutionVO（增加 Input 记录）                            │
└──────────────────────┬──────────────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────────────┐
│ Infrastructure Layer (CoreSRE.Infrastructure)                   │
│  ├─ WorkflowEngine（重写 — 执行栈模型）                            │
│  ├─ WorkflowExpressionEvaluator（JsonPath + 模板表达式）          │
│  ├─ SignalRWorkflowNotifier（SignalR 推送实现）                    │
│  └─ WorkflowExecutionBackgroundService（并发消费）                │
└─────────────────────────────────────────────────────────────────┘
```

---

## 三、核心升级设计

### 3.1 数据流模型升级

#### 现状

```
Node A  ──string? lastOutput──▶  Node B  ──string? lastOutput──▶  Node C
```

- 一维线性传递，单个 JSON 字符串
- 无类型、无结构、无批量

#### 目标：Items 模型

借鉴 n8n 的 `INodeExecutionData[]`，引入结构化数据条目：

```csharp
// Domain/ValueObjects/WorkflowItemVO.cs — 新增
public sealed record WorkflowItemVO
{
    /// <summary>主 JSON 负载</summary>
    public JsonElement Json { get; init; }

    /// <summary>追踪来源：由哪个节点的第几条输出产生</summary>
    public ItemSourceVO? Source { get; init; }
}

public sealed record ItemSourceVO
{
    public string NodeId { get; init; } = string.Empty;
    public int OutputIndex { get; init; }
    public int ItemIndex { get; init; }
}
```

```csharp
// Domain/ValueObjects/PortDataVO.cs — 新增
/// <summary>
/// 端口数据：一个端口上的多条数据条目。
/// </summary>
public sealed record PortDataVO
{
    public List<WorkflowItemVO> Items { get; init; } = [];
}
```

#### 节点输入/输出模型

```csharp
// 节点执行的输入：按端口索引组织
// { "main": [ PortData(port0), PortData(port1) ] }
public sealed record NodeInputData
{
    /// <summary>按连接类型（main）和端口索引组织的输入数据</summary>
    public Dictionary<string, List<PortDataVO?>> Ports { get; init; } = new();

    /// <summary>获取主输入的第一个端口数据</summary>
    public PortDataVO? GetMainInput(int portIndex = 0)
        => Ports.GetValueOrDefault("main")?.ElementAtOrDefault(portIndex);
}

// 节点执行的输出：按输出端口索引组织
public sealed record NodeOutputData
{
    public Dictionary<string, List<PortDataVO?>> Ports { get; init; } = new();
}
```

#### 数据传播流程

```
Node A 执行完成，输出:
  main[0] = [ {json: {name:"Alice"}}, {json: {name:"Bob"}} ]

Edge: A.main[0] → B.main[0]

Node B 接收到输入:
  main[0] = [ {json: {name:"Alice"}}, {json: {name:"Bob"}} ]

Node B 可以:
  a) 对每个 item 逐条处理（默认行为）
  b) 对整个 items 数组批量处理（通过 Config 配置）
```

#### WorkflowNodeVO 增加端口定义

```csharp
public sealed record WorkflowNodeVO
{
    public string NodeId { get; init; } = string.Empty;
    public WorkflowNodeType NodeType { get; init; }
    public Guid? ReferenceId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? Config { get; init; }

    // ── 新增 ──
    /// <summary>输入端口数量（main 类型），默认 1</summary>
    public int InputCount { get; init; } = 1;

    /// <summary>输出端口数量（main 类型），默认 1。
    /// Condition 节点默认 2（true/false）。</summary>
    public int OutputCount { get; init; } = 1;
}
```

#### WorkflowEdgeVO 增加端口索引

```csharp
public sealed record WorkflowEdgeVO
{
    public string EdgeId { get; init; } = string.Empty;
    public string SourceNodeId { get; init; } = string.Empty;
    public string TargetNodeId { get; init; } = string.Empty;
    public WorkflowEdgeType EdgeType { get; init; }
    public string? Condition { get; init; }

    // ── 新增 ──
    /// <summary>源节点输出端口索引（默认 0）</summary>
    public int SourcePortIndex { get; init; } = 0;

    /// <summary>目标节点输入端口索引（默认 0）</summary>
    public int TargetPortIndex { get; init; } = 0;
}
```

---

### 3.2 执行引擎升级：执行栈模型

#### 现状

```
拓扑排序 → 按固定顺序逐个执行 → lastOutput 线性传递
```

#### 目标：执行栈 + 等待队列

借鉴 n8n 的 `processRunExecutionData`，核心数据结构：

```csharp
// Domain/ValueObjects/ExecutionContext.cs — 新增
public sealed class ExecutionContext
{
    /// <summary>待执行队列（栈语义，LIFO — 深度优先）</summary>
    public LinkedList<NodeExecutionTask> ExecutionStack { get; } = new();

    /// <summary>等待区：多输入节点等待所有端口数据到齐</summary>
    public Dictionary<string, WaitingNodeData> WaitingNodes { get; } = new();

    /// <summary>所有节点的运行结果：nodeId → List&lt;NodeRunResult&gt;（支持多次运行）</summary>
    public Dictionary<string, List<NodeRunResult>> RunData { get; } = new();

    /// <summary>按源节点名索引的连接</summary>
    public Dictionary<string, List<ResolvedEdge>> ConnectionsBySource { get; init; } = new();

    /// <summary>按目标节点名索引的连接</summary>
    public Dictionary<string, List<ResolvedEdge>> ConnectionsByTarget { get; init; } = new();
}

public sealed record NodeExecutionTask
{
    public WorkflowNodeVO Node { get; init; } = null!;
    public NodeInputData InputData { get; init; } = new();
    public NodeSourceInfo? Source { get; init; }
    public int RunIndex { get; init; }
}

public sealed record WaitingNodeData
{
    public int TotalInputPorts { get; init; }
    public Dictionary<int, PortDataVO?> ReceivedPorts { get; } = new();
    public Dictionary<int, NodeSourceInfo?> SourceInfo { get; } = new();
    public bool AllPortsReceived => ReceivedPorts.Count >= TotalInputPorts
        && ReceivedPorts.Values.All(v => v is not null);
}

public sealed record NodeRunResult
{
    public NodeOutputData OutputData { get; init; } = new();
    public DateTime StartedAt { get; init; }
    public DateTime CompletedAt { get; init; }
    public string? ErrorMessage { get; init; }
    public NodeExecutionStatus Status { get; init; }
}
```

#### 核心执行循环

```csharp
public async Task ExecuteAsync(WorkflowExecution execution, CancellationToken ct)
{
    var context = BuildExecutionContext(execution.GraphSnapshot);
    var startNode = FindStartNode(context);
    
    // 初始化：将起始节点压入执行栈
    context.ExecutionStack.AddFirst(new NodeExecutionTask
    {
        Node = startNode,
        InputData = BuildInitialInput(execution.Input),
        Source = null,
        RunIndex = 0
    });
    
    execution.Start();
    await _repo.SaveAsync(execution, ct);
    
    string? lastNodeExecuted = null;
    
    while (context.ExecutionStack.Count > 0)
    {
        ct.ThrowIfCancellationRequested();
        
        // 1. 取出栈首
        var task = context.ExecutionStack.First!.Value;
        context.ExecutionStack.RemoveFirst();
        
        var node = task.Node;
        var runIndex = context.RunData.GetValueOrDefault(node.NodeId)?.Count ?? 0;
        
        // 2. 无限循环检测
        var executionKey = $"{node.NodeId}:{runIndex}";
        // ...
        
        // 3. 通知前端：节点开始执行
        execution.StartNode(node.NodeId);
        await _repo.SaveAsync(execution, ct);
        await _notifier.NodeExecutionStarted(execution.Id, node.NodeId);
        
        try
        {
            // 4. 执行节点
            var output = await ExecuteNodeAsync(execution, node, task.InputData, ct);
            
            // 5. 记录结果
            var result = new NodeRunResult { ... };
            context.RunData.GetOrAdd(node.NodeId).Add(result);
            execution.CompleteNode(node.NodeId, SerializeOutput(output));
            await _repo.SaveAsync(execution, ct);
            
            // 6. 通知前端：节点完成
            await _notifier.NodeExecutionCompleted(execution.Id, node.NodeId, output);
            
            // 7. ★ 传播数据给下游节点
            PropagateData(context, node, output, runIndex);
        }
        catch (Exception ex)
        {
            // 8. 错误处理
            await HandleNodeError(context, execution, node, ex, ct);
        }
        
        // 9. 若栈为空但等待队列有节点 → 尝试提升
        if (context.ExecutionStack.Count == 0)
        {
            PromoteWaitingNodes(context);
        }
        
        lastNodeExecuted = node.NodeId;
    }
    
    // 完成
    execution.Complete(BuildFinalOutput(context, lastNodeExecuted));
    await _repo.SaveAsync(execution, ct);
    await _notifier.ExecutionCompleted(execution.Id);
}
```

#### 数据传播方法

```csharp
private void PropagateData(ExecutionContext ctx, WorkflowNodeVO finishedNode,
    NodeOutputData output, int runIndex)
{
    if (!ctx.ConnectionsBySource.TryGetValue(finishedNode.NodeId, out var edges))
        return;
    
    foreach (var edge in edges)
    {
        var targetNode = ctx.Graph.Nodes.First(n => n.NodeId == edge.TargetNodeId);
        var outputPortData = output.Ports.GetValueOrDefault("main")
            ?.ElementAtOrDefault(edge.SourcePortIndex);
        
        int targetInputCount = ctx.ConnectionsByTarget
            .GetValueOrDefault(edge.TargetNodeId)?.Count ?? 1;
        
        if (targetInputCount <= 1)
        {
            // 单输入节点 → 直接入栈
            ctx.ExecutionStack.AddFirst(new NodeExecutionTask
            {
                Node = targetNode,
                InputData = new NodeInputData
                {
                    Ports = new() { ["main"] = [outputPortData] }
                },
                Source = new NodeSourceInfo { ... },
                RunIndex = 0
            });
        }
        else
        {
            // 多输入节点 → 放入等待区
            if (!ctx.WaitingNodes.TryGetValue(edge.TargetNodeId, out var waiting))
            {
                waiting = new WaitingNodeData { TotalInputPorts = targetInputCount };
                ctx.WaitingNodes[edge.TargetNodeId] = waiting;
            }
            waiting.ReceivedPorts[edge.TargetPortIndex] = outputPortData;
            waiting.SourceInfo[edge.TargetPortIndex] = new NodeSourceInfo { ... };
            
            // 检查是否所有端口都已到齐
            if (waiting.AllPortsReceived)
            {
                var inputData = new NodeInputData
                {
                    Ports = new() { ["main"] = Enumerable.Range(0, targetInputCount)
                        .Select(i => waiting.ReceivedPorts.GetValueOrDefault(i))
                        .ToList() }
                };
                ctx.ExecutionStack.AddFirst(new NodeExecutionTask
                {
                    Node = targetNode,
                    InputData = inputData,
                    RunIndex = 0
                });
                ctx.WaitingNodes.Remove(edge.TargetNodeId);
            }
        }
    }
}
```

#### 与现有代码的对应关系

| 现有代码 | 升级后 |
|---------|-------|
| `TopologicalSort()` → 按固定顺序 | 执行栈 → 动态决定下一个 |
| `string? lastOutput` 线性传递 | `NodeOutputData` → `PropagateData()` → 按边索引派发 |
| `ExecuteFanOutGroupAsync` + `Task.WhenAll` | 普通多输出节点 + 多条边 → 多个 task 入栈（不再需要专门的 FanOut 类型） |
| `ExecuteFanInAsync` 聚合上游 | 多输入节点 + `WaitingNodes` 自动汇聚（不再需要专门的 FanIn 类型） |
| `ExecuteConditionNodeAsync` 手动路由 | Condition 节点返回 `OutputCount=2` 的 `NodeOutputData`，edge 连到不同 port → 自然路由 |

---

### 3.3 表达式引擎

#### 目标

允许节点 Config 中使用表达式引用上游节点的输出数据：

```
{{ $node["AgentA"].json.analysis }}
{{ $input.json.severity }}
{{ $input.items.length }}
```

#### 设计

```csharp
// Application/Interfaces/IExpressionEvaluator.cs
public interface IExpressionEvaluator
{
    /// <summary>
    /// 解析字符串中的 {{ ... }} 表达式，替换为运行时数据。
    /// </summary>
    string Evaluate(string template, ExpressionContext context);
}

public sealed record ExpressionContext
{
    /// <summary>所有已执行节点的输出数据</summary>
    public Dictionary<string, List<NodeRunResult>> RunData { get; init; } = new();

    /// <summary>当前节点的输入数据</summary>
    public NodeInputData CurrentInput { get; init; } = new();

    /// <summary>当前处理的 item 索引</summary>
    public int CurrentItemIndex { get; init; }
}
```

#### 内置变量

| 变量 | 含义 |
|------|------|
| `$input` | 当前节点的输入数据 |
| `$input.json` | 当前 item 的 JSON |
| `$input.items` | 主输入端口的所有 items |
| `$node["NodeId"]` | 引用指定节点的最近一次输出 |
| `$node["NodeId"].json` | 该节点第一个 item 的 JSON |
| `$node["NodeId"].items` | 该节点的所有输出 items |
| `$execution.id` | 当前执行 ID |

#### 实现方案

使用 **JsonPath + 模板替换**：

```csharp
// Infrastructure/Services/WorkflowExpressionEvaluator.cs
public sealed class WorkflowExpressionEvaluator : IExpressionEvaluator
{
    private static readonly Regex TemplatePattern = new(@"\{\{\s*(.+?)\s*\}\}", RegexOptions.Compiled);

    public string Evaluate(string template, ExpressionContext context)
    {
        return TemplatePattern.Replace(template, match =>
        {
            var expression = match.Groups[1].Value.Trim();
            return ResolveExpression(expression, context);
        });
    }

    private string ResolveExpression(string expr, ExpressionContext ctx)
    {
        // $input.json.fieldName → 从当前输入的当前 item 取 JSON 字段
        // $node["NodeId"].json.fieldName → 从指定节点的输出取 JSON 字段
        // 实现：解析表达式 → 定位 JsonElement → JsonPath 查询 → 返回值
    }
}
```

#### ConditionEvaluator 升级

```csharp
// 升级前：仅 `$.path == "value"`
// 升级后：支持 ==, !=, >, <, >=, <=, contains, matches, exists
public enum ConditionOperator
{
    Equals,           // ==
    NotEquals,        // !=
    GreaterThan,      // >
    LessThan,         // <
    GreaterOrEqual,   // >=
    LessOrEqual,      // <=
    Contains,         // contains
    Matches,          // 正则 matches
    Exists,           // 字段存在性检查
}

// 同时支持表达式求值：
// 条件表达式可使用 {{ }} 模板引用其他节点数据
// 例: {{ $input.json.severity }} == "critical"
```

---

### 3.4 部分执行

#### 场景

用户在编辑器中修改了 Node C 的配置，想只重新执行 C 及其下游，而不重跑整个工作流。

#### 设计

```csharp
// Application/Workflows/Commands/PartialExecuteWorkflowCommand.cs
public sealed record PartialExecuteWorkflowCommand(
    Guid WorkflowDefinitionId,
    string TargetNodeId,          // 要执行到的目标节点
    string[]? DirtyNodeIds,       // 脏节点（配置变更的节点）
    JsonElement? Input
);
```

#### 算法

```
1. 载入上次执行的 RunData
2. 从 TargetNode 反向遍历找到子图
3. 找到子图中的"起始节点"：
   - DirtyNodes（参数变更的节点）
   - 没有 RunData 的节点
   - Target 节点本身
4. 从 RunData 中清除起始节点及下游的旧数据
5. 重建执行栈：
   - 起始节点的输入从上游已有的 RunData 中获取
   - 将起始节点压入执行栈
6. 执行 — 与全量执行共用同一个执行循环
```

#### API

```
POST /api/workflows/{id}/execute-partial
{
  "targetNodeId": "agent-b",
  "dirtyNodeIds": ["agent-b"],
  "input": {}
}
→ 202 Accepted { executionId, ... }
```

#### 前端交互

- 用户修改节点参数后，该节点及其下游显示"脏"标记（黄色虚线边框）
- 右键节点 → "Execute from here" / "执行到此节点"
- 使用上次执行的 RunData 作为上游输入，只执行子图

---

### 3.5 实时推送（SignalR）

#### 架构

```
WorkflowEngine
    │  执行到每个节点前后
    ▼
IWorkflowExecutionNotifier（接口）
    │
    ▼
SignalRWorkflowNotifier（实现）
    │
    ▼
WorkflowHub（SignalR Hub）
    │  WebSocket
    ▼
Frontend SignalR Client
    │
    ▼
DagExecutionViewer 实时更新节点颜色/状态
```

#### 事件定义

```csharp
// Application/Interfaces/IWorkflowExecutionNotifier.cs
public interface IWorkflowExecutionNotifier
{
    Task ExecutionStarted(Guid executionId);
    Task NodeExecutionStarted(Guid executionId, string nodeId);
    Task NodeExecutionCompleted(Guid executionId, string nodeId, NodeOutputData? output);
    Task NodeExecutionFailed(Guid executionId, string nodeId, string error);
    Task NodeExecutionSkipped(Guid executionId, string nodeId);
    Task ExecutionCompleted(Guid executionId);
    Task ExecutionFailed(Guid executionId, string error);
}
```

#### SignalR Hub

```csharp
// CoreSRE/Hubs/WorkflowHub.cs
[Authorize]
public sealed class WorkflowHub : Hub
{
    // 客户端加入执行的观察组
    public Task JoinExecution(Guid executionId)
        => Groups.AddToGroupAsync(Context.ConnectionId, $"execution:{executionId}");

    public Task LeaveExecution(Guid executionId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"execution:{executionId}");
}
```

#### 前端事件监听

```typescript
// hooks/useWorkflowExecution.ts
const connection = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/workflow")
  .build();

connection.on("NodeExecutionStarted", (executionId, nodeId) => {
  // 将节点标记为 running（蓝色旋转动画）
  setNodeStatus(nodeId, "running");
});

connection.on("NodeExecutionCompleted", (executionId, nodeId, output) => {
  // 将节点标记为 success（绿色）
  setNodeStatus(nodeId, "success");
  setNodeOutput(nodeId, output);
});

connection.on("NodeExecutionFailed", (executionId, nodeId, error) => {
  // 将节点标记为 error（红色）
  setNodeStatus(nodeId, "error");
  setNodeError(nodeId, error);
});
```

---

### 3.6 前端升级

#### DagExecutionViewer 增强

| 功能 | 现状 | 目标 |
|------|------|------|
| 节点状态显示 | 刷新页面后静态着色 | SignalR 实时动画更新 |
| 节点输出预览 | 无 | 点击节点显示输出数据面板 |
| 数据流可视化 | 无 | 边上显示数据条目数量 badge |
| 部分执行 | 无 | 右键菜单 "Execute from here" |
| 脏节点标记 | 无 | 修改参数后节点虚线边框 |

#### DagEditor 增强

| 功能 | 现状 | 目标 |
|------|------|------|
| 端口模型 | 每个节点 1 入 1 出 | 可配置端口数量，Condition 有 true/false 两个输出 |
| 节点 Config 编辑 | 有 NodePropertyPanel | 增加表达式编辑支持（语法高亮、自动补全上游节点名） |
| 连接校验 | 无 | 类型不匹配时拒绝连接 |

#### 新增组件

| 组件 | 职责 |
|------|------|
| `NodeOutputPanel.tsx` | 执行完成后点击节点显示 Items 数据表格 |
| `ExpressionInput.tsx` | 带 `{{ }}` 语法高亮的表达式输入框 |
| `ExecutionLogPanel.tsx` | 实时执行日志（SignalR 事件流） |

---

### 3.7 错误处理升级

#### 当前问题

- FanOut 任一分支失败 → 整个工作流失败
- 没有 continueOnFail
- 条件无匹配 → 工作流失败

#### 升级设计

##### 节点级错误策略

在 `WorkflowNodeVO.Config` 中增加错误处理配置：

```json
{
  "onError": "stop" | "continueWithEmpty" | "continueWithError",
  "maxRetries": 0,
  "retryDelayMs": 1000
}
```

| 策略 | 行为 |
|------|------|
| `stop`（默认） | 节点失败 → 整个工作流失败 |
| `continueWithEmpty` | 节点失败 → 输出空 Items，下游继续 |
| `continueWithError` | 节点失败 → 输出包含 error 字段的 Item，下游继续 |

##### Condition 默认分支

```csharp
// Condition 节点输出端口:
// port 0 = true 分支（条件匹配时）
// port 1 = false 分支（条件不匹配时，即 else）

// 如果没有连接 port 1（else），则条件不匹配时数据被丢弃（而非失败）
```

##### 重试机制

```csharp
for (int attempt = 0; attempt <= maxRetries; attempt++)
{
    try
    {
        output = await ExecuteNodeAsync(...);
        break;
    }
    catch when (attempt < maxRetries)
    {
        await Task.Delay(retryDelayMs * (attempt + 1), ct); // 线性退避
        await _notifier.NodeRetrying(executionId, nodeId, attempt + 1);
    }
}
```

---

### 3.8 数据追踪（Lineage / Paired Item）

#### 目标

用户在执行结果中点击某条输出数据时，能追踪到这条数据是由哪些上游数据经过哪些节点加工而来的。

#### 设计

每个 `WorkflowItemVO` 携带 `Source` 信息：

```csharp
public sealed record ItemSourceVO
{
    /// <summary>产生此 item 的源节点 ID</summary>
    public string NodeId { get; init; } = string.Empty;

    /// <summary>源节点的第几次运行</summary>
    public int RunIndex { get; init; }

    /// <summary>源节点输出的第几个端口</summary>
    public int OutputIndex { get; init; }

    /// <summary>源节点输出中的第几条 item</summary>
    public int ItemIndex { get; init; }
}
```

#### 追踪链

```
最终输出 Item X
  ├─ source: { nodeId: "NodeC", runIndex: 0, outputIndex: 0, itemIndex: 2 }
  │   └─ NodeC.input[0].items[2]
  │       ├─ source: { nodeId: "NodeB", runIndex: 0, outputIndex: 0, itemIndex: 2 }
  │       │   └─ NodeB.input[0].items[2]
  │       │       └─ source: { nodeId: "NodeA", runIndex: 0, outputIndex: 0, itemIndex: 2 }
```

#### 持久化

`NodeExecutionVO` 升级：

```csharp
public sealed record NodeExecutionVO
{
    public string NodeId { get; init; } = string.Empty;
    public NodeExecutionStatus Status { get; init; }
    public string? Input { get; init; }          // ← 现已写入
    public string? Output { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }

    // ── 新增 ──
    public int RunIndex { get; init; }
    public int ItemCount { get; init; }           // 输出的 item 数量
}
```

#### 前端展示

- 点击节点 → 打开 `NodeOutputPanel`
- 显示 Items 表格（每行是一条 `WorkflowItemVO`）
- 点击某行 → 高亮上游节点和对应的源 Item
- 连接线上显示数据流量 badge（"3 items"）

---

## 四、数据库 Schema 变更

### 新增/修改字段

```sql
-- workflow_nodes (在 graph JSONB 中)
ALTER TABLE ... -- 无需改表结构，因为 Graph 是 JSONB
-- 但 JSONB 内的 node 对象增加:
--   "inputCount": 1,
--   "outputCount": 1

-- workflow_edges (在 graph JSONB 中)
-- edge 对象增加:
--   "sourcePortIndex": 0,
--   "targetPortIndex": 0

-- node_executions (在 workflow_executions 的 JSONB 中)
-- 增加:
--   "runIndex": 0,
--   "itemCount": 0,
--   "input": "..." (现在会写入)
```

因为 Graph 和 NodeExecutions 都是 JSONB 存储，**不需要执行 SQL 迁移**，只需确保新字段有默认值。

---

## 五、向后兼容策略

| 关注点 | 策略 |
|--------|------|
| 现有工作流定义 | `InputCount` / `OutputCount` 默认 1，`SourcePortIndex` / `TargetPortIndex` 默认 0 — 旧数据自动兼容 |
| 现有执行记录 | 旧记录没有 `RunIndex` / `ItemCount` 字段，反序列化时用默认值 0 |
| 前端 API | 所有新字段可选，前端渐进式升级 |
| FanOut/FanIn 节点类型 | 保留但标记 deprecated — 新工作流通过多端口 + 边自然实现 |
| Condition 节点 | 升级为 2 个输出端口（true/false），旧的单输出 Condition 仍兼容 |

---

## 六、实施路线图

### Phase 1 — 「能跑起来」（1 周）

| 任务 | 说明 |
|------|------|
| P1-1 | ~~删除 BoxLite 依赖~~（✅ 已完成） |
| P1-2 | 修复 `NodeExecutionVO.Input` 写入 — 在 `WorkflowEngine` 中每次执行节点前记录输入 |
| P1-3 | 修复 `WorkflowExecutionDto` 缺失 `GraphSnapshot` 映射 |
| P1-4 | 增加 Mock Agent 执行模式（无 LLM 时返回模拟响应） |
| P1-5 | 端到端测试：创建 → 发布 → 执行 → 查看结果 |

### Phase 2 — 数据流模型升级（2 周）

| 任务 | 说明 |
|------|------|
| P2-1 | 新增 `WorkflowItemVO`、`PortDataVO`、`NodeInputData`、`NodeOutputData` 值对象 |
| P2-2 | `WorkflowNodeVO` 增加 `InputCount` / `OutputCount` |
| P2-3 | `WorkflowEdgeVO` 增加 `SourcePortIndex` / `TargetPortIndex` |
| P2-4 | 重写 `WorkflowEngine` — 执行栈 + 等待队列模型 |
| P2-5 | 统一 FanOut/FanIn 为多端口自然路由 |
| P2-6 | 更新所有 DTO 和前端类型 |

### Phase 3 — 实时推送（1 周）

| 任务 | 说明 |
|------|------|
| P3-1 | 添加 `Microsoft.AspNetCore.SignalR` 依赖 |
| P3-2 | 实现 `IWorkflowExecutionNotifier` + `SignalRWorkflowNotifier` |
| P3-3 | 创建 `WorkflowHub` |
| P3-4 | 前端集成 `@microsoft/signalr` |
| P3-5 | `DagExecutionViewer` 实时状态更新 |

### Phase 4 — 表达式引擎 + 错误处理（2 周）

| 任务 | 说明 |
|------|------|
| P4-1 | 实现 `WorkflowExpressionEvaluator`（`{{ }}` 模板 + `$input` / `$node` 变量） |
| P4-2 | 升级 `ConditionEvaluator`（支持 `!=`, `>`, `<`, `contains` 等） |
| P4-3 | 节点 Config 支持表达式（执行前求值） |
| P4-4 | 实现错误策略（`stop` / `continueWithEmpty` / `continueWithError`） |
| P4-5 | 实现重试机制（`maxRetries` + 退避） |
| P4-6 | Condition 默认分支（else 端口） |

### Phase 5 — 部分执行 + 数据追踪（2 周）

| 任务 | 说明 |
|------|------|
| P5-1 | 实现 `PartialExecuteWorkflowCommand` + API |
| P5-2 | 子图提取算法：从目标节点反向遍历 |
| P5-3 | 脏节点检测 + RunData 缓存复用 |
| P5-4 | `ItemSourceVO` 追踪链实现 |
| P5-5 | 前端脏节点标记 + "Execute from here" |
| P5-6 | `NodeOutputPanel` 数据查看 + 源追踪高亮 |

### Phase 6 — 前端完善（1 周）

| 任务 | 说明 |
|------|------|
| P6-1 | 多端口节点 ReactFlow 渲染（Handle 数量动态） |
| P6-2 | `ExpressionInput` 组件（语法高亮） |
| P6-3 | `ExecutionLogPanel`（实时日志流） |
| P6-4 | 边上 Items 数量 badge |
| P6-5 | 并发执行支持（BackgroundService → `SemaphoreSlim` 控制并发度） |

---

## 七、技术决策记录

| 决策 | 选择 | 理由 |
|------|------|------|
| 实时推送 | SignalR（而非 SSE） | .NET 原生支持，自动降级 WebSocket → Long Polling，比 SSE 更适合双向通信 |
| 前端画布 | 保持 ReactFlow（而非迁移 Vue Flow） | 项目已用 React，ReactFlow 功能等同 Vue Flow |
| 表达式引擎 | 自研简版（而非嵌入 JS 引擎） | 避免引入 V8/Jint 依赖，`{{ }}` 模板 + JsonPath 覆盖 90% 场景 |
| 执行栈 vs 拓扑排序 | 执行栈 | 支持动态路由、暂停/恢复、部分执行，n8n 验证过的模式 |
| FanOut/FanIn 节点 | 保留但可被多端口替代 | 向后兼容，新工作流推荐用多端口 |
| 数据 Schema | JSONB 内扩展 | 不需要 SQL 迁移，JSONB 天然支持字段新增 |
