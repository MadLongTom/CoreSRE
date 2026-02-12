# n8n 工作流引擎调研报告

> 调研目标：分析 n8n 工作流引擎的核心架构（执行引擎、数据流、前端画布），与 CoreSRE 现有实现做差距分析，指导后续改进。

---

## 一、n8n 架构总览

n8n 采用 monorepo 架构，核心包：

| 包名 | 职责 |
|------|------|
| `packages/workflow` | 数据模型定义 (`INode`, `IConnections`, `INodeExecutionData` 等)、`Workflow` 类、表达式引擎、DAG 图工具 |
| `packages/core` | 执行引擎 (`WorkflowExecute`)、节点执行上下文、部分执行 (Partial Execution)、二进制数据管理 |
| `packages/cli` | HTTP 服务器、API 路由、执行调度、WebSocket/SSE 推送 |
| `packages/frontend/editor-ui` | Vue 3 + Vue Flow 可视化编辑器、Pinia 状态管理、实时执行状态显示 |
| `packages/nodes-base` | 400+ 内置节点实现 |

---

## 二、核心数据模型

### 2.1 节点 (`INode`)

```typescript
interface INode {
  id: string;                    // UUID
  name: string;                  // 用户可见名称（图内唯一）
  type: string;                  // 节点类型标识，如 "n8n-nodes-base.httpRequest"
  typeVersion: number;           // 类型版本号
  position: [number, number];    // 画布坐标 [x, y]
  parameters: INodeParameters;   // 节点配置参数
  disabled?: boolean;
  retryOnFail?: boolean;
  maxTries?: number;
  executeOnce?: boolean;         // 仅用第一条输入数据执行
  credentials?: INodeCredentials;
}
```

### 2.2 连接 (`IConnections`)

n8n 的连接模型是**源节点中心**的邻接表：

```typescript
// 完整连接结构
IConnections = {
  [sourceNodeName: string]: {
    [connectionType: string]: Array<IConnection[] | null>
    //  ↑ "main", "ai_agent", "ai_memory" 等
    //                          ↑ 外层索引 = 源节点输出端口索引
    //                                       ↑ 内层数组 = 该端口连向的所有目标
  }
}

// 单条连接
IConnection = {
  node: string;              // 目标节点名
  type: NodeConnectionType;  // 目标端口类型
  index: number;             // 目标端口索引
}
```

**设计亮点：**
- 支持多种连接类型（`main` 是数据流，`ai_agent` / `ai_memory` / `ai_tool` 等是 AI 子连接）
- 同时维护 `connectionsBySourceNode` 和 `connectionsByDestinationNode` 两个视图
- 每个节点可有多个输入/输出端口，且每个端口可接多条连接

### 2.3 单条数据项 (`INodeExecutionData`)

这是 n8n 数据流的**原子单位**：

```typescript
interface INodeExecutionData {
  json: IDataObject;              // 主 JSON 负载
  binary?: IBinaryKeyData;        // 二进制附件（文件、图片等）
  pairedItem?: IPairedItemData;   // 追踪"这条输出来自哪条输入"
  error?: NodeApiError;
}
```

**关键设计：每个节点处理和输出的是 `INodeExecutionData[]`（数组），一个节点可以输出多条数据。**

### 2.4 节点间数据传递 (`ITaskDataConnections`)

```typescript
ITaskDataConnections = {
  [connectionType: string]: Array<INodeExecutionData[] | null>
  // e.g. { main: [ [item0, item1, ...], null ] }
  //                 ↑ 输入端口0的数据     ↑ 输入端口1的数据（尚无）
}
```

外层数组索引 = 端口索引，内层数组 = 该端口上的数据条目。

### 2.5 执行运行时数据 (`IRunExecutionData`)

```
IRunExecutionData {
  startData? {
    destinationNode?: IDestinationNode  // "执行到此节点为止"
    runNodeFilter?: string[]            // 只允许这些节点执行
  }
  resultData {
    runData: IRunData                   // 核心：所有节点的执行结果
    pinData?: IPinData                  // 用户钉选的模拟数据
    lastNodeExecuted?: string
    error?: ExecutionError
  }
  executionData? {
    nodeExecutionStack: IExecuteData[]            // 待执行队列
    waitingExecution: IWaitingForExecution         // 多输入节点等待区
    waitingExecutionSource: IWaitingForExecutionSource
    metadata: { [nodeName]: ITaskMetadata[] }
  }
  waitTill?: Date                       // 暂停到某时间
}
```

