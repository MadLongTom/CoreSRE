# Research: 工作流实时推送（SignalR）

**Feature**: 017-workflow-signalr-push  
**Date**: 2026-02-14  
**Status**: Complete

## R1: 强类型 Hub vs 无类型 Hub

**Decision**: 使用强类型 Hub (`Hub<IWorkflowClient>`)  
**Rationale**:
- 编译时检查客户端方法名，避免 `SendAsync("name")` 魔法字符串
- 重构安全——重命名方法时编译器捕获所有调用点
- 在 `IHubContext<WorkflowHub, IWorkflowClient>` 注入时获得 IntelliSense
- .NET 10 框架内置支持，零额外包

**Alternatives considered**:
- 无类型 Hub + `SendAsync(string)` — 灵活但缺乏编译时安全，易拼写错误，放弃

## R2: SignalR Group 生命周期与重连

**Decision**: 客户端在 `onreconnected` 回调中重新调用 `JoinExecution`  
**Rationale**:
- SignalR Group 是基于 connectionId 的。重连后获得新 connectionId，旧组成员资格丢失
- `withAutomaticReconnect()` 会自动重建连接，但不会自动重新加入 Group
- `OnDisconnectedAsync` 中无需手动 `RemoveFromGroup`——框架自动清理断开连接的组成员

**Alternatives considered**:
- 服务端维护 userId→executionId 映射，`OnConnectedAsync` 时自动加入 — 复杂度过高，且项目当前无认证系统，放弃

## R3: IHubContext 在 BackgroundService 作用域中注入

**Decision**: `SignalRWorkflowNotifier` 注册为 Scoped，依赖 `IHubContext<WorkflowHub, IWorkflowClient>`(Singleton)  
**Rationale**:
- `IHubContext` 由 `AddSignalR()` 注册为 Singleton，可安全注入任何作用域
- `WorkflowExecutionBackgroundService` 通过 `IServiceScopeFactory.CreateScope()` 创建作用域，在其中解析 `IWorkflowEngine`
- `IWorkflowEngine` 依赖 `IWorkflowExecutionNotifier`（Scoped），后者依赖 `IHubContext`（Singleton）——依赖链合法
- `IHubContext` 是线程安全的，支持并行工作流推送

**Alternatives considered**:
- 将 Notifier 注册为 Singleton — 可行，但与项目其他服务 Scoped 风格不一致，放弃

## R4: 前端 SignalR 客户端库

**Decision**: 使用 `@microsoft/signalr` npm 包 + `withAutomaticReconnect()` + React `useEffect` 清理模式  
**Rationale**:
- 官方客户端库，与 ASP.NET Core SignalR 版本同步
- `withAutomaticReconnect()` 默认重试策略 `[0, 2000, 10000, 30000]ms` 满足 SC-005 的 5 秒降级要求
- React 集成标准模式：`useEffect` 中创建连接 + 注册 handler → 返回 cleanup 函数停止连接
- Handler 必须在 `.start()` 之前通过 `.on()` 注册，避免丢失消息

**Alternatives considered**:
- Socket.IO — 需要额外服务器端库，不与 ASP.NET Core 生态集成，放弃
- 原生 WebSocket — 缺少自动重连、组管理、协议协商等高级功能，放弃

## R5: 消息协议选择 (JSON vs MessagePack)

**Decision**: 使用 JSON（默认）  
**Rationale**:
- 节点状态事件是小体积载荷（~100-500 bytes：executionId, nodeId, status, error?）
- JSON 零额外配置，浏览器 DevTools 可直接调试
- SC-003 仅要求 10 个并发观察者，SC-001 允许 500ms 延迟——JSON 绰绰有余
- MessagePack 大小写敏感，需要前端用 PascalCase 属性名，增加复杂度

**Alternatives considered**:
- MessagePack — 对大型载荷/高吞吐场景有优势，但此场景载荷小、并发低，增加的包依赖和调试复杂度不值得，放弃

## R6: CORS 配置

**Decision**: 无需额外配置  
**Rationale**:
- 现有 `Program.cs` CORS 已配置：`WithOrigins("http://localhost:5173")` + `AllowCredentials()` + `AllowAnyMethod()` + `AllowAnyHeader()`
- SignalR WebSocket 连接通过协商流程处理，使用相同的 CORS 策略
- `app.UseCors()` 已在中间件管道中，在端点映射之前调用
- 仅需确保 `MapHub` 在 `UseCors()` 之后注册（当前管道顺序已满足）

**Alternatives considered**: 无——现有配置已完备

## R7: 断线恢复策略

**Decision**: SignalR 实时事件 + REST 快照回退（连接建立/重连时加载）  
**Rationale**:
- SignalR 没有内置"重放丢失消息"机制
- 现有 `GET /api/workflows/{id}/executions/{execId}` 返回完整的 `WorkflowExecutionDto`（含 `graphSnapshot` 和 `nodeExecutions[]`）——天然的状态快照端点
- 页面挂载/重连时序：(1) REST 加载快照 → (2) SignalR 连接并加入组 → (3) 实时事件增量更新
- 这是业界标准模式（GitHub、Figma 等使用类似方案）

**Alternatives considered**:
- 仅 SignalR（无 REST 回退）— 断线期间丢失事件无法恢复，不可接受
- 服务端事件缓冲/重放 — 有效但实现复杂（需维护 per-execution 事件日志），超出当前需求范围，放弃

## R8: WorkflowEngine 中 Notifier 注入策略

**Decision**: 将 `IWorkflowExecutionNotifier` 作为可选构造函数参数注入 `WorkflowEngine`  
**Rationale**:
- 保持向后兼容——无 Notifier 时使用 `NullWorkflowExecutionNotifier`（空操作实现）
- 现有 7 个构造函数参数 + 1 个新参数 = 8 个，仍在 DI 可管理范围内
- Hook 点已明确：`execution.Start()` → `NotifyExecutionStarted`，`execution.StartNode()` → `NotifyNodeStarted`，以此类推
- 通知调用应紧跟在 `_executionRepo.UpdateAsync()` 之后（确保数据库状态已持久化后再通知）

**Alternatives considered**:
- 通过 Domain Events 发布/订阅 — 架构上更优雅，但项目当前无 Domain Event 基础设施（无 EventDispatcher/MediatR notification pipeline），引入成本高，放弃
- 中间件/AOP 方式拦截 — 执行引擎是内部服务不经过 HTTP 管道，无法用中间件拦截，放弃

## Summary Decision Table

| Topic | Decision | Pattern |
|-------|----------|---------|
| Hub 类型 | `Hub<IWorkflowClient>` (强类型) | 编译时安全 |
| Group 管理 | `execution:{executionId}`, 客户端重连时重新加入 | 标准 SignalR Group |
| DI 注册 | `SignalRWorkflowNotifier` Scoped, `IHubContext` Singleton | 合法依赖链 |
| 前端客户端 | `@microsoft/signalr` + `withAutomaticReconnect()` | useEffect 清理模式 |
| 消息协议 | JSON (默认) | 小载荷，可调试 |
| CORS | 无需更改 | 现有配置已满足 |
| 断线恢复 | SignalR + REST 快照 | 业界标准 |
| 引擎注入 | 可选构造函数参数 + NullNotifier | 向后兼容 |
