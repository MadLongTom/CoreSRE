# Research: A2A AgentCard 自动解析

**Feature**: 008-a2a-card-resolve  
**Date**: 2026-02-10

## 1. A2ACardResolver SDK 选型

### Decision: 使用 `A2A` NuGet 包（`a2aproject/a2a-dotnet` 社区库）

### Rationale
- **Microsoft Agents SDK 没有 A2A 客户端**。`microsoft/Agents-for-net` 的 README 明确声明："We are not supplying an A2A Client. Future support will come for this when Agents SDK adopts a2aproject/a2a-dotnet."
- `a2a-dotnet` 库提供了完整的 `A2ACardResolver` 类，API 签名：
  ```csharp
  public sealed class A2ACardResolver
  {
      public A2ACardResolver(Uri baseUrl, HttpClient? httpClient = null,
          string agentCardPath = "/.well-known/agent-card.json", ILogger? logger = null);
      public Task<AgentCard> GetAgentCardAsync(CancellationToken cancellationToken = default);
  }
  ```
- NuGet 包名：`A2A`（客户端 + 模型）；`A2A.AspNetCore`（仅服务端，本次不需要）
- 支持 .NET Standard 2.0 / .NET 8+，与我们的 .NET 10 目标框架兼容

### Alternatives Considered
| Alternative | Rejected Because |
|-------------|------------------|
| 自行实现 HTTP GET + JSON 反序列化 | 重复造轮子；`A2ACardResolver` 已处理路径拼接、HttpClient 共享、错误处理、SSE 解析 |
| Microsoft.Agents.Client | 这是 Bot Framework 协议客户端，不支持 A2A 协议 |
| Microsoft.Agents.Hosting.AspNetCore.A2A.Preview | 这是服务端 hosting 包，不包含客户端解析能力 |

## 2. A2A Well-Known 路径

### Decision: 默认使用 `/.well-known/agent-card.json`

### Rationale
- A2A 协议 v0.3.0 规范定义的标准路径
- `A2ACardResolver` 默认使用此路径，构造函数可自定义
- Microsoft SDK 在服务端同时映射 `/.well-known/agent-card.json`（主）和 `/.well-known/agent.json`（旧版兼容）
- 我们的 resolver 只需传入 baseUrl，路径拼接由 SDK 自动处理

### Alternatives Considered
| Alternative | Rejected Because |
|-------------|------------------|
| `/.well-known/agent.json` | 旧版路径，仅为 TCK 兼容保留；新协议版本使用 `agent-card.json` |
| 允许用户自定义路径 | 超出 MVP 范围；绝大多数 A2A Agent 使用标准路径 |

## 3. AgentCard 模型映射策略

### Decision: 从 `A2A.AgentCard`（SDK 类型）手动映射到现有 `AgentCardVO`

### Rationale
- SDK 的 `AgentCard` 字段远多于我们的 `AgentCardVO`（简化子集）
- 现有 `AgentCardVO` 只存储：`Skills`（name, description）、`Interfaces`（protocol, path）、`SecuritySchemes`（type, parameters）
- SDK 的 `AgentSkill` 有 `Id`, `Name`, `Description`, `Tags`, `Examples`, `InputModes`, `OutputModes`；我们只映射 `Name` 和 `Description`
- SDK 的 `AgentInterface` 有 `Transport`（枚举）和 `Url`；需映射到我们的 `Protocol`（string）和 `Path`（string）
- SDK 的 `SecurityScheme` 是 `Dictionary<string, SecurityScheme>`（键值对），需映射到我们的 `List<SecuritySchemeVO>`
- **不修改现有 VO 结构**——避免影响已有 A2A Agent 的 JSONB 数据

### Alternatives Considered
| Alternative | Rejected Because |
|-------------|------------------|
| 直接存储完整 SDK AgentCard JSON | 会引入大量未建模字段到 JSONB，增加存储复杂性和迁移风险 |
| 扩展 AgentCardVO 以包含更多字段 | 超出本次功能范围；可作为后续增量 |
| 使用 AutoMapper 自动映射 | SDK 类型的属性名和结构与 VO 差异较大（如 `additionalInterfaces` vs `Interfaces`），自动映射反而更复杂 |

## 4. 新接口放置层

### Decision: `IAgentCardResolver` 放在 Application 层，实现在 Infrastructure 层

### Rationale
- 遵循 DDD 分层：Application 层定义接口，Infrastructure 层提供实现
- Application 层的 Query Handler 依赖 `IAgentCardResolver` 接口
- Infrastructure 层实现 `A2ACardResolverService`，内部使用 `A2A.A2ACardResolver` SDK
- 与现有 `IAgentResolver`（Application/Interfaces）模式一致

