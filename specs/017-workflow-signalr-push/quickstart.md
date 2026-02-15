# Quickstart: 工作流实时推送（SignalR）

**Feature**: 017-workflow-signalr-push  
**Date**: 2026-02-14

## Prerequisites

- .NET 10 SDK
- Node.js 22+ / npm
- PostgreSQL (via Aspire AppHost)
- 已完成的 SPEC-016 工作流数据流执行引擎

## Backend Setup

### 1. 无需安装额外 NuGet 包

ASP.NET Core SignalR 服务端已包含在 `net10.0` 框架中，不需要额外安装 NuGet 包。

### 2. 注册 SignalR 服务

在 `Program.cs` 中添加：

```csharp
builder.Services.AddSignalR();
```

### 3. 映射 Hub 端点

在 `Program.cs` 的端点映射区域添加：

```csharp
app.MapHub<WorkflowHub>("/hubs/workflow");
```

确保位于 `app.UseCors()` 之后。

### 4. DI 注册

在 `Infrastructure/DependencyInjection.cs` 中注册 notifier：

```csharp
services.AddScoped<IWorkflowExecutionNotifier, SignalRWorkflowNotifier>();
```

## Frontend Setup

### 1. 安装 SignalR 客户端

```bash
cd Frontend
npm install @microsoft/signalr
```

### 2. 创建连接

```typescript
import * as signalR from "@microsoft/signalr";

const connection = new signalR.HubConnectionBuilder()
  .withUrl("http://localhost:5102/hubs/workflow")
  .withAutomaticReconnect()
  .build();
```

### 3. 注册事件处理器

```typescript
connection.on("NodeExecutionStarted", (executionId, nodeId, input) => {
  // 更新节点状态
});
```

**重要**: 所有 `.on()` 注册必须在 `.start()` 之前完成。

## Development Workflow (Constitution 5-step)

```
Step 1: ✅ Spec 已完成 (spec.md)
Step 2: 编写测试 (Red phase)
  - SignalRWorkflowNotifierTests — mock IHubContext, 验证 Group 推送
  - WorkflowEngine notifier 集成测试 — 验证生命周期钩子调用
Step 3: 定义接口
  - IWorkflowExecutionNotifier (Domain/Interfaces)
  - IWorkflowClient (CoreSRE/Hubs)
Step 4: 实现
  - SignalRWorkflowNotifier (Infrastructure/Services)
  - NullWorkflowExecutionNotifier (Infrastructure/Services)
  - WorkflowHub (CoreSRE/Hubs)
  - WorkflowEngine 修改 — 注入 + 调用 notifier
  - Program.cs — AddSignalR + MapHub
  - Frontend — useWorkflowSignalR hook + 页面集成
Step 5: 验证 (Green phase)
  - 全部测试通过
  - 手动验证：启动工作流 → 观察 DAG 节点实时着色
```

## Verification Commands

```bash
# 后端构建
cd Backend/CoreSRE
dotnet build

# 后端测试
cd Backend
dotnet test CoreSRE.Infrastructure.Tests
dotnet test CoreSRE.Application.Tests

# 前端构建
cd Frontend
npm run build

# 运行应用（Aspire AppHost）
cd Backend/CoreSRE.AppHost
dotnet run
```

## Key File Locations

| File | Purpose |
|------|---------|
| `Backend/CoreSRE.Domain/Interfaces/IWorkflowExecutionNotifier.cs` | 通知接口 |
| `Backend/CoreSRE/Hubs/IWorkflowClient.cs` | 强类型 Hub 客户端接口 |
| `Backend/CoreSRE/Hubs/WorkflowHub.cs` | SignalR Hub |
| `Backend/CoreSRE.Infrastructure/Services/SignalRWorkflowNotifier.cs` | 通知实现 |
| `Backend/CoreSRE.Infrastructure/Services/WorkflowEngine.cs` | 执行引擎（修改） |
| `Frontend/src/hooks/useWorkflowSignalR.ts` | React SignalR hook |
| `Frontend/src/pages/WorkflowExecutionDetailPage.tsx` | 执行详情页（修改） |
