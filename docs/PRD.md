# CoreSRE — 产品需求文档 (PRD)

**文档编号**: PRD-001  
**版本**: 1.0.0  
**状态**: ACTIVE  
**创建日期**: 2026-02-09  
**关联 BRD**: BRD-001  

---

## 1. 产品概述

CoreSRE 是一个基于 A2A 协议的分布式智能体编排与协同平台。它是企业 AI 运营体系的**上游编排层**：

- **上游**：管理多智能体的注册、发现、调度与编排
- **下游**：通过 MCP/API 网关对接各类工具链和数据源
- **核心价值**：让多智能体"被统筹调度并生成标准化输出"

### 1.1 技术架构总览

```
┌─────────────────────────────────────────────────────────────────┐
│                     Frontend (React + shadcn/ui)                │
│  ┌──────────┐ ┌──────────────┐ ┌───────────┐ ┌──────────────┐  │
│  │ Agent    │ │ Workflow     │ │ Tool      │ │ Monitoring   │  │
│  │ Registry │ │ Designer     │ │ Manager   │ │ Dashboard    │  │
│  └──────────┘ └──────────────┘ └───────────┘ └──────────────┘  │
├─────────────────────────────────────────────────────────────────┤
│                   API Gateway (ASP.NET Core)                    │
├─────────────────────────────────────────────────────────────────┤
│                 Application Layer (CQRS/MediatR)                │
├──────────┬──────────┬──────────┬──────────┬─────────────────────┤
│ Agent    │ Workflow │ Tool     │ AIOps    │ Observability       │
│ Registry │ Engine   │ Gateway  │ Engine   │ (OpenTelemetry)     │
│ Module   │ Module   │ Module   │ Module   │                     │
├──────────┴──────────┴──────────┴──────────┴─────────────────────┤
│                      Domain Layer                               │
├─────────────────────────────────────────────────────────────────┤
│                   Infrastructure Layer                          │
│  ┌──────┐ ┌───────┐ ┌──────────┐ ┌───────────┐ ┌───────────┐  │
│  │ EF   │ │ A2A   │ │ MCP      │ │ External  │ │ OTel      │  │
│  │ Core │ │ Proto │ │ Protocol │ │ API Proxy │ │ Exporters │  │
│  └──────┘ └───────┘ └──────────┘ └───────────┘ └───────────┘  │
├─────────────────────────────────────────────────────────────────┤
│              .NET Aspire AppHost (Orchestration)                │
│                    + Aspire Dashboard                           │
└─────────────────────────────────────────────────────────────────┘
```

### 1.2 技术选型依据（基于源码分析）

以下技术选型基于对 `.reference/codes/` 中克隆的实际源码进行无幻觉分析得出：

#### Microsoft Agent Framework（`agent-framework/dotnet/`）

| 能力 | 源码依据 | 本平台使用方式 |
|------|---------|---------------|
| `AIAgent` 抽象基类 | `RunAsync()` / `RunStreamingAsync()` 非流/流式双模式 | 所有平台内 Agent 继承此基类 |
| `ChatClientAgent` | 包装 `IChatClient`，支持 instructions/tools 注入 | 快速创建 LLM 驱动的 Agent |
| `DelegatingAIAgent` | 装饰器模式，拦截 Agent 调用 | 实现日志、审计、限流中间件 |
| `AIAgentBuilder` | 管道式 `.Use()` 扩展 | Agent 中间件链：认证→限流→审计→执行 |
| `AgentSession` + `AgentSessionStore` | 会话状态管理，可序列化为 JSON | 基于 PostgreSQL 实现自定义 `PostgresAgentSessionStore`（框架仅提供 InMemory/Noop） |
| `Workflow` + `WorkflowBuilder` | 图节点 `Executor`，`Edge`（Direct/FanOut/FanIn）| 后端工作流引擎核心 |
| `AgentWorkflowBuilder.BuildSequential/BuildConcurrent` | 便捷构建器 | 预置工作流模板 |
| `HandoffsWorkflowBuilder` | Agent 间任务交接 | AIOps 多 Agent 协作 |
| `A2AAgent` (客户端) | 通过 `A2AClient` 调用远程 Agent | 跨平台 Agent 互联 |
| `MapA2A()` (服务端) | ASP.NET Core 端点映射 | 将本平台 Agent 暴露为 A2A 服务端 |
| `IHostedAgentBuilder.AddAIAgent()` | DI 集成 | Agent 注册到 DI 容器 |

