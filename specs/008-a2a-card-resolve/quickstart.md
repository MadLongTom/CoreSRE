# Quickstart: A2A AgentCard 自动解析

**Feature**: 008-a2a-card-resolve  
**Date**: 2026-02-10

## Prerequisites

1. CoreSRE 后端运行中（`dotnet run --project Backend/CoreSRE`）
2. CoreSRE 前端运行中（`cd Frontend && npm run dev`）
3. 至少一个可访问的 A2A Agent endpoint（提供 `/.well-known/agent-card.json`）

## 快速验证

### 1. 后端 API 验证

使用 HTTP 请求直接测试解析端点：

```http
POST /api/agents/resolve-card
Content-Type: application/json

{
  "url": "https://your-a2a-agent.example.com/a2a"
}
```

**成功响应** (200):
```json
{
  "success": true,
  "data": {
    "name": "Translation Agent",
    "description": "An agent that translates text between languages",
    "url": "https://your-a2a-agent.internal.com/a2a",
    "version": "1.0.0",
    "skills": [
      { "name": "translate", "description": "Translate text" }
    ],
    "interfaces": [
      { "protocol": "JsonRpc", "path": "https://your-a2a-agent.internal.com/a2a" }
    ],
    "securitySchemes": [
      { "type": "bearer", "parameters": "{\"bearerFormat\":\"JWT\"}" }
    ]
  }
}
```

**错误响应** (502 — 端点不可达):
```json
{
  "success": false,
  "message": "Failed to connect to the remote agent endpoint: Connection refused"
}
```

### 2. 前端完整流程

1. 打开创建 Agent 页面
2. 在 Step 1 选择 **A2A** 类型
3. 在 Step 2 的 **Endpoint URL** 输入框中输入远程 Agent 的 URL
4. 点击 **解析** 按钮
5. 等待加载（最多 10 秒）
6. 观察：
   - Skills、Interfaces、SecuritySchemes 字段自动填充
   - 名称和描述字段预填（如果之前为空）
   - 如果 AgentCard 中的 URL 与输入的 URL 不同，显示 **"覆写 URL"** 开关
7. 审阅并微调字段后，点击创建

### 3. URL 覆写验证

当 AgentCard 返回的 `url` 与你输入的 URL 不同时：

| 覆写选项 | 创建后的 Endpoint |
|---------|------------------|
| 开启（默认） | 你输入的 URL |
| 关闭 | AgentCard 中的 URL |

### 4. 错误场景验证

| 输入 | 预期结果 |
|------|---------|
| 空 URL | 前端验证阻止，提示"URL 不能为空" |
| `ftp://invalid` | 前端验证阻止，提示"URL 必须以 http 或 https 开头" |
| `https://nonexistent.local` | 解析失败，显示"无法连接到远程端点" |
| 不返回 AgentCard 的 URL | 解析失败，显示"返回数据无法解析为有效的 AgentCard" |
| 10 秒内无响应的 URL | 超时提示，显示"请求超时" |

## 涉及的关键文件

### 后端（新增/修改）

| 文件 | 类型 | 描述 |
|------|------|------|
| `Application/Interfaces/IAgentCardResolver.cs` | 新增 | 解析接口定义 |
| `Application/Agents/DTOs/ResolvedAgentCardDto.cs` | 新增 | 解析结果 DTO |
| `Application/Agents/Queries/ResolveAgentCard/` | 新增 | MediatR Query + Handler + Validator |
| `Infrastructure/Services/A2ACardResolverService.cs` | 新增 | SDK 封装实现 |
| `Endpoints/AgentEndpoints.cs` | 修改 | 添加 POST /resolve-card 路由 |
| `Program.cs` | 修改 | DI 注册 IAgentCardResolver |
| `CoreSRE.Infrastructure.csproj` | 修改 | 添加 `A2A` NuGet 包引用 |

### 前端（新增/修改）

| 文件 | 类型 | 描述 |
|------|------|------|
| `lib/api/agents.ts` | 修改 | 新增 `resolveAgentCard()` API 函数 |
| `types/agent.ts` | 修改 | 新增 `ResolvedAgentCard` 类型 |
| `pages/AgentCreatePage.tsx` | 修改 | 解析按钮、自动填充、URL 覆写开关 |