其中 `IRunData = { [nodeName: string]: ITaskData[] }`，每个节点可执行多次（循环），所以是数组。

---

## 三、执行引擎设计

### 3.1 执行模型：基于栈的 DAG 遍历

n8n 不使用传统的拓扑排序，而是采用**执行栈 + 等待队列**模式：

```
┌─────────────────────────────────────────────────┐
│  nodeExecutionStack (执行栈)                     │
│  ┌──────┐ ┌──────┐ ┌──────┐                    │
│  │Node A│ │Node C│ │Node D│  ← shift() 取出    │
│  └──────┘ └──────┘ └──────┘                     │
└─────────────────────────────────────────────────┘
                    ↕ 数据不全时
┌─────────────────────────────────────────────────┐
│  waitingExecution (等待区)                       │
│  { "MergeNode": {                               │
│      0: { main: [data_port0, null] }   ← 端口1  │
│    }                                    缺数据   │
│  }                                               │
└─────────────────────────────────────────────────┘
```

### 3.2 主循环算法 (`processRunExecutionData`)

```
while (nodeExecutionStack 不为空):
  1. 检查超时/取消 → 退出
  2. executionData = nodeExecutionStack.shift()  // 取出栈首
  3. 计算 runIndex（该节点第几次运行）
  4. 无限循环检测（同 node+runIndex 连续两次 → 报错）
  5. 节点过滤器检查（runNodeFilter）
  6. 确认输入数据完整性
  7. 触发 nodeExecuteBefore 钩子
  8. 重试循环 (maxTries):
     a. 有 pinData？→ 直接用钉选数据，跳过执行
     b. 调用 runNode() → 获取输出数据
     c. 处理 continueOnFail / 错误路由
  9. 构建 ITaskData → 存入 runData[nodeName]
  10. 检查 waitTill → 暂停
  11. 检查 destinationNode → 到达目标即停
  12. ★ 传播数据：遍历 connectionsBySourceNode[nodeName].main:
      对每个 outputIndex → 每条连接:
        调用 addNodeToBeExecuted() 将下游节点加入栈/等待区
  13. 触发 nodeExecuteAfter 钩子
  14. 若栈为空但 waitingExecution 还有 → 提升等待节点到栈中

return 最终运行结果
```

### 3.3 数据传播 (`addNodeToBeExecuted`)

这是执行引擎最核心的方法，处理节点间数据传递：

```
addNodeToBeExecuted(connectionData, outputIndex, parentNodeName, nodeSuccessData, runIndex):

  if 目标节点只有 1 个输入:
    → 直接推入 nodeExecutionStack:
      { node: 目标, data: { main: [parentOutput] }, source: 来源信息 }

  if 目标节点有多个输入 (如 Merge 节点):
    → 检查 waitingExecution 中是否已有该节点的等待条目
    → 将数据填入对应的输入端口槽位：
      waitingExecution[target][runIndex].main[portIndex] = data
    → 检查所有槽位是否已满
    → 若全满：从 waitingExecution 移到 nodeExecutionStack
    → 若未满：继续等待
```

### 3.4 表达式引擎：节点间数据引用

n8n 允许任何节点通过表达式引用其他已执行节点的输出数据：

```javascript
// 现代语法
{{ $("HTTP Request").item.json.name }}      // 当前项的配对数据
{{ $("HTTP Request").first().json }}        // 第一条输出
{{ $("HTTP Request").all() }}               // 所有输出

// $input 快捷方式
{{ $input.item.json.field }}                // 当前输入项
{{ $input.first().json }}                   // 第一条输入

// 遗留语法
{{ $node["HTTP Request"].json }}
```

实现机制：`WorkflowDataProxy` 创建 JavaScript `Proxy` 对象，当表达式被求值时：
1. 查找 `runExecutionData.resultData.runData["目标节点"]`
2. 通过 `pairedItem` 链追踪当前项对应的源项
3. 返回 `executionData[itemIndex].json`

### 3.5 部分执行 (Partial Execution)

当用户在编辑器中只想执行某个节点时，n8n 不会重新执行整个工作流：