#### Microsoft.Extensions.AI（`dotnet-extensions/`）

| 能力 | 源码依据 | 本平台使用方式 |
|------|---------|---------------|
| `IChatClient` 接口 | `CompleteAsync()` / `CompleteStreamingAsync()` | 统一 LLM 调用抽象 |
| `DelegatingChatClient` | 俄罗斯套娃中间件模式 | 实现缓存、日志、OTel 装饰 |
| `ChatClientBuilder` + DI | `.AddChatClient().UseLogging().UseFunctionInvocation()` | LLM 管道配置 |
| `AIFunction` / `AITool` | 工具定义与自动调用 | MCP Tool → AITool 桥接 |
| `FunctionInvocationChatClient` | 自动 function-call 循环 | Agent 自动调用工具 |
| OpenTelemetry 集成 | GenAI 语义约定 v1.39，`gen_ai.*` 指标 | 全链路 AI 调用追踪 |

#### A2A 协议（`a2a-protocol/`）

| 能力 | 源码依据 | 本平台使用方式 |
|------|---------|---------------|
| AgentCard | 能力声明：name, skills, interfaces, securitySchemes | Agent Registry 的核心数据模型 |
| Task 生命周期 | 9 状态机：submitted→working→completed/failed/canceled | 任务跟踪与可视化 |
| Message / Part / Artifact | 多模态消息：text/file/data | Agent 通信数据格式 |
| 三种 Transport | JSON-RPC / gRPC / HTTP+JSON | 灵活选择通信方式 |
| Push Notifications | Webhook + JWT 认证 | 异步长任务结果回调 |
| Multi-tenancy URL | `/{tenant}/...` 模式 | 可扩展至多租户 |

#### MCP 协议（`mcp-specification/`）

| 能力 | 源码依据 | 本平台使用方式 |
|------|---------|---------------|
| Tool 定义 | `name` + `inputSchema` (JSON Schema) + `annotations` | 外部工具标准化描述 |
| Resource 系统 | URI 模板 + text/binary 内容 | 数据源统一抽象 |
| Capability 协商 | `initialize` 握手协商能力 | 工具能力自动发现 |
| Streamable HTTP | POST+GET+SSE | 长连接工具通信 |

#### .NET Aspire（`dotnet-aspire/`）

| 能力 | 源码依据 | 本平台使用方式 |
|------|---------|---------------|
| AppHost 编排 | `AddProject<T>()` / `WithReference()` | 一键编排所有微服务 |
| ServiceDefaults | `AddServiceDefaults()` → OTel + 健康检查 + 弹性 | 每个服务自动具备可观测性 |
| Aspire Dashboard | Blazor Server + OTLP 接收 | 开发环境一站式监控 |
| 服务发现 | `WithReference()` 自动注入连接信息 | 微服务间零配置通信 |

---

## 2. 功能模块定义

### 模块 M1：Agent Registry（智能体注册中心）

**对应 BRD**: BG-1, BG-2  
**核心职责**: 管理所有类型智能体（A2AAgent、ChatClientAgent、WorkflowAgent）的注册、发现、健康检查与生命周期

#### 智能体类型说明

| 类型 | Agent Framework 基类 | 说明 |
|------|---------------------|------|
| **A2AAgent** | `A2AAgent` (封装 `A2AClient`) | 远程智能体，通过 A2A 协议调用外部 Agent 服务 |
| **ChatClientAgent** | `ChatClientAgent` (封装 `IChatClient`) | 本地 LLM 智能体，直接调用 LLM API，支持工具绑定与 instructions |
| **WorkflowAgent** | `WorkflowHostAgent` (via `Workflow.AsAgent()`) | 工作流智能体，将已发布的工作流包装为 Agent，可被其他工作流或 Agent 复用调用 |

#### 功能需求