### Alternatives Considered
| Alternative | Rejected Because |
|-------------|------------------|
| 接口放在 Domain 层 | AgentCard 解析是应用层关注点（外部服务集成），不是领域逻辑 |
| 不使用接口，直接在 Handler 中调用 SDK | 违反 Constitution Principle V（Interface-Before-Implementation）；不可测试 |

## 5. API 端点设计

### Decision: `POST /api/agents/resolve-card` 接收 URL，返回解析后的 AgentCard 数据

### Rationale
- POST 而非 GET：URL 可能很长，且操作有副作用（外部 HTTP 调用）
- 路径 `/api/agents/resolve-card` 遵循现有 `/api/agents/*` 路由组织模式
- 返回结构化 DTO（包含 skills、interfaces、securitySchemes、name、description、url），不是原始 SDK AgentCard
- 前端从返回数据中提取所需字段填充表单

### Alternatives Considered
| Alternative | Rejected Because |
|-------------|------------------|
| `GET /api/agents/resolve-card?url=...` | URL 参数可能超长且包含特殊字符；GET 暗示幂等但远程端点状态可能变化 |
| 前端直接调用远程 endpoint | CORS 限制；安全隐患（暴露内网 URL 到浏览器）；无法做服务端超时控制 |

## 6. 超时与错误处理

### Decision: 后端设置 10 秒 HttpClient 超时，统一异常处理

### Rationale
- SC-001 要求 10 秒内返回结果
- `A2ACardResolver` 支持注入自定义 `HttpClient`，可配置 `Timeout`
- 支持 `CancellationToken`，前端取消请求时后端可及时中止
- 异常类型映射：
  - `HttpRequestException`（网络错误/HTTP 状态码错误）→ 502 Bad Gateway
  - `TaskCanceledException`（超时）→ 504 Gateway Timeout
  - `A2AException`（JSON 解析失败）→ 422 Unprocessable Entity
  - `UriFormatException`（无效 URL）→ 400 Bad Request

### Alternatives Considered
| Alternative | Rejected Because |
|-------------|------------------|
| 不设超时 | 远程端点无响应会导致请求无限挂起 |
| 前端超时控制 | 后端仍持有连接，浪费服务端资源 |
| 返回统一 400 | 不同错误类型需要不同的用户提示（不可达 vs 格式错误 vs 超时） |

## 7. 测试基础设施（NEEDS CLARIFICATION 解决）

### Decision: 本次功能的 tasks 阶段需首先建立测试项目

### Rationale
- Constitution Principle II (TDD) 要求所有代码必须先有测试
- 当前仓库无任何测试项目——这是历史技术债
- 至少需建立 `CoreSRE.Application.Tests`（xUnit + Moq）用于 Query Handler 单元测试
- `CoreSRE.Infrastructure.Tests` 用于 `A2ACardResolverService` 集成测试（可 mock HttpClient）
- 测试项目创建属于 tasks 阶段 Step 2（写测试），不在 plan 阶段执行

### Alternatives Considered
| Alternative | Rejected Because |
|-------------|------------------|
| 跳过测试 | 违反 Constitution NON-NEGOTIABLE 原则 |
| 仅手动测试 | 不满足 TDD 要求；不可重复；不可自动化 |

## 8. 前端解析按钮的 URL 覆写选项交互

### Decision: 仅当 AgentCard.Url ≠ 用户输入的 URL 时，显示覆写开关（默认开启）

### Rationale
- 从 spec US2 的 acceptance scenario 3 推导：URL 相同时无需显示选项
- 默认开启（使用用户 URL）因为用户主动输入的 URL 通常是可达的部署地址
- AgentCard 内部 URL 可能是内网地址或构建时生成的默认地址

### Alternatives Considered
| Alternative | Rejected Because |
|-------------|------------------|
| 始终显示开关 | URL 相同时开关无意义，增加认知负担 |
| 默认关闭 | 违反用户直觉——用户输入的是他们期望使用的地址 |

## 9. 前端返回 DTO 设计

### Decision: 新增 `ResolvedAgentCardDto` 包含 AgentCard 子集 + 元数据（name、description、url）

### Rationale
- 前端需要 AgentCard 中的 name/description 用于预填（US3）
- 前端需要 AgentCard 中的 url 用于判断是否显示覆写选项（US2）
- 现有 `AgentCardDto` 只有 skills/interfaces/securitySchemes，缺少 name/description/url
- 新增独立 DTO 而非修改 `AgentCardDto`，避免影响现有创建/更新/详情的数据流

### Alternatives Considered
| Alternative | Rejected Because |
|-------------|------------------|
| 修改现有 AgentCardDto | 会影响创建和编辑流程的数据契约 |
| 返回原始 SDK JSON | 前端需要处理完整 AgentCard 类型，增加前端复杂度 |
