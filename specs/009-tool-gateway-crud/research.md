# Research: Tool Gateway — 工具注册、管理与统一调用

**Feature**: 009-tool-gateway-crud  
**Date**: 2026-02-10  
**Purpose**: Resolve all technical decisions for Tool Gateway implementation

---

## R1: ToolRegistration Entity Design — Single Aggregate vs Separate per ToolType

**Question**: RestApi 和 McpServer 两种工具类型使用单一 `ToolRegistration` 聚合根 + 类型鉴别器，还是分为两个独立实体？

**Decision**: 单一 `ToolRegistration` 实体 + `ToolType` 枚举鉴别器 + 可空类型特有值对象

**Rationale**:
- 两种类型共享相同的生命周期行为（Register → Query → Update → Delete → Invoke），差异仅在数据形状和调用协议
- 统一 CRUD 端点 `POST/GET/PUT/DELETE /api/tools`、统一仓储、统一查询——与已有 AgentRegistration 设计模式一致
- 工厂方法模式（`CreateRestApi()`、`CreateMcpServer()`）在构造时保证类型与值对象一致性
- `toolType` 注册后不可变更，与 AgentRegistration 的 `agentType` 设计完全对齐
- OpenAPI 导入批量生成的工具也是 RestApi 类型，复用相同实体

**Alternatives Considered**:
- 每类型一个实体类（`RestApiTool`、`McpServerTool`）——增加 2 个实体 + 2 套 CRUD + 2 套端点，但生命周期完全相同
- TPH 继承——EF Core 中 JSON 列在派生类上配置复杂，且无多态行为需求

---

## R2: McpToolItem — 内嵌值对象 vs 独立实体

**Question**: MCP Server 发现的子工具项存储为 ToolRegistration 内的嵌套集合（JSONB），还是独立实体？

**Decision**: `McpToolItem` 作为独立实体，继承 `BaseEntity`，通过外键 `ToolRegistrationId` 关联到父工具源

**Rationale**:
- 子工具项需要独立查询（`GET /api/tools/{id}/mcp-tools`），JSONB 嵌套集合查询不便
- 子工具项需要在统一调用时按 `toolName` 精确查找，独立实体可建索引
- 级联删除通过 EF Core `OnDelete(DeleteBehavior.Cascade)` 自动处理
- 子工具项数量可达数十个（一个 MCP Server 可暴露大量工具），独立表更适合

**Alternatives Considered**:
- JSONB 嵌套集合（`List<McpToolItemVO>` 存为 JSON 列）——无法独立查询、按名称索引，数量多时 JSON 体积大
- 与 ToolRegistration 同表不同行——违背聚合根单一入口原则

**Database Schema**:

| Column | PostgreSQL Type | Constraint |
|--------|----------------|------------|
| id | uuid | PK |
| tool_registration_id | uuid | FK → tool_registrations.id, CASCADE DELETE |
| tool_name | varchar(200) | NOT NULL, UNIQUE within parent |
| description | text | nullable |
| input_schema | jsonb | nullable |
| output_schema | jsonb | nullable |
| annotations | jsonb | nullable |
| created_at | timestamptz | NOT NULL |
| updated_at | timestamptz | nullable |

---

## R3: Credential Encryption Strategy

**Question**: 认证凭据（API Key、Bearer Token、OAuth2 Client Secret）如何安全存储？

**Decision**: 使用 ASP.NET Core Data Protection API（`IDataProtector`）加密凭据字符串

**Rationale**:
- Data Protection API 是 ASP.NET Core 内置框架，无需引入额外依赖（已在共享框架中）
- `IDataProtector` 线程安全，可注册为 Singleton
- Purpose 字符串（`"CoreSRE.Infrastructure.CredentialEncryption.v1"`）提供密钥隔离
- 默认 Windows DPAPI 自动加密存储的密钥，容器环境可配置 `PersistKeysToDbContext` 或 Azure Key Vault
- 未来需要密钥轮换时，bumping purpose 版本号即可
- `Protect()` / `Unprotect()` API 简洁明了

**Alternatives Considered**:
- 手动 AES-256-GCM——需自行管理 IV/nonce/密钥轮换，重复造轮子
- Azure Key Vault 直接存储——增加云依赖，开发环境不便
- HashiCorp Vault——额外基础设施组件，当前阶段过度工程化

**Service Pattern**:
```
ICredentialEncryptionService (Application/Interfaces)
├── Encrypt(plaintext) → ciphertext (Base64)
├── Decrypt(ciphertext) → plaintext
└── Mask(ciphertext, visibleChars=4) → "****abcd"

CredentialEncryptionService (Infrastructure/Services)
└── 内部使用 IDataProtector
```

---

## R4: MCP Client Library Selection

**Question**: .NET 中使用哪个库与 MCP Server 通信（initialize 握手、tools/list 发现、tools/call 调用）？