| ID | 需求 | 优先级 |
|----|------|--------|
| FR-M1-01 | 注册 A2AAgent（提交 AgentCard：name, description, skills, interfaces） | P1 |
| FR-M1-02 | 注册 ChatClientAgent（提交 LLM 配置：model, instructions, tools 绑定） | P1 |
| FR-M1-03 | 注册 WorkflowAgent（引用已发布的 WorkflowDefinition，通过 `Workflow.AsAgent()` 包装） | P1 |
| FR-M1-04 | 查询 Agent 列表（支持按 type/skill/tag 过滤） | P1 |
| FR-M1-05 | 获取单个 Agent 详情（完整配置信息） | P1 |
| FR-M1-06 | 更新 Agent 注册信息 | P1 |
| FR-M1-07 | 注销 Agent | P1 |
| FR-M1-08 | Agent 健康检查（定时探活） | P2 |
| FR-M1-09 | Agent 能力搜索（语义匹配 skill description） | P3 |
| FR-M1-10 | AgentSession 持久化（基于 PostgreSQL 的 `AgentSessionStore` 实现，服务重启后会话可恢复） | P1 |

#### 领域模型

```
AgentRegistration (聚合根)
├── Id: Guid
├── Name: string
├── Description: string
├── AgentType: AgentType              ← A2A / ChatClient / Workflow
├── Status: AgentStatus              ← Registered/Active/Inactive/Error
├── // === A2AAgent 专属字段 ===
├── Endpoint: Uri?                   ← A2A 服务端点（仅 A2A 类型）
├── AgentCard: AgentCardVO?          ← A2A AgentCard 值对象
│   ├── Skills: List<AgentSkillVO>
│   ├── Interfaces: List<AgentInterfaceVO>
│   └── SecuritySchemes: List<SecuritySchemeVO>
├── // === ChatClientAgent 专属字段 ===
├── LlmConfig: LlmConfigVO?          ← LLM 配置（仅 ChatClient 类型）
│   ├── ModelId: string              ← 模型名称（如 gpt-4o）
│   ├── Instructions: string         ← 系统提示词
│   └── ToolRefs: List<Guid>         ← 绑定的工具 ID 列表（引用 Tool Gateway）
├── // === WorkflowAgent 专属字段 ===
├── WorkflowRef: Guid?               ← 引用的 WorkflowDefinition ID（仅 Workflow 类型）
├── // === 公共字段 ===
├── HealthCheck: HealthCheckVO
│   ├── LastCheckTime: DateTime
│   ├── IsHealthy: bool
│   └── FailureCount: int
├── CreatedAt: DateTime
└── UpdatedAt: DateTime
```

#### API 端点

| Method | Path | 说明 |
|--------|------|------|
| POST | `/api/agents` | 注册 Agent（通过 `agentType` 字段区分类型） |
| GET | `/api/agents` | 查询 Agent 列表（支持 `?type=` 过滤） |
| GET | `/api/agents/{id}` | 获取 Agent 详情 |
| PUT | `/api/agents/{id}` | 更新 Agent |
| DELETE | `/api/agents/{id}` | 注销 Agent |
| GET | `/api/agents/{id}/health` | Agent 健康检查 |

---

### 模块 M2：Tool Gateway（工具统一接入网关）

**对应 BRD**: BG-3, 课题成果 1  
**核心职责**: 统一异构外部 API/MCP 工具的接入、认证与生命周期管理

#### 功能需求

| ID | 需求 | 优先级 |
|----|------|--------|
| FR-M2-01 | 注册外部 REST API 工具（提供 endpoint + auth config） | P1 |
| FR-M2-02 | 注册 MCP Server 工具（提供 MCP 连接配置） | P1 |
| FR-M2-03 | 从 OpenAPI/Swagger 文档自动生成工具节点 | P1 |
| FR-M2-04 | 统一凭据托管（API Key / OAuth2 / Bearer Token） | P1 |
| FR-M2-05 | 工具调用代理（统一入口，屏蔽协议差异） | P1 |
| FR-M2-06 | 配额管理（每工具调用频率限制） | P2 |
| FR-M2-07 | 熔断策略（错误率阈值自动熔断） | P2 |
| FR-M2-08 | 工具调用审计日志 | P2 |

#### 领域模型

