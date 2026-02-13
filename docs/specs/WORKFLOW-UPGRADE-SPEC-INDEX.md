# CoreSRE — 工作流引擎升级 Spec 总览

**文档编号**: WORKFLOW-UPGRADE-SPEC-INDEX  
**版本**: 1.0.0  
**创建日期**: 2026-02-12  
**关联文档**: [SPEC-INDEX](SPEC-INDEX.md) | [Workflow-Engine-Upgrade-Design](../Workflow-Engine-Upgrade-Design.md)  

> 本文档将工作流引擎升级设计报告拆分为可独立交付的 Spec 清单。  
> 升级目标：将 CoreSRE 工作流从「能编译但跑不通」升级为 n8n 级别的可用工作流引擎。  
> 原则：渐进式升级，保持 C# / .NET 技术栈和现有 Clean Architecture 分层。  
> 前置依赖：SPEC-020（工作流定义 CRUD）、SPEC-021（工作流执行引擎）、SPEC-063/064（工作流前端）均已完成。

---

## 现存缺陷与 Spec 覆盖矩阵

| 缺陷编号 | 缺陷描述 | 覆盖 Spec |
|----------|---------|----------|
| D1 | 节点间数据模型太弱（仅 `string? lastOutput` 线性传递） | SPEC-081 |
| D2 | 执行模型太僵（一次性拓扑排序，固定顺序执行） | SPEC-081 |
| D3 | 无实时推送（前端只能刷新页面） | SPEC-082 |
| D4 | 无表达式引擎（条件仅支持 `==`） | SPEC-083 |
| D5 | `NodeExecutionVO.Input` 从未写入 | SPEC-080 |
| D6 | Config 字段被忽略 | SPEC-083 |
| D7 | 错误处理一刀切（FanOut 任一分支失败 → 全部失败） | SPEC-083 |
| D8 | Agent 调用无状态（无对话历史） | SPEC-080 |
| D9 | Channel 串行消费（同一时刻只跑 1 个工作流） | SPEC-085 |
| D10 | FanIn 聚合数据给后续节点困难 | SPEC-081 |

---

## 模块 M3-Upgrade：工作流引擎升级

### SPEC-080: 工作流引擎基础修复

**优先级**: P1（Phase 1 — 1 周）  
**修复缺陷**: D5, D8  
**前置依赖**: SPEC-021（现有执行引擎）  

**简述**: 修复现有工作流引擎中「能编译但跑不通」的基础缺陷，使当前引擎在原有架构下达到可用状态。这是后续所有升级的前提——不先跑通现有流程，无法验证后续改进。

**核心工作**:

| 任务 | 说明 | 涉及层 |
|------|------|--------|
| P1-1 | ~~删除 BoxLite 依赖~~（✅ 已完成） | Infrastructure |
| P1-2 | 修复 `NodeExecutionVO.Input` 写入 — 在 `WorkflowEngine` 中每次执行节点前将输入数据序列化到 `Input` 字段 | Infrastructure |
| P1-3 | 修复 `WorkflowExecutionDto` 缺失 `GraphSnapshot` 映射 — 确保执行详情 API 返回完整的 DAG 快照 | Application |
| P1-4 | 增加 Mock Agent 执行模式 — 无 LLM 时返回模拟响应，降低开发调试门槛 | Application / Infrastructure |
| P1-5 | 端到端冒烟测试 — 创建 → 发布 → 执行 → 查看结果的完整流程验证 | Tests |

**领域模型变更**: 
- `NodeExecutionVO.Input` — 现有字段，确保写入时序列化当前节点的实际输入数据

**端点变更**: 无新端点，修复现有端点行为  

**验收标准**:
1. **Given** 一个 Published 工作流包含 3 个顺序 Agent 节点，**When** 提交执行请求，**Then** 执行完成后每个 `NodeExecutionVO` 的 `Input` 字段均有值（JSON 字符串），可追溯每个节点收到的实际输入。
2. **Given** 一次完成的工作流执行，**When** 通过 `GET /api/workflows/{id}/executions/{execId}` 查询详情，**Then** 返回的 DTO 包含完整的 `GraphSnapshot`（节点列表 + 边列表）。
3. **Given** 系统未配置任何 LLM Provider，**When** 工作流引用的 Agent 使用 Mock 模式执行，**Then** Agent 节点返回模拟响应（包含节点名称和输入摘要），工作流正常完成。
4. **Given** 完整的端到端流程，**When** 依次执行创建工作流 → 发布 → 执行 → 查询执行详情，**Then** 所有步骤成功完成，执行状态为 Completed。