**Decision**: 使用官方 MCP C# SDK `ModelContextProtocol` NuGet 包（0.8.0-preview.1）

**Rationale**:
- 由 Anthropic + Microsoft 联合维护的官方 SDK（github.com/modelcontextprotocol/csharp-sdk，3.9k stars）
- `McpClient.CreateAsync()` 自动处理完整的 initialize 握手流程
- `client.ListToolsAsync()` 返回 `IList<McpClientTool>`，每个工具自动继承 `AIFunction`，可直接给 `IChatClient` 使用
- `client.CallToolAsync("toolName", args)` 直接发起 tools/call 调用
- 内置 `HttpClientTransport`（StreamableHttp/SSE）和 `StdioClientTransport`（进程管理）两种传输
- 与项目已有的 Microsoft.Extensions.AI 生态无缝集成

**Alternatives Considered**:
- 手动实现 JSON-RPC over HTTP——工作量巨大，需处理握手、会话管理、SSE 流式传输
- mcpdotnet 社区包——已被官方 SDK 取代（fork 关系），不再维护

**Required Package**:
```xml
<PackageReference Include="ModelContextProtocol" Version="0.8.0-preview.1" />
```

---

## R5: OpenAPI Document Parsing Library

**Question**: 如何解析 OpenAPI/Swagger 文档（JSON + YAML）以提取工具信息？

**Decision**: 使用 `Microsoft.OpenApi` + `Microsoft.OpenApi.YamlReader` NuGet 包

**Rationale**:
- Microsoft 官方维护，支持 OpenAPI 3.0、3.1、Swagger 2.0
- `OpenApiDocument.LoadAsync(stream)` 自动检测 JSON 格式
- YAML 支持通过 `settings.AddYamlReader()` 扩展注册
- 丰富的 Diagnostic 错误报告——`errors` + `warnings` + JSON Pointer 定位
- 强类型 API：`document.Paths` → `pathItem.Operations` → `operation.Parameters` / `RequestBody` / `Responses`
- `operation.OperationId` 直接可用作工具名

**Alternatives Considered**:
- NSwag——更重（包含代码生成能力），我们只需要解析
- 手动 JSON 解析——无法处理 `$ref` 解析、多版本兼容
- SwashBuckle——专用于生成 OpenAPI 文档，不适合解析

**Required Packages**:
```xml
<PackageReference Include="Microsoft.OpenApi" Version="3.3.1" />
<PackageReference Include="Microsoft.OpenApi.YamlReader" Version="3.3.1" />
```

**映射规则**:

| OpenAPI 概念 | ToolRegistration 字段 |
|-------------|---------------------|
| `operationId` 或 `{method}_{path}` | Name |
| `summary` 或 `description` | Description |
| `parameters` + `requestBody.schema` | ToolSchema.InputSchema |
| `responses["200"].content.schema` | ToolSchema.OutputSchema |
| `servers[0].url` + `path` | ConnectionConfig.Endpoint |
| 请求中附带的 authConfig | AuthConfig |

---

## R6: Unified Tool Invocation Architecture

**Question**: 统一调用入口如何根据工具类型自动选择调用协议（REST HTTP vs MCP tools/call）？

**Decision**: 策略模式——`IToolInvoker` 接口 + `RestApiToolInvoker` / `McpToolInvoker` 实现 + `ToolInvokerFactory` 工厂

**Rationale**:
- 策略模式清晰分离不同协议的调用逻辑，符合开闭原则
- `IToolInvoker.InvokeAsync(ToolRegistration, parameters)` 统一签名，由 Factory 按 ToolType 分派
- RestApiToolInvoker 负责：构建 HTTP 请求 + 注入认证头 + 发送请求 + 解析响应
- McpToolInvoker 负责：获取 MCP 客户端连接 + 调用 `tools/call` + 解析 Content 结果
- 未来扩展新协议（如 gRPC）只需添加新 Invoker，不修改已有代码
- InvokeToolCommandHandler 只依赖 IToolInvoker，不关心底层协议

**Alternatives Considered**:
- 单一 Invoker 内部 if/else 分支——违反开闭原则，代码膨胀
- 在 ToolRegistration 实体上定义 `Invoke()` 方法——违背 DDD 分层原则（Domain 层不应有 HTTP/MCP 调用）

**Architecture**:
```
InvokeToolCommandHandler (Application)
  → IToolInvoker.InvokeAsync(tool, params)
    → ToolInvokerFactory (Infrastructure)
      ├── ToolType.RestApi → RestApiToolInvoker
      │   └── HttpClient + AuthConfig → HTTP request → parse response
      └── ToolType.McpServer → McpToolInvoker
          └── McpClient → tools/call → parse Content
```

---

## R7: MCP Client Connection Lifecycle