```
ToolRegistration (聚合根)
├── Id: Guid
├── Name: string
├── Description: string
├── ToolType: ToolType               ← RestApi / McpServer
├── Status: ToolStatus               ← Active / Inactive / CircuitOpen
├── ConnectionConfig: ConnectionConfigVO
│   ├── Endpoint: Uri
│   ├── Protocol: ProtocolType       ← Http / Mcp
│   └── TransportType: TransportType ← Rest / StreamableHttp / Stdio
├── AuthConfig: AuthConfigVO
│   ├── AuthType: AuthType           ← None / ApiKey / OAuth2 / Bearer
│   └── CredentialRef: string        ← 凭据存储引用
├── ToolSchema: ToolSchemaVO         ← 从 OpenAPI/MCP 解析
│   ├── InputSchema: JsonSchema
│   ├── OutputSchema: JsonSchema
│   └── Annotations: ToolAnnotations ← readOnly, destructive, idempotent
├── QuotaConfig: QuotaConfigVO
│   ├── MaxCallsPerMinute: int
│   └── MaxCallsPerHour: int
├── CircuitBreakerConfig: CircuitBreakerConfigVO
│   ├── FailureThreshold: int
│   ├── ResetTimeoutSeconds: int
│   └── CurrentState: CircuitState
└── Metadata: Dictionary<string, string>
```

#### 工具调用流程

```
Agent 请求调用工具
  → Tool Gateway 接收请求
    → 认证 (从凭据存储获取 credentials)
    → 限流检查 (配额管理)
    → 熔断检查 (CircuitBreaker)
    → 协议适配 (REST ↔ MCP 统一为 AIFunction 调用)
    → 发起实际调用
    → 记录审计日志 + OTel Span
    → 返回标准化结果
```

---

### 模块 M3：Workflow Engine（工作流编排引擎）

**对应 BRD**: BG-4, 课题成果 3  
**核心职责**: 支持可视化定义和执行多 Agent 协作工作流

#### 功能需求

| ID | 需求 | 优先级 |
|----|------|--------|
| FR-M3-01 | 创建工作流定义（JSON 格式的 DAG 图） | P1 |
| FR-M3-02 | 工作流包含 Agent 节点（引用 Agent Registry 中的 Agent） | P1 |
| FR-M3-03 | 工作流包含 Tool 节点（引用 Tool Gateway 中的工具） | P1 |
| FR-M3-04 | 支持顺序执行（Sequential）编排 | P1 |
| FR-M3-05 | 支持并行执行（Concurrent/FanOut）编排 | P1 |
| FR-M3-06 | 支持条件分支（Conditional Edge）编排 | P1 |
| FR-M3-07 | 支持 Agent Handoff（任务交接）编排 | P1 |
| FR-M3-08 | 支持 Group Chat（多 Agent 讨论）编排 | P2 |
| FR-M3-09 | 工作流执行实例管理（启动、暂停、取消、查询状态） | P1 |
| FR-M3-10 | 工作流执行历史与回溯 | P2 |
| FR-M3-11 | 短时工具与长时工具统一编排 | P2 |
| FR-M3-12 | 工作流发布为 WorkflowAgent（通过 `Workflow.AsAgent()` 包装，注册到 Agent Registry） | P1 |

#### 与 Agent Framework 的映射

| 平台概念 | Agent Framework 类型 | 说明 |
|---------|---------------------|------|
| 工作流定义 | `Workflow` | 通过 `WorkflowBuilder` 构建 DAG |
| Agent 节点 | `AIAgentBinding` (ExecutorBinding) | `AIAgent` 隐式转换为 `ExecutorBinding` |
| 顺序编排 | `AgentWorkflowBuilder.BuildSequential()` | 链式 Agent 执行 |
| 并行编排 | `AgentWorkflowBuilder.BuildConcurrent()` | FanOut + 结果聚合 |
| 条件分支 | `WorkflowBuilder.AddEdge<T>(condition)` | 基于输出类型的条件路由 |
| 任务交接 | `HandoffsWorkflowBuilder` | Agent 间 Handoff |
| 群组讨论 | `GroupChatWorkflowBuilder` + `GroupChatManager` | 多 Agent 轮流发言 |
| **工作流→Agent** | **`WorkflowHostAgent` via `workflow.AsAgent()`** | **工作流本身也是 Agent，可被嵌套调用** |

#### 领域模型