---

### SPEC-081: 工作流数据流模型与执行栈引擎

**优先级**: P1（Phase 2 — 2 周）  
**修复缺陷**: D1, D2, D10  
**前置依赖**: SPEC-080（基础修复）  

**简述**: 这是整个升级的核心。将节点间数据传递从单一 `string? lastOutput` 线性传递，升级为结构化的 Items 数据模型（借鉴 n8n 的 `INodeExecutionData[]`）。同时将执行引擎从「拓扑排序 → 固定顺序执行」重写为「执行栈 + 等待队列」模型，支持动态路由、多端口输入输出、自然的 FanOut/FanIn 行为。

**核心工作**:

| 任务 | 说明 | 涉及层 |
|------|------|--------|
| P2-1 | 新增 `WorkflowItemVO`、`PortDataVO`、`NodeInputData`、`NodeOutputData` 值对象 | Domain |
| P2-2 | `WorkflowNodeVO` 增加 `InputCount` / `OutputCount` 端口定义 | Domain |
| P2-3 | `WorkflowEdgeVO` 增加 `SourcePortIndex` / `TargetPortIndex` 端口索引 | Domain |
| P2-4 | 新增 `ExecutionContext`（执行栈 + 等待队列 + RunData）| Domain |
| P2-5 | 重写 `WorkflowEngine` — 执行栈循环 + `PropagateData()` 数据传播 | Infrastructure |
| P2-6 | 统一 FanOut/FanIn 为多端口自然路由（保留旧类型但标记 deprecated）| Infrastructure |
| P2-7 | 更新所有 DTO 映射和 API 契约 | Application |
| P2-8 | 前端类型定义同步更新 | Frontend |

**领域模型（新增）**:

```csharp
// 数据条目
WorkflowItemVO { Json: JsonElement, Source: ItemSourceVO? }
ItemSourceVO { NodeId, RunIndex, OutputIndex, ItemIndex }

// 端口数据
PortDataVO { Items: List<WorkflowItemVO> }

// 节点输入/输出（按端口索引组织）
NodeInputData { Ports: Dictionary<string, List<PortDataVO?>> }
NodeOutputData { Ports: Dictionary<string, List<PortDataVO?>> }

// 执行上下文
ExecutionContext { ExecutionStack, WaitingNodes, RunData, ConnectionsBySource, ConnectionsByTarget }
NodeExecutionTask { Node, InputData, Source, RunIndex }
WaitingNodeData { TotalInputPorts, ReceivedPorts, AllPortsReceived }
NodeRunResult { OutputData, StartedAt, CompletedAt, ErrorMessage, Status }
```

**领域模型（修改）**:

```csharp
// WorkflowNodeVO 增加:
InputCount: int = 1        // 输入端口数量（main 类型）
OutputCount: int = 1       // 输出端口数量（Condition 默认 2）

// WorkflowEdgeVO 增加:
SourcePortIndex: int = 0   // 源节点输出端口索引
TargetPortIndex: int = 0   // 目标节点输入端口索引
```

**执行引擎核心算法**:

```
1. 从 GraphSnapshot 构建 ExecutionContext（连接索引、节点映射）
2. 将起始节点 + 初始输入压入 ExecutionStack
3. 主循环 — while (ExecutionStack.Count > 0):
   a. 取出栈首任务
   b. 通知前端：节点开始
   c. 执行节点（调用 Agent/Tool/Condition 等）
   d. 记录 RunData
   e. 通知前端：节点完成
   f. PropagateData()：
      - 遍历该节点的所有下游边
      - 单输入下游节点 → 直接压入执行栈
      - 多输入下游节点 → 放入等待区
      - 等待区数据到齐 → 提升至执行栈
   g. 若栈为空 → 检查等待区是否有可提升的节点
4. 所有节点执行完毕 → 记录最终输出
```

**向后兼容策略**:
- `InputCount` / `OutputCount` 默认 1 → 旧工作流 JSONB 无需迁移
- `SourcePortIndex` / `TargetPortIndex` 默认 0 → 旧边定义兼容
- FanOut/FanIn 节点类型保留但标记 `[Obsolete]`，新工作流推荐用多端口