```
1. findTriggerForPartialExecution() → 找到最近的触发器
2. findSubgraph(graph, destination, trigger) → 从目标节点反向遍历到触发器，提取子图
3. findStartNodes(graph, trigger, destination, runData, pinData)
   → 正向遍历，找到"脏节点"（参数变更、无运行数据、是目标节点）作为起始点
4. handleCycles() → 处理环路
5. cleanRunData() → 清理起始节点之后的旧运行数据
6. recreateNodeExecutionStack() → 重建执行栈
   → 已有数据的节点作为输入源，脏节点放入执行栈
7. processRunExecutionData() → 执行
```

---

## 四、前端画布设计

### 4.1 技术栈

| 技术 | 用途 |
|------|------|
| **Vue 3** | UI 框架 |
| **Vue Flow** (`@vue-flow/core`) | 节点画布渲染（Vue 版 React Flow） |
| **Pinia** | 状态管理 |
| **WebSocket / SSE** | 实时执行状态推送 |

### 4.2 组件层级

```
NodeView.vue (路由级视图)
  └─ WorkflowCanvas.vue (useCanvasMapping 将工作流数据 → 画布模型)
       └─ Canvas.vue (VueFlow 包装器)
            ├─ <VueFlow> (核心画布)
            ├─ CanvasNode.vue (自定义节点模板)
            │    ├─ CanvasNodeToolbar.vue
            │    ├─ CanvasHandleRenderer.vue (端口渲染)
            │    └─ CanvasNodeRenderer.vue
            │         ├─ CanvasNodeDefault.vue     (标准节点)
            │         ├─ CanvasNodeStickyNote.vue   (便签)
            │         ├─ CanvasNodeAddNodes.vue     (添加节点按钮)
            │         └─ CanvasNodeChoicePrompt.vue (选择提示)
            ├─ CanvasEdge.vue (自定义边模板)
            ├─ CanvasConnectionLine.vue (拖拽连线时的临时线)
            ├─ CanvasBackground.vue (网格背景)
            ├─ CanvasControlButtons.vue (缩放/适应按钮)
            └─ MiniMap (小地图)
```

### 4.3 核心 Composable

| Composable | 职责 |
|-----------|------|
| `useCanvasMapping` | **核心桥梁**：将 `IWorkflowDb` → `CanvasNode[]` + `CanvasConnection[]`，计算每个节点的执行状态/运行数据/错误/钉选数据等 |
| `useCanvas` | 画布上下文注入（视口、连接状态） |
| `useCanvasNode` | 单节点上下文（ID、标签、执行状态、样式） |
| `useCanvasTraversal` | 图遍历（上游/下游/兄弟节点查找，用于键盘导航） |
| `useCanvasLayout` | 自动布局 |

### 4.4 节点数据结构 (`CanvasNodeData`)

```typescript
interface CanvasNodeData {
  id, name, type, typeVersion: string;
  subtitle: string;
  disabled: boolean;
  inputs: CanvasConnectionPort[];     // 输入端口定义
  outputs: CanvasConnectionPort[];    // 输出端口定义
  connections: { inputs, outputs };   // 实际连接
  issues: { items, visible };         // 错误信息
  pinnedData: { count, visible };     // 钉选数据
  execution: {                        // 执行状态
    status: ExecutionStatus;          // success/error/running/waiting
    waiting?: string;
    running: boolean;
  };
  runData: {                          // 运行数据摘要
    outputMap: Map<type, Map<index, { total, iterations }>>;
    iterations: number;
    visible: boolean;
  };
  render: CanvasNodeRender;           // 渲染类型
}
```

### 4.5 执行流程（前端视角）

```
1. 用户点击 "Run" → useRunWorkflow.runWorkflow()
2. 检查推送连接是否就绪（WebSocket/SSE）
3. 保存工作流（如有未保存修改）
4. 收集工作流数据 + 起始/目标节点
5. POST /workflows/{id}/run → 获取 executionId
6. 通过 WebSocket/SSE 接收实时事件：
   ├─ nodeExecuteBefore  → 标记节点为"运行中"（转圈动画）
   ├─ nodeExecuteAfter   → 更新节点状态
   ├─ nodeExecuteAfterData → 接收节点输出数据（显示在节点上）
   ├─ executionStarted   → 标记执行开始
   └─ executionFinished  → 接收最终结果，清除运行状态
7. useCanvasMapping 响应式重算节点视觉状态 → Vue Flow 重渲染
```