```
WorkflowDefinition (聚合根)
├── Id: Guid
├── Name: string
├── Description: string
├── Version: int
├── Status: WorkflowStatus            ← Draft / Published / Archived
├── Graph: WorkflowGraphVO
│   ├── Nodes: List<WorkflowNodeVO>
│   │   ├── NodeId: string
│   │   ├── NodeType: NodeType        ← Agent / Tool / Condition / FanOut / FanIn
│   │   ├── AgentRef: Guid?           ← 引用 Agent Registry
│   │   ├── ToolRef: Guid?            ← 引用 Tool Gateway
│   │   └── Config: JsonElement
│   └── Edges: List<WorkflowEdgeVO>
│       ├── SourceNodeId: string
│       ├── TargetNodeId: string
│       ├── EdgeType: EdgeType        ← Direct / FanOut / FanIn
│       ├── Condition: string?        ← 条件表达式
│       └── Label: string?
├── CreatedBy: string
└── CreatedAt: DateTime

WorkflowExecution (聚合根)
├── Id: Guid
├── WorkflowDefinitionId: Guid
├── Status: ExecutionStatus           ← Pending / Running / Paused / Completed / Failed / Canceled
├── Input: JsonElement
├── Output: JsonElement?
├── StartedAt: DateTime
├── CompletedAt: DateTime?
├── NodeExecutions: List<NodeExecutionVO>
│   ├── NodeId: string
│   ├── Status: NodeExecutionStatus
│   ├── Input: JsonElement
│   ├── Output: JsonElement?
│   ├── StartedAt: DateTime
│   └── CompletedAt: DateTime?
└── TraceId: string                    ← OpenTelemetry Trace ID
```

---

### 模块 M4：AIOps Engine（智能运维引擎）

**对应 BRD**: BG-5, 课题成果 4  
**核心职责**: 完成至少一个 AIOps 自动化运维闭环场景

#### 场景：智能告警联动 + 多维根因分析（基于 Handoff 模式）

```
告警事件
  → 告警接收 Agent (接收 webhook/轮询)
    → [任务交接 Handoff] 告警聚合 Agent (去重、归类、优先级)
      → [任务交接 Handoff] 根因分析 Agent (关联日志/指标/拓扑，LLM 推理)
        → [任务交接 Handoff] 修复建议 Agent (生成修复方案)
          → [人工审批卡点 — 需登录用户身份认证]
            → 自动修复 Agent (执行修复操作)
              → 验证 Agent (确认修复效果)
                → 通知 Agent (发送处理报告)
```

> **编排模式说明**: AIOps 场景采用 **Handoff 任务交接模式**（`HandoffsWorkflowBuilder`），每个 Agent 处理完自己的职责后，将工作连同上下文交接给下一个最合适的 Agent。这种模式的优势：每个 Agent 可以动态决定交接给谁（如根据告警严重程度跳过聚合直接分析），而非固定顺序。人工审批卡点通过工作流暂停机制实现，审批时需通过身份认证确认操作者身份。

#### 功能需求

| ID | 需求 | 优先级 |
|----|------|--------|
| FR-M4-01 | 告警事件接收（Webhook 接入） | P1 |
| FR-M4-02 | 告警聚合与去重 | P1 |
| FR-M4-03 | LLM 驱动的根因分析（基于日志 + 指标关联） | P1 |
| FR-M4-04 | 自动生成修复建议 | P1 |
| FR-M4-05 | 人工审批卡点（Human-in-the-loop） | P1 |
| FR-M4-06 | 自动执行修复操作（调用外部工具） | P2 |
| FR-M4-07 | 修复效果验证 | P2 |
| FR-M4-08 | 处理报告生成与通知 | P2 |

#### 领域模型

```
AlertEvent (聚合根)
├── Id: Guid
├── Source: string
├── Severity: AlertSeverity           ← Critical / Warning / Info
├── Title: string
├── Description: string
├── Labels: Dictionary<string, string>
├── ReceivedAt: DateTime
├── Status: AlertStatus               ← New / Acknowledged / Investigating / Resolved
└── CorrelationId: string?

RootCauseAnalysis (实体)
├── Id: Guid
├── AlertEventId: Guid
├── AnalysisResult: string            ← LLM 生成的分析结果
├── RelatedMetrics: List<MetricDataVO>
├── RelatedLogs: List<LogEntryVO>
├── Confidence: double
├── SuggestedActions: List<ActionSuggestionVO>
└── AnalyzedAt: DateTime

RemediationAction (实体)
├── Id: Guid
├── AnalysisId: Guid
├── ActionType: ActionType            ← Restart / Scale / Rollback / Custom
├── Description: string
├── ToolRef: Guid                     ← 引用 Tool Gateway
├── ApprovalStatus: ApprovalStatus    ← Pending / Approved / Rejected
├── ExecutionStatus: ExecutionStatus
├── ApprovedBy: string?
└── ExecutedAt: DateTime?
```