**端点变更**: 
- 现有 CRUD 端点无变化（新字段可选，有默认值）
- 输出 DTO 增加端口数据（前端渐进升级）

**数据库变更**: 
- 无 SQL 迁移（Graph 和 NodeExecutions 均为 JSONB，新字段有默认值自动兼容）

**验收标准**:
1. **Given** 一个旧格式的工作流定义（无 InputCount/OutputCount/PortIndex 字段），**When** 通过升级后的引擎执行，**Then** 行为与升级前一致（所有新字段自动使用默认值）。
2. **Given** 一个 3 节点顺序工作流 A → B → C，**When** 执行完成后查看 RunData，**Then** 每个节点的输入/输出均为结构化的 `NodeInputData` / `NodeOutputData`，包含 `PortDataVO` 和 `WorkflowItemVO`。
3. **Given** 一个 FanOut 工作流（1 个节点同时连接 3 个下游节点的 port 0），**When** 执行时，**Then** 3 个下游节点并行入栈执行（不再依赖专门的 FanOut 节点类型）。
4. **Given** 一个 FanIn 场景（3 个节点分别连接到同一个下游节点的 port 0/1/2），**When** 前两个上游完成但第三个未完成，**Then** 下游节点在等待区中等待；当第三个上游完成后，下游节点自动提升至执行栈。
5. **Given** 一个 Condition 节点配置了 OutputCount=2（true/false），**When** 条件为 true，**Then** 数据从 port 0 输出，仅 port 0 连接的下游节点被压入执行栈。

---

### SPEC-082: 工作流实时推送（SignalR）

**优先级**: P1（Phase 3 — 1 周）  
**修复缺陷**: D3  
**前置依赖**: SPEC-081（数据流模型，执行栈中的 Notifier 钩子）  

**简述**: 为工作流执行过程引入 SignalR 实时推送能力。执行引擎在节点执行前后通过 `IWorkflowExecutionNotifier` 接口发送事件，`SignalRWorkflowNotifier` 实现将事件推送到前端观察者。前端 `DagExecutionViewer` 接收事件后实时更新节点颜色和状态动画，无需刷新页面。

**核心工作**:

| 任务 | 说明 | 涉及层 |
|------|------|--------|
| P3-1 | 添加 `Microsoft.AspNetCore.SignalR` NuGet 依赖 | API |
| P3-2 | 定义 `IWorkflowExecutionNotifier` 接口（7 个事件方法） | Application |
| P3-3 | 实现 `SignalRWorkflowNotifier`（通过 `IHubContext<WorkflowHub>` 推送） | Infrastructure |
| P3-4 | 创建 `WorkflowHub`（Join/Leave 执行观察组） | API |
| P3-5 | 前端集成 `@microsoft/signalr` 客户端 | Frontend |
| P3-6 | `DagExecutionViewer` 绑定 SignalR 事件，实时更新节点状态 | Frontend |

**接口定义**:

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

**SignalR Hub**:

```csharp
// CoreSRE/Hubs/WorkflowHub.cs
public sealed class WorkflowHub : Hub
{
    public Task JoinExecution(Guid executionId)
        => Groups.AddToGroupAsync(Context.ConnectionId, $"execution:{executionId}");
    public Task LeaveExecution(Guid executionId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"execution:{executionId}");
}
```

**前端事件流**:

```
WorkflowHub ─ WebSocket ─→ SignalR Client
  ├─ "ExecutionStarted"       → 整体状态 Running
  ├─ "NodeExecutionStarted"   → 节点蓝色旋转动画
  ├─ "NodeExecutionCompleted" → 节点绿色 ✓ + 输出数据缓存
  ├─ "NodeExecutionFailed"    → 节点红色 ✗ + 错误信息
  ├─ "NodeExecutionSkipped"   → 节点灰色虚线
  ├─ "ExecutionCompleted"     → 整体状态 Completed
  └─ "ExecutionFailed"        → 整体状态 Failed
```

**端点变更**:
- 新增 WebSocket 端点: `/hubs/workflow`（由 SignalR 自动管理）
- `Program.cs` 增加 `app.MapHub<WorkflowHub>("/hubs/workflow")`

**第三方库映射**:
- `Microsoft.AspNetCore.SignalR` → Hub 基类、Group 管理
- `Microsoft.AspNetCore.SignalR.Client` → 前端 .NET 客户端（可选）
- `@microsoft/signalr` → 前端 npm 包