**Question**: MCP 客户端连接如何管理？每次调用创建新连接，还是维护长连接池？

**Decision**: 在 MCP Tool 发现阶段创建临时连接，完成后释放。调用阶段创建短生命周期连接（per-invocation）。

**Rationale**:
- MCP 连接是有状态的（session-based），长连接需要处理重连、会话过期等复杂场景
- 当前阶段调用频率不高（MVP），短连接足够
- `McpClient` 实现 `IAsyncDisposable`，每次 `CreateAsync` + `DisposeAsync` 清理资源
- 未来优化：可引入连接池或单例 McpClient 管理器，但不在 MVP 范围内
- MCP 发现（tools/list）和调用（tools/call）可以在同一连接中完成

**Alternatives Considered**:
- 全局单例 McpClient per ToolRegistration——需要管理连接状态、重连、多线程共享，复杂度高
- 连接池——适合高频调用场景，当前阶段过度工程化

---

## R8: Async MCP Discovery Pattern

**Question**: MCP Server 注册后的握手和 Tool 发现如何异步执行？

**Decision**: 使用 `IHostedService` / `BackgroundService` + Channel<T> 消息队列模式

**Rationale**:
- 注册 API 必须立即返回（<1 秒），不能等待 MCP 握手（可能耗时数秒到 30 秒超时）
- `Channel<Guid>` 作为进程内消息队列，注册完成后将 ToolRegistrationId 推入 Channel
- `McpDiscoveryBackgroundService` 后台消费 Channel，执行握手和 Tool 发现
- 发现成功后更新 ToolRegistration 状态为 Active，持久化发现的 McpToolItem
- 发现失败后更新状态为 Inactive，记录错误信息

**Alternatives Considered**:
- `Task.Run` 后台任务——生命周期不受管控，进程退出时可能丢失任务
- 消息队列（RabbitMQ/Redis）——外部依赖，当前阶段过度工程化
- Hangfire——额外库依赖，单一用途不值得

**Architecture**:
```
RegisterToolCommandHandler
  → 保存 ToolRegistration (Status = Inactive for McpServer)
  → _mcpDiscoveryChannel.Writer.WriteAsync(toolId)
  → 立即返回 201

McpDiscoveryBackgroundService (IHostedService)
  → 循环读取 _mcpDiscoveryChannel.Reader
  → 创建 McpClient + 握手 + tools/list
  → 成功: 保存 McpToolItem[], 更新 Status = Active
  → 失败: 更新 Status = Inactive, 记录 DiscoveryError
```

---

## R9: Authentication Injection for REST API Tool Invocation

**Question**: 统一调用 REST API 工具时，如何将认证信息注入到 HTTP 请求中？

**Decision**: 在 `RestApiToolInvoker` 中根据 `AuthType` 枚举分支注入不同的认证头

**Rationale**:
- 认证配置完全存储在 `AuthConfigVO` 中，调用时从中提取并解密
- 策略分支清晰（4 种 AuthType 已确定且有限）：
  - **None**: 不注入任何头
  - **ApiKey**: 注入 `X-Api-Key: {decryptedKey}` 或 `Authorization: ApiKey {key}`
  - **Bearer**: 注入 `Authorization: Bearer {decryptedToken}`
  - **OAuth2**: 使用 Client Credentials Grant 获取 Access Token，然后注入 `Authorization: Bearer {token}`
- OAuth2 Token 获取通过 `HttpClient` POST 到 `tokenEndpoint`，响应中提取 `access_token`
- 目前不缓存 OAuth2 Token（MVP 简化），每次调用重新获取

**Alternatives Considered**:
- DelegatingHandler per AuthType——适合 HttpClientFactory 管道，但需动态切换 handler chain，配置复杂
- OAuth2 MSAL 库——仅适用于 Azure AD，我们需要通用 OAuth2 Client Credentials

---

## R10: Standardized Invocation Result

**Question**: 统一调用接口返回什么结构？REST 响应和 MCP `tools/call` 响应如何标准化？

**Decision**: 定义 `ToolInvocationResultDto`，包含 `data`（响应数据）、`metadata`（耗时/状态码等）和 `isSuccess`

**Rationale**:
- Agent 和 Workflow 需要统一的结果格式，不关心底层协议
- REST 响应：`data` = 响应 body（JSON），`metadata.statusCode` = HTTP 状态码
- MCP 响应：`data` = `result.Content` 内容（text/JSON），`metadata` 提取自 structured content
- `isSuccess` = REST 2xx / MCP `isError == false`
- `metadata.durationMs` = 调用耗时（用于 OTel Span 和前端展示）

**Result Structure**:
```json
{
  "isSuccess": true,
  "data": { ... },
  "metadata": {
    "durationMs": 142,
    "statusCode": 200,
    "protocol": "REST",
    "toolName": "query_logs"
  },
  "error": null
}
```