---

### 模块 M5：Observability（可观测性）

**对应 BRD**: BG-6  
**核心职责**: 全链路 Traces/Metrics/Logs，集成 Aspire Dashboard

#### 功能需求

| ID | 需求 | 优先级 |
|----|------|--------|
| FR-M5-01 | 所有 Agent 调用自动生成 OTel Trace Span | P1 |
| FR-M5-02 | 工作流执行端到端 Trace 串联 | P1 |
| FR-M5-03 | 工具调用 Span（包含延迟、状态、错误） | P1 |
| FR-M5-04 | LLM 调用指标（token 用量、延迟、模型名） | P2 |
| FR-M5-05 | Agent 状态面板（前端可视化） | P2 |
| FR-M5-06 | 结构化日志聚合 | P2 |

#### 技术实现

```
.NET Aspire ServiceDefaults
  → AddOpenTelemetry()
    → Traces: ASP.NET Core + HttpClient + Agent Framework GenAI 语义约定
    → Metrics: gen_ai.client.operation.duration, gen_ai.client.token.usage
    → Logs: 结构化日志 → OTLP 导出
  → Aspire Dashboard 接收 OTLP
    → 实时展示 Traces / Metrics / Logs
```

---

### 模块 M6：Security & Governance（安全认证 + 安全与治理）

**对应 BRD**: BG-7, 课题需求 5  
**核心职责**: 身份认证（JWT）是平台基础设施的前置依赖——人工审批（FR-M6-04）需要知道"谁在审批"，因此认证模块必须在 M4 AIOps 之前就绪。本模块还负责 RBAC 权限管理、Agent 工具访问控制、操作审计等治理能力。

> ⚠️ **FR-M6-01 身份认证是 P0 优先级**，属于前置基础设施，应在 Sprint 0 与 Aspire 基础设施一起完成。

#### 功能需求

| ID | 需求 | 优先级 |
|----|------|--------|
| FR-M6-01 | 身份认证（JWT 登录，支持用户名/密码登录获取 Token） | P0 |
| FR-M6-02 | 角色与权限管理（Admin/Operator/Viewer RBAC） | P1 |
| FR-M6-03 | 工具调用权限控制（Agent 级别的工具访问白名单） | P1 |
| FR-M6-04 | 危险操作人工审批（destructive 注解的工具需审批，审批者必须已认证） | P1 |
| FR-M6-05 | 操作审计日志（谁、何时、对什么、做了什么） | P1 |
| FR-M6-06 | Agent 调用频率限制 | P2 |
| FR-M6-07 | 敏感数据脱敏（日志中的 credentials、PII） | P2 |

---

### 模块 M7：Frontend（前端可视化）

**对应 BRD**: BG-4, BG-6  
**技术栈**: React + Vite + SWC + shadcn/ui + Tailwind CSS

#### 页面规划

| 页面 | 路由 | 功能 |
|------|------|------|
| Login | `/login` | 登录页，获取 JWT Token |
| Dashboard | `/` | 系统概览：Agent 数量（按类型统计）、活跃工作流、告警统计 |
| Agent Registry | `/agents` | Agent 列表（按类型筛选: A2A/ChatClient/Workflow）、注册、详情、健康状态 |
| Agent Detail | `/agents/:id` | 单个 Agent 详情（根据类型展示不同配置：AgentCard / LLM配置 / 工作流引用） |
| Tool Manager | `/tools` | 工具列表、注册、导入 OpenAPI |
| Workflow Designer | `/workflows` | 可视化工作流编排（DAG 画布） |
| Workflow Detail | `/workflows/:id` | 工作流执行历史、节点状态 |
| Workflow Execution | `/workflows/:id/executions/:execId` | 单次执行详情、实时状态流 |
| AIOps | `/aiops` | 告警列表、根因分析、修复审批 |
| Alert Detail | `/aiops/alerts/:id` | 单个告警详情、分析过程、修复操作 |
| Settings | `/settings` | 系统配置、凭据管理 |

---

## 3. 非功能需求