**验收标准**:
1. **Given** 前端打开工作流执行页面并通过 SignalR 加入执行观察组，**When** 后端开始执行工作流，**Then** 前端立即收到 `ExecutionStarted` 事件，页面整体状态切换为 Running。
2. **Given** 工作流包含 3 个顺序节点，**When** 各节点依次执行，**Then** 前端依次收到每个节点的 `NodeExecutionStarted` → `NodeExecutionCompleted` 事件，节点颜色实时从灰色 → 蓝色（运行中） → 绿色（完成）过渡。
3. **Given** 某节点执行失败，**When** 前端收到 `NodeExecutionFailed` 事件，**Then** 该节点显示红色并展示错误信息，无需手动刷新。
4. **Given** 用户离开执行页面后重新进入，**When** 重新加入观察组，**Then** 当前执行状态通过 REST API fallback 加载（不依赖错过的 SignalR 事件）。

---

### SPEC-083: 表达式引擎与错误处理升级

**优先级**: P1（Phase 4 — 2 周）  
**修复缺陷**: D4, D6, D7  
**前置依赖**: SPEC-081（数据流模型，RunData 提供表达式数据源）  

**简述**: 为工作流节点引入模板表达式引擎，允许节点 Config 中通过 `{{ }}` 语法引用上游节点输出数据和运行时变量。同时升级条件评估器支持多种比较操作符，并引入节点级错误处理策略（停止/继续/重试）。

**核心工作**:

| 任务 | 说明 | 涉及层 |
|------|------|--------|
| P4-1 | 定义 `IExpressionEvaluator` 接口 + `ExpressionContext` | Application |
| P4-2 | 实现 `WorkflowExpressionEvaluator`（`{{ }}` 模板 + `$input` / `$node` 变量解析） | Infrastructure |
| P4-3 | 升级 `ConditionEvaluator` — 支持 `==`, `!=`, `>`, `<`, `>=`, `<=`, `contains`, `matches`, `exists` | Infrastructure |
| P4-4 | 节点 Config 表达式求值 — 执行前对 Config 中的 `{{ }}` 模板进行替换 | Infrastructure |
| P4-5 | 节点级错误策略（`onError`: `stop` / `continueWithEmpty` / `continueWithError`） | Infrastructure |
| P4-6 | 节点重试机制（`maxRetries` + 线性退避） | Infrastructure |
| P4-7 | Condition 默认分支（else 端口） — 条件不匹配时数据走 port 1 而非失败 | Infrastructure |

**表达式引擎设计**:

内置变量：

| 变量 | 含义 |
|------|------|
| `$input` | 当前节点的输入数据 |
| `$input.json` | 当前 item 的 JSON |
| `$input.items` | 主输入端口的所有 items |
| `$node["NodeId"]` | 引用指定节点的最近一次输出 |
| `$node["NodeId"].json` | 该节点第一个 item 的 JSON |
| `$node["NodeId"].items` | 该节点的所有输出 items |
| `$execution.id` | 当前执行 ID |

实现方案：`Regex` 匹配 `{{ }}` → 解析表达式路径 → 从 `ExpressionContext.RunData` 定位 `JsonElement` → `JsonPath` 查询 → 字符串替换。

**条件操作符**:

```csharp
public enum ConditionOperator
{
    Equals,        // ==
    NotEquals,     // !=
    GreaterThan,   // >
    LessThan,      // <
    GreaterOrEqual,// >=
    LessOrEqual,   // <=
    Contains,      // contains
    Matches,       // 正则 matches
    Exists,        // 字段存在性检查
}
```

**错误策略 Config**:

```json
{
  "onError": "stop | continueWithEmpty | continueWithError",
  "maxRetries": 0,
  "retryDelayMs": 1000
}
```

| 策略 | 行为 |
|------|------|
| `stop`（默认） | 节点失败 → 整个工作流失败 |
| `continueWithEmpty` | 节点失败 → 输出空 Items，下游继续 |
| `continueWithError` | 节点失败 → 输出包含 error 字段的 Item，下游继续 |

**接口定义**:

```csharp
public interface IExpressionEvaluator
{
    string Evaluate(string template, ExpressionContext context);
}

public sealed record ExpressionContext
{
    public Dictionary<string, List<NodeRunResult>> RunData { get; init; } = new();
    public NodeInputData CurrentInput { get; init; } = new();
    public int CurrentItemIndex { get; init; }
}
```

**端点变更**: 无新端点