### 4.6 Store 架构

| Store | 职责 |
|-------|------|
| `workflows.store` | 工作流数据中心：节点、连接、设置、钉选数据、执行数据、`Workflow` 实例 |
| `workflowState.store` | 执行状态追踪：活跃执行 ID、正在执行的节点 |
| `pushConnection.store` | 推送连接生命周期：WebSocket/SSE 连接管理、消息队列、重连 |
| `executions.store` | 执行列表管理：历史记录获取/过滤/排序 |
| `nodeTypes.store` | 节点类型注册表和描述信息 |
| `canvas.store` | 画布 UI 状态 |

---

## 五、与 CoreSRE 的差距分析

### 5.1 CoreSRE 当前实现状态

| 能力 | 状态 | 说明 |
|------|------|------|
| 工作流 CRUD | ✅ 完成 | 创建/查询/更新/删除 + DAG 验证 |
| DAG 数据模型 | ✅ 完成 | `WorkflowGraphVO` + 节点/边 VO |
| 执行引擎代码 | ✅ 完成 | `WorkflowEngine.cs` (575行, 拓扑排序 + 并行/条件) |
| 执行记录持久化 | ✅ 完成 | `WorkflowExecution` + `NodeExecutionVO` |
| 后台执行服务 | ✅ 完成 | `Channel<T>` + `BackgroundService` |
| 前端画布编辑器 | ✅ 完成 | ReactFlow DAG 编辑 + 执行可视化 |
| 能实际运行 | ❌ 不能 | 见下方问题清单 |

### 5.2 为什么"跑不起来"

| # | 问题 | 详情 |
|---|------|------|
| 1 | **BoxLite 构建失败** | Infrastructure 项目依赖 Rust 编译的 boxlite，WSL 环境多次构建失败，整个后端无法编译 |
| 2 | **Agent 节点无法实际执行** | `WorkflowEngine` 调用 `IAgentResolver.ResolveAsync()` → `IChatClient.GetResponseAsync()`，需要真实的 LLM Provider 配置和 Agent 注册 |
| 3 | **Tool 节点是桩函数** | Tool 节点通过 `IToolInvokerFactory` 调用，但实际 invoker 实现可能不完整 |
| 4 | **数据库迁移未应用** | `workflow_executions` 表的 EF Core 迁移可能未实际运行 |
| 5 | **无实时执行状态推送** | 只有轮询，没有 WebSocket/SSE 推送节点执行进度 |

### 5.3 架构层面的关键差距

| 维度 | n8n | CoreSRE | 差距 |
|------|-----|---------|------|
| **执行模型** | 执行栈 + 等待队列，事件驱动 | Kahn 拓扑排序，批量顺序执行 | CoreSRE 更简单但不支持动态路由 |
| **数据流粒度** | 每条数据独立 (`INodeExecutionData[]`)，支持批量/单条模式 | 节点间仅传递 JSON 字符串 | n8n 的 items 模型更灵活 |
| **多输入汇聚** | `waitingExecution` 等待队列自动协调 | FanIn 固定等待所有 FanOut 完成 | n8n 更通用 |
| **表达式引擎** | Proxy-based JS 表达式，可引用任意已执行节点 | 条件边用 JSON Path 解析 | CoreSRE 缺少节点间数据引用 |
| **部分执行** | 仅重新执行脏节点子图 | 无（每次全量执行） | 开发体验大差距 |
| **实时推送** | WebSocket/SSE 推送每个节点状态 | 仅轮询 | 用户体验差 |
| **节点类型系统** | 400+ 类型，声明式 + 编程式 | 5 种固定类型 (Agent/Tool/Condition/FanOut/FanIn) | CoreSRE 按场景足够 |
| **前端画布** | Vue Flow + 丰富的交互 (拖拽/缩放/小地图/自动布局) | ReactFlow 基础实现 | 功能可扩展 |
| **错误处理** | continueOnFail、错误路由、重试 | per-node 超时、失败标记 | 缺少错误路由 |
| **数据追踪** | pairedItem 追踪每条数据的来源链 | 无 | 调试困难 |

---

## 六、n8n 关键设计理念的启示

### 6.1 "Items" 数据模型

n8n 最核心的设计是**每个节点处理的是数据条目数组**，而不是单个 JSON 对象：