| 类别 | 需求 | 指标 |
|------|------|------|
| 性能 | 单 Agent 调用响应 | < 500ms (不含 LLM 推理) |
| 性能 | 工作流编排调度延迟 | < 100ms |
| 可用性 | 核心 API 可用率 | ≥ 99% (开发环境) |
| 安全 | 凭据存储 | 加密存储，运行时解密 |
| 可观测性 | Trace 覆盖率 | 100% API 调用 |
| 可扩展性 | 架构支持横向扩展 | 通过 Aspire + K8s |

## 4. 技术约束

| 约束 | 具体要求 |
|------|---------|
| 后端框架 | .NET 10 + ASP.NET Core Minimal API |
| Agent 框架 | Microsoft Agent Framework (`Microsoft.Agents.AI.*`) |
| AI 抽象 | Microsoft.Extensions.AI (`IChatClient`, `AITool`) |
| 协议 | A2A (Agent-to-Agent) + MCP (Model Context Protocol) |
| 前端 | React 19 + Vite 7 + SWC + shadcn/ui + Tailwind CSS v4 |
| 编排 | .NET Aspire (AppHost + ServiceDefaults + Dashboard) |
| 可观测性 | OpenTelemetry (OTLP → Aspire Dashboard) |
| 数据库 | PostgreSQL（通过 EF Core + Npgsql；Aspire 编排 PostgreSQL 容器） |
| 会话持久化 | 自定义 `PostgresAgentSessionStore : AgentSessionStore`，将 AgentSession JSON 序列化存储到 PostgreSQL |
| AI Provider | OpenAI Compatible API |
| 架构模式 | DDD + CQRS + TDD + SDD |

## 5. 数据流全景

```
                    ┌─────────────────┐
                    │   React 前端     │
                    └────────┬────────┘
                             │ HTTP
                    ┌────────▼────────┐
                    │  API Gateway    │
                    │  (Minimal API)  │
                    └────────┬────────┘
                             │ MediatR Command/Query
              ┌──────────────┼──────────────┐
              │              │              │
     ┌────────▼──────┐ ┌────▼─────┐ ┌──────▼──────┐
     │ Agent Registry │ │ Workflow │ │ Tool Gateway│
     │   Service      │ │ Engine   │ │   Service   │
     └────────┬──────┘ └────┬─────┘ └──────┬──────┘
              │              │              │
     ┌────────▼──────────────▼──────────────▼──────┐
     │              Domain Layer                    │
     │  AgentRegistration  WorkflowDefinition  ...  │
     └──────────────────────┬──────────────────────┘
                            │
     ┌──────────────────────▼──────────────────────┐
     │           Infrastructure Layer               │
     │  ┌──────┐ ┌──────┐ ┌──────┐ ┌────────────┐ │
     │  │EFCore│ │A2A   │ │MCP   │ │External API│ │
     │  │(PgSQL)│ │Client│ │Client│ │  Proxy     │ │
     │  └──────┘ └──────┘ └──────┘ └────────────┘ │
     │  ┌─────────────────────────┐                │
     │  │PostgresAgentSessionStore│ ← 会话持久化   │
     │  └─────────────────────────┘                │
     └─────────────────────────────────────────────┘
```

## 6. 开发策略：前后端增量协同

> **核心原则**: 每个功能模块的前端页面随后端 API 同步开发，而非先完成全部后端再集中开发前端。

| 迭代 | 后端模块 | 同步开发的前端页面 | 交付物 |
|------|---------|-------------------|--------|
| Sprint 0 | M0 Aspire 基础设施 + M6 JWT 认证 | Login 页面 + 路由框架 | 可登录的空壳应用 |
| Sprint 1 | M1 Agent Registry | Agent 管理页面（列表 + 注册 + 详情） | Agent CRUD 全栈闭环 |
| Sprint 2 | M2 Tool Gateway | 工具管理页面（列表 + 注册 + 导入） | 工具管理全栈闭环 |
| Sprint 3 | M3 Workflow Engine | 工作流编排器 + 执行监控页面 | 可视化编排全栈闭环 |
| Sprint 4 | M4 AIOps Engine | AIOps 告警 + 修复审批页面 | AIOps 端到端闭环 |
| Sprint 5 | M5 Observability + Dashboard | Dashboard 首页 + Agent 状态面板 | 可观测性全栈闭环 |

这种增量策略的优势：
- 每个 Sprint 交付可演示的全栈功能切片
- 前端开发尽早发现 API 设计问题
- 避免后期前后端集成时的大量联调工作