**验收标准**:
1. **Given** 节点 B 的 Config 包含 `{{ $node["AgentA"].json.analysis }}`，**When** AgentA 执行完成输出 `{"analysis": "CPU 过载"}`，**Then** 节点 B 执行前 Config 中的模板被替换为 `"CPU 过载"`。
2. **Given** Condition 节点配置条件 `$input.json.severity > 3`，**When** 输入 `{"severity": 5}`，**Then** 条件为 true，数据从 port 0 输出。
3. **Given** Condition 节点配置条件 `$input.json.severity > 3`，**When** 输入 `{"severity": 1}`，**Then** 条件为 false，数据从 port 1（else 端口）输出（而非工作流失败）。
4. **Given** 节点 Config 配置 `"onError": "continueWithEmpty"`，**When** 该节点执行抛出异常，**Then** 节点状态标记为 Failed，但输出空 Items，下游节点继续执行，整体工作流不中断。
5. **Given** 节点 Config 配置 `"maxRetries": 2, "retryDelayMs": 500`，**When** 第一次执行失败，**Then** 系统等待 500ms 后重试第一次；若仍然失败再等待 1000ms 重试第二次；若第二次重试成功，节点状态为 Completed。
6. **Given** 条件表达式 `$input.json.tags contains "urgent"`，**When** 输入 `{"tags": ["urgent", "network"]}`，**Then** 条件匹配为 true。

---

### SPEC-084: 部分执行与数据追踪

**优先级**: P2（Phase 5 — 2 周）  
**前置依赖**: SPEC-081（执行栈模型 + RunData）、SPEC-083（表达式引擎）  

**简述**: 支持用户只重新执行工作流的某个子图（部分执行），以及追踪每条输出数据从源节点到当前节点的完整加工链路（数据谱系/Paired Item）。部分执行复用上次 RunData 中已有的结果，仅执行脏节点及其下游。数据追踪为每条 `WorkflowItemVO` 添加 `Source` 信息，支持前端点击某条输出时高亮其上游来源。

**核心工作**:

| 任务 | 说明 | 涉及层 |
|------|------|--------|
| P5-1 | 新增 `PartialExecuteWorkflowCommand` + Handler | Application |
| P5-2 | 子图提取算法：从目标节点反向遍历（BFS/DFS）找到执行子图 | Application / Infrastructure |
| P5-3 | 脏节点检测 + RunData 缓存复用 — 清除脏节点及下游旧数据，上游数据直接复用 | Infrastructure |
| P5-4 | `ItemSourceVO` 追踪链 — 每个 Item 携带 `Source` 信息（产自哪个节点/哪次运行/哪个端口/第几条） | Domain |
| P5-5 | `NodeExecutionVO` 增加 `RunIndex` 和 `ItemCount` — 持久化追踪信息 | Domain |
| P5-6 | 前端脏节点标记 + "Execute from here" 右键菜单 | Frontend |
| P5-7 | 前端 `NodeOutputPanel` 数据查看 + 源追踪高亮 | Frontend |

**部分执行算法**:

```
1. 载入上次执行的 RunData
2. 从 TargetNode 反向遍历找到子图（需经过的所有节点）
3. 确定子图中的"起始节点"：
   - DirtyNodes（参数变更的节点）
   - 没有 RunData 的节点
   - TargetNode 本身
4. 从 RunData 中清除起始节点及其下游的旧数据
5. 重建执行栈：
   - 起始节点的输入从上游已有 RunData 获取
   - 将起始节点压入执行栈
6. 执行 — 与全量执行共用同一个执行循环
```

**数据追踪链示例**:

```
最终输出 Item X
  └─ source: { nodeId: "NodeC", runIndex: 0, outputIndex: 0, itemIndex: 2 }
      └─ NodeC.input[0].items[2]
          └─ source: { nodeId: "NodeB", runIndex: 0, outputIndex: 0, itemIndex: 2 }
              └─ NodeB.input[0].items[2]
                  └─ source: { nodeId: "NodeA", runIndex: 0, outputIndex: 0, itemIndex: 2 }
```

**端点变更**:
- 新增 `POST /api/workflows/{id}/execute-partial` → 202 Accepted `{ executionId }`

请求体:
```json
{
  "targetNodeId": "agent-b",
  "dirtyNodeIds": ["agent-b"],
  "input": {}
}
```