```
Node A 输出:
  [ {json: {name: "Alice"}}, {json: {name: "Bob"}} ]
      ↓
Node B 对每条数据执行:
  对 Alice 执行 → 输出 {json: {name: "Alice", age: 30}}
  对 Bob   执行 → 输出 {json: {name: "Bob", age: 25}}
      ↓
Node B 输出:
  [ {json: {name: "Alice", age: 30}}, {json: {name: "Bob", age: 25}} ]
```

这使得数据管道天然支持批量处理，而 CoreSRE 目前的 `string? Input/Output` 模型只能处理单个 JSON。

### 6.2 执行栈而非拓扑排序

n8n 用栈 (`shift/unshift`) 而不是一次性拓扑排序，因为：
- 支持动态路由（条件分支在运行时决定走哪条路）
- 支持循环（通过 `handleCycles` 处理）
- 支持暂停/恢复（`waitTill` + 将节点推回栈）
- 自然处理多输入节点的数据汇聚（`waitingExecution`）

### 6.3 连接的类型系统

n8n 的连接不只是"从 A 到 B"，还区分类型：
- `main`：数据流
- `ai_agent`：AI Agent 的子节点
- `ai_memory`：记忆模块
- `ai_tool`：工具绑定

这在 AI 工作流场景下非常关键——CoreSRE 的边只有 Normal/Conditional 两种，如果要深度支持 Agent 编排，需要扩展边类型。

### 6.4 前后端实时联动

n8n 的实时体验核心：
1. 后端执行到每个节点时触发 Hook（`nodeExecuteBefore`、`nodeExecuteAfter`）
2. Hook 通过 WebSocket/SSE 推送事件到前端
3. 前端 Store 更新 → Composable 重算 → Vue Flow 节点颜色/动画实时变化
4. 用户看到节点一个个"亮起来"，有明确的执行进度感

---

## 七、建议的改进优先级

### P0 — 让工作流能跑起来

1. **解决 BoxLite 构建问题**（或移除该依赖），让后端能编译运行
2. **确保数据库迁移已应用**
3. **实现最小可用的 Agent 节点执行**：即使没有真实 LLM，也能用 Mock 跑通完整流程
4. **端到端测试**：从前端创建工作流 → 执行 → 查看结果

### P1 — 数据流改进

5. **丰富节点间数据模型**：从 `string? Output` 升级为结构化的 `JsonElement[] Items`，支持批量条目
6. **实现 WebSocket 推送执行进度**：复用现有 SignalR/WebSocket 基础设施，推送 `nodeExecuteBefore`/`nodeExecuteAfter` 事件
7. **增加输入数据映射**：允许节点配置如何从上游节点的输出中提取数据（类似 n8n 的表达式）

### P2 — 高级功能

8. **部分执行**：记录节点的运行数据缓存，只重新执行变更的节点
9. **错误路由**：支持 `continueOnFail`，让失败不阻断整个工作流
10. **扩展边类型**：增加 AI 相关的连接类型（Memory、Tool Binding 等）

---

## 八、附录：核心文件索引

| 文件 | 说明 |
|------|------|
| `packages/workflow/src/workflow.ts` | `Workflow` 类：节点管理、连接管理、图遍历 |
| `packages/workflow/src/interfaces.ts` | 所有核心类型定义 (3445 行) |
| `packages/workflow/src/run-execution-data/` | `IRunExecutionData` 运行时数据结构 |
| `packages/workflow/src/graph/` | DAG 图工具：邻接表构建、根/叶节点查找、路径检测 |
| `packages/core/src/execution-engine/workflow-execute.ts` | 执行引擎主体 (2652 行)：`run()`, `processRunExecutionData()`, `addNodeToBeExecuted()` |
| `packages/core/src/execution-engine/partial-execution-utils/` | 部分执行：子图提取、起始节点查找、执行栈重建 |
| `packages/core/src/execution-engine/partial-execution-utils/directed-graph.ts` | `DirectedGraph` 类：便于操作的图结构 |
| `packages/frontend/editor-ui/src/features/workflows/canvas/` | 画布组件 + Composable |
| `packages/frontend/editor-ui/src/app/stores/workflows.store.ts` | 工作流状态 Store (1956 行) |
| `packages/frontend/editor-ui/src/app/stores/pushConnection.store.ts` | WebSocket/SSE 推送连接 |