**领域模型变更**:
- `NodeExecutionVO` 增加: `RunIndex: int`, `ItemCount: int`
- `WorkflowItemVO.Source` — 已在 SPEC-081 中定义，此处实现写入逻辑

**验收标准**:
1. **Given** 一个已成功执行过的 5 节点工作流 A→B→C→D→E，用户修改了节点 C 的 Config，**When** 用户通过 `POST /api/workflows/{id}/execute-partial` 指定 `targetNodeId="E"` 且 `dirtyNodeIds=["C"]`，**Then** 系统复用 A、B 的 RunData，只重新执行 C、D、E，最终输出反映 C 的新配置。
2. **Given** 部分执行请求中的 `targetNodeId` 不存在于工作流中，**When** 系统处理请求，**Then** 返回 400 错误。
3. **Given** 一个已执行的工作流，最终节点输出 5 条 Items，**When** 查看第 3 条 Item 的 `Source` 信息，**Then** 可沿追踪链逐步回溯到源节点和源 Item。
4. **Given** 前端工作流编辑器中用户修改了节点 C 的参数，**When** 编辑器检测到变更，**Then** 节点 C 及其下游节点（D、E）显示脏标记（黄色虚线边框），右键菜单出现"Execute from here"选项。
5. **Given** 前端执行完成后用户点击某个节点，**When** `NodeOutputPanel` 打开并展示该节点的输出 Items 表格，**Then** 用户点击某行 Item 后，上游贡献节点和对应连接线高亮显示。

---

### SPEC-085: 工作流前端升级与并发执行

**优先级**: P2（Phase 6 — 1 周）  
**修复缺陷**: D9  
**前置依赖**: SPEC-081（多端口模型）、SPEC-082（SignalR）、SPEC-083（表达式引擎）  

**简述**: 完成工作流前端的全面升级，包括多端口 ReactFlow 渲染、表达式输入组件、实时执行日志面板、边上数据流量 badge。同时解决后端并发执行瓶颈（D9），将 `BackgroundService` 从串行消费升级为 `SemaphoreSlim` 控制的并发消费。

**核心工作**:

| 任务 | 说明 | 涉及层 |
|------|------|--------|
| P6-1 | 多端口节点 ReactFlow 渲染 — Handle 数量动态化，Condition 节点显示 true/false 两个输出 Handle | Frontend |
| P6-2 | `ExpressionInput` 组件 — 带 `{{ }}` 语法高亮的输入框，自动补全上游节点名称 | Frontend |
| P6-3 | `ExecutionLogPanel` 组件 — 实时展示 SignalR 推送的执行事件日志流 | Frontend |
| P6-4 | 边上 Items 数量 badge — 执行完成后在连接线上显示传递的数据条目数 | Frontend |
| P6-5 | 并发执行 — `WorkflowExecutionBackgroundService` 使用 `SemaphoreSlim` 控制并发度 | Infrastructure |
| P6-6 | 连接校验 — 端口类型不匹配时拒绝连接 | Frontend |

**ReactFlow 多端口渲染**:

```
当前：每个节点 1 个 source Handle + 1 个 target Handle
目标：根据 InputCount / OutputCount 动态渲染多个 Handle
  - Agent 节点：1 入 1 出
  - Condition 节点：1 入 2 出（port 0 = true ✓, port 1 = false ✗）
  - FanIn 节点（或多输入节点）：N 入 1 出
  - Handle 上显示端口索引 label
```

**新增前端组件**:

| 组件 | 职责 |
|------|------|
| `ExpressionInput.tsx` | `{{ }}` 语法高亮输入框 + 上游节点名自动补全 |
| `ExecutionLogPanel.tsx` | 实时执行日志面板（时间线式展示 SignalR 事件） |
| `NodeOutputPanel.tsx` | 节点输出 Items 数据表格（在 SPEC-084 中也涉及） |

**并发执行设计**:

```csharp
// Infrastructure/Services/WorkflowExecutionBackgroundService.cs
// 升级前：while + Dequeue → 串行 1 个
// 升级后：SemaphoreSlim(maxConcurrency) + Task.Run
private readonly SemaphoreSlim _semaphore = new(maxConcurrency); // 默认 5

while (await _channel.Reader.WaitToReadAsync(ct))
{
    while (_channel.Reader.TryRead(out var command))
    {
        await _semaphore.WaitAsync(ct);
        _ = Task.Run(async () =>
        {
            try { await ExecuteWorkflow(command, ct); }
            finally { _semaphore.Release(); }
        }, ct);
    }
}
```

**端点变更**: 无

**验收标准**:
1. **Given** 一个 Condition 节点在 ReactFlow 画布上，**When** 渲染该节点，**Then** 显示 1 个输入 Handle 和 2 个输出 Handle（分别标记 ✓ 和 ✗），用户可分别从两个输出 Handle 拉出连接线到不同的下游节点。
2. **Given** 用户在节点 Config 编辑面板中输入 `{{ $node["`，**When** 触发自动补全，**Then** 下拉菜单显示当前工作流中所有上游节点的 NodeId 列表。
3. **Given** 前端打开执行监控页面，**When** 工作流执行过程中 SignalR 推送到达，**Then** `ExecutionLogPanel` 实时追加事件条目（时间戳 + 事件类型 + 节点名），无需手动刷新。
4. **Given** 一个已执行完成的工作流画布，**When** 边上的数据流量 badge 显示，**Then** 各连接线中间位置显示传递的 Items 数量（如 "3 items"）。
5. **Given** 后端同时有 8 个工作流待执行且 `maxConcurrency=5`，**When** `BackgroundService` 消费队列，**Then** 最多 5 个工作流并行执行，第 6~8 个等待信号量释放后执行。
6. **Given** 用户尝试从一个节点的 source Handle 拖拽连接到另一个节点的 source Handle（而非 target Handle），**When** 释放连接，**Then** 连接被拒绝，不会创建无效边。

---

## 实施路线图

```
Phase 1 (1 周) — SPEC-080: 基础修复
    修复 Input 写入 → GraphSnapshot 映射 → Mock Agent → 端到端验证
    
Phase 2 (2 周) — SPEC-081: 数据流 + 执行栈 ★ 核心
    新增值对象 → 端口定义 → ExecutionContext → 重写引擎 → 统一 FanOut/FanIn → 更新 DTO
    
Phase 3 (1 周) — SPEC-082: 实时推送
    SignalR 依赖 → Notifier 接口 → Hub → 前端集成 → 实时状态
    
Phase 4 (2 周) — SPEC-083: 表达式 + 错误处理
    表达式引擎 → 条件升级 → Config 求值 → 错误策略 → 重试 → Condition else
    
Phase 5 (2 周) — SPEC-084: 部分执行 + 数据追踪
    PartialExecute API → 子图提取 → 脏节点 → Source 追踪 → 前端 UI
    
Phase 6 (1 周) — SPEC-085: 前端 + 并发
    多端口渲染 → 表达式输入 → 执行日志 → 数据 badge → 并发消费 → 连接校验
```

**总工期**: 约 9 周

---

## 优先级总览

```
P1 (MVP 可用 — 必须完成)
  ├── SPEC-080: 工作流引擎基础修复（Phase 1, 1 周）
  ├── SPEC-081: 数据流模型与执行栈引擎（Phase 2, 2 周）★
  ├── SPEC-082: 工作流实时推送 SignalR（Phase 3, 1 周）
  └── SPEC-083: 表达式引擎与错误处理（Phase 4, 2 周）

P2 (增强功能 — 第二轮迭代)
  ├── SPEC-084: 部分执行与数据追踪（Phase 5, 2 周）
  └── SPEC-085: 前端升级与并发执行（Phase 6, 1 周）
```

---

## 技术决策记录

| 决策 | 选择 | 理由 |
|------|------|------|
| 实时推送 | SignalR（而非 SSE） | .NET 原生支持，自动降级 WebSocket → Long Polling |
| 表达式引擎 | 自研简版 `{{ }}` + JsonPath（而非嵌入 JS 引擎） | 避免 V8/Jint 依赖，覆盖 90% 场景 |
| 执行模型 | 执行栈（而非拓扑排序） | 支持动态路由、暂停/恢复、部分执行，n8n 验证过的模式 |
| FanOut/FanIn | 保留但可被多端口替代 | 向后兼容，新工作流推荐用多端口 |
| 数据 Schema | JSONB 内扩展（无 SQL 迁移） | 新字段有默认值，天然向后兼容 |
| 并发控制 | `SemaphoreSlim`（而非 Worker Pool） | 轻量级，适合 `BackgroundService` 场景 |

---

*每个 SPEC 展开为详细文档时，遵循 Constitution 五步流程：先写 Spec 详情 → 再写 Test → 再定义 Interface → 最后 Implement。*
