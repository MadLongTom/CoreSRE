# CoreSRE — Spec 总览（分解清单）

**文档编号**: SPEC-INDEX  
**版本**: 1.0.0  
**创建日期**: 2026-02-09  
**关联文档**: [BRD](BRD.md) | [PRD](PRD.md)  

> 本文档列出所有待编写的详细 Spec。每条 Spec 包含：编号、归属模块、简述、涉及的核心领域模型、对外端点以及与第三方库的映射关系。按模块和优先级排列，P1 先做，P2/P3 后续迭代。

---

## 模块 M0：Aspire AppHost 编排与 ServiceDefaults

> 基础设施 Spec，所有模块的前置依赖。

### SPEC-000: Aspire AppHost 编排与 ServiceDefaults 配置

**优先级**: P0（前置基础设施）  
**对应 BRD**: 技术约束  
**对应 PRD**: 技术架构总览  

**简述**: 搭建 .NET Aspire AppHost 项目（`CoreSRE.AppHost`）和 ServiceDefaults 项目（`CoreSRE.ServiceDefaults`），将后端 API、未来的各微服务通过 `AddProject<T>()` 编排，统一配置 OpenTelemetry（Traces/Metrics/Logs）、健康检查、HTTP 弹性策略（Polly）。Aspire Dashboard 作为开发环境的一站式可观测性面板。

**核心工作**:
- 创建 `CoreSRE.AppHost` 项目，引用 API 项目、PostgreSQL 资源
- 创建 `CoreSRE.ServiceDefaults`，封装 `AddServiceDefaults()` 扩展方法
- 配置 OTLP 导出到 Aspire Dashboard
- 配置 HTTP 弹性策略（重试、超时、熔断）
- 通过 Aspire `AddPostgres()` 编排 PostgreSQL 容器，开发环境零配置
- 所有服务自动具备健康检查端点

**第三方库映射**:
- `dotnet-aspire/src/Aspire.Hosting/` → `AddProject<T>()`, `WithReference()`
- `dotnet-aspire/src/Aspire.Hosting.PostgreSQL/` → `AddPostgres()`, `AddDatabase()`
- `dotnet-aspire/src/Aspire.Hosting.AppHost/` → AppHost 入口模式
- `opentelemetry-dotnet/` → OTel SDK 配置

---

## 模块 M1：Agent Registry（智能体注册中心）

### SPEC-001: Agent 注册与 CRUD 管理（多类型）

**优先级**: P1  
**对应 PRD**: FR-M1-01 ~ FR-M1-07  

**简述**: 实现多类型 Agent 的完整生命周期管理——注册、查询列表、获取详情、更新、注销。系统支持三种 Agent 类型：
- **A2AAgent**: 提交 A2A 协议的 AgentCard 信息（name, description, skills, interfaces, securitySchemes, endpoint）
- **ChatClientAgent**: 提交 LLM 配置（modelId, instructions, toolRefs）
- **WorkflowAgent**: 引用已发布的 WorkflowDefinition，通过 `Workflow.AsAgent()` 包装

注册 API 统一入口，通过 `agentType` 字段区分类型，根据类型校验不同的必填字段。数据持久化到数据库。

**领域模型**: `AgentRegistration`（聚合根）、`AgentType` 枚举（A2A/ChatClient/Workflow）、`AgentCardVO`、`LlmConfigVO`（ModelId, Instructions, ToolRefs）、`AgentSkillVO`、`AgentInterfaceVO`、`SecuritySchemeVO`  
**端点**: `POST/GET /api/agents`（支持 `?type=` 过滤）, `GET/PUT/DELETE /api/agents/{id}`  

**第三方库映射**:
- `a2a-protocol/specification/json/a2a.json` → AgentCard schema 定义（仅 A2A 类型）
- `agent-framework/dotnet/src/Microsoft.Agents.A2A/` → `A2AAgent`, `MapA2A()` 端点映射
- `agent-framework/dotnet/src/Microsoft.Agents.AI/` → `ChatClientAgent`（封装 `IChatClient`）
- `agent-framework/dotnet/src/Microsoft.Agents.AI/Workflows/` → `WorkflowHostAgent` via `Workflow.AsAgent()`

---

### SPEC-002: Agent 健康检查与状态管理

**优先级**: P2  
**对应 PRD**: FR-M1-06  

**简述**: 为已注册的 Agent 提供定时健康探活机制。通过后台定时任务（`BackgroundService`）周期性向 Agent 的 A2A 端点发送探活请求，根据响应更新 Agent 状态（Active/Inactive/Error）。连续失败超过阈值自动标记为 Inactive。

**领域模型**: `HealthCheckVO`（LastCheckTime, IsHealthy, FailureCount）  
**端点**: `GET /api/agents/{id}/health`  

**第三方库映射**:
- `a2a-protocol/` → AgentCard.url 作为探活端点
- `dotnet-aspire/` → 健康检查基础设施

---

### SPEC-003: Agent 能力语义搜索

**优先级**: P3  
**对应 PRD**: FR-M1-07  

**简述**: 支持按自然语言描述搜索 Agent 的 skill。基于 Agent 注册时的 skill description 字段，使用向量嵌入（调用 LLM Embedding API）进行语义匹配，返回最相关的 Agent 列表。初期可用关键词模糊匹配实现，后续升级为向量搜索。

**领域模型**: `AgentRegistration` 上的 skills 字段  
**端点**: `GET /api/agents/search?q={query}`  

**第三方库映射**:
- `dotnet-extensions/src/Libraries/Microsoft.Extensions.AI.Abstractions/` → `IEmbeddingGenerator<string, Embedding<float>>`

---

### SPEC-004: AgentSession PostgreSQL 持久化

**优先级**: P1  
**对应 PRD**: FR-M1-10  

**简述**: 实现基于 PostgreSQL 的 `AgentSessionStore` 子类（`PostgresAgentSessionStore`），使 Agent 会话在服务重启后可恢复。Agent Framework 仅提供 `InMemoryAgentSessionStore`（开发用）和 `NoopAgentSessionStore`（无持久化），没有任何数据库实现。

**核心设计**:
1. 继承 `AgentSessionStore` 抽象类，实现 `SaveSessionAsync(agent, conversationId, session)` 和 `GetSessionAsync(agent, conversationId)`
2. 内部通过 `AIAgent.SerializeSession()` 将会话序列化为 `JsonElement`，存储到 PostgreSQL `agent_sessions` 表
3. 恢复时通过 `AIAgent.DeserializeSessionAsync()` 反序列化
4. 主键为复合键 `(agentId, conversationId)`
5. 通过 `IHostedAgentBuilder.WithSessionStore()` 注册到 DI

**数据库表设计**:
```sql
CREATE TABLE agent_sessions (
    agent_id        VARCHAR(255)  NOT NULL,
    conversation_id VARCHAR(255)  NOT NULL,
    session_data    JSONB         NOT NULL,  -- AgentSession 序列化 JSON
    session_type    VARCHAR(100)  NOT NULL,  -- 会话类型（ChatClientAgentSession 等）
    created_at      TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    PRIMARY KEY (agent_id, conversation_id)
);
```

**领域模型**: `AgentSessionRecord`（实体，AgentId, ConversationId, SessionData: JsonElement, SessionType, CreatedAt, UpdatedAt）  
**端点**: 无直接 API 端点（通过 Agent Framework 内部调用）  

**第三方库映射**:
- `agent-framework/dotnet/src/Microsoft.Agents.AI/` → `AgentSessionStore`（抽象基类）, `AIAgent.SerializeSession()` / `DeserializeSessionAsync()`
- `agent-framework/dotnet/src/Microsoft.Agents.AI.Hosting/` → `WithSessionStore()` DI 注册
- `Npgsql.EntityFrameworkCore.PostgreSQL` → PostgreSQL + JSONB 存储

---

## 模块 M2：Tool Gateway（工具统一接入网关）

### SPEC-010: REST API 工具注册与管理

**优先级**: P1  
**对应 PRD**: FR-M2-01, FR-M2-04  

**简述**: 支持手动注册外部 REST API 工具。注册时提供：工具名称、描述、端点 URL、认证配置（ApiKey/OAuth2/Bearer）。凭据加密存储。工具注册后进入 Active 状态，可被 Agent 通过 Tool Gateway 统一调用。

**领域模型**: `ToolRegistration`（聚合根）、`ConnectionConfigVO`、`AuthConfigVO`  
**端点**: `POST/GET /api/tools`, `GET/PUT/DELETE /api/tools/{id}`  

**第三方库映射**:
- `dotnet-extensions/src/Libraries/Microsoft.Extensions.AI.Abstractions/` → `AIFunction`, `AITool` 抽象

---

### SPEC-011: MCP Server 工具注册与管理

**优先级**: P1  
**对应 PRD**: FR-M2-02  

**简述**: 支持注册外部 MCP Server 作为工具源。注册时提供 MCP Server 的连接配置（Endpoint URL, Transport 类型: StreamableHttp/Stdio）。注册后平台通过 MCP `initialize` 握手自动发现该 Server 暴露的所有 Tool，并将其作为子工具项纳入管理。

**领域模型**: `ToolRegistration`（ToolType=McpServer）、`ToolSchemaVO`  
**端点**: `POST /api/tools`（type=mcp）, `GET /api/tools/{id}/mcp-tools`  

**第三方库映射**:
- `mcp-specification/docs/specification/` → MCP Tool 定义（name, inputSchema, annotations）
- `mcp-specification/` → initialize 握手流程、tools/list 方法

---

### SPEC-012: OpenAPI 文档自动导入生成工具节点

**优先级**: P1  
**对应 PRD**: FR-M2-03  

**简述**: 用户上传 OpenAPI/Swagger JSON 或 YAML 文档，系统自动解析每个 path+method 为一个独立的工具节点。自动提取：工具名（operationId 或 method+path）、描述（summary）、输入 Schema（parameters + requestBody）、输出 Schema（responses）。批量生成 `ToolRegistration` 记录。

**领域模型**: `ToolRegistration`（批量创建）、`ToolSchemaVO`（InputSchema, OutputSchema 从 OpenAPI 映射）  
**端点**: `POST /api/tools/import-openapi`（上传文件或 URL）  

**第三方库映射**:
- `Microsoft.OpenApi` NuGet 包 → OpenAPI 文档解析
- `dotnet-extensions/` → AIFunction.Create() 动态生成 AIFunction

---

### SPEC-013: 工具调用代理（统一调用入口）

**优先级**: P1  
**对应 PRD**: FR-M2-05  

**简述**: 提供统一的工具调用 API，屏蔽底层协议差异。Agent 或 Workflow 通过 `POST /api/tools/{id}/invoke` 发起调用，Gateway 自动完成：1) 从凭据存储获取认证信息 2) 根据工具类型选择调用协议（REST HTTP / MCP call_tool）3) 序列化请求、反序列化响应 4) 记录 OTel Span 和审计日志 5) 返回标准化结果。

**领域模型**: `ToolInvocation`（值对象，记录调用上下文）  
**端点**: `POST /api/tools/{id}/invoke`  

**第三方库映射**:
- `agent-framework/` → `AITool` / `AIFunction` 调用机制
- `dotnet-extensions/` → `FunctionInvocationChatClient` 自动 function-call 循环
- `mcp-specification/` → `tools/call` 方法

---

### SPEC-014: 工具配额管理与熔断

**优先级**: P2  
**对应 PRD**: FR-M2-06, FR-M2-07  

**简述**: 为每个工具配置调用频率限制（MaxCallsPerMinute/Hour）和熔断策略（失败阈值、重置超时）。调用前检查配额，超限返回 429；错误率超阈值触发熔断，CircuitOpen 期间直接拒绝调用。使用 Polly（通过 Aspire ServiceDefaults 的 HTTP 弹性管道）实现。

**领域模型**: `QuotaConfigVO`、`CircuitBreakerConfigVO`、`ToolRegistration.Status` (CircuitOpen 状态)  
**端点**: `PUT /api/tools/{id}/quota`, `GET /api/tools/{id}/circuit-status`  

**第三方库映射**:
- `dotnet-extensions/src/Libraries/Microsoft.Extensions.Http.Resilience/` → Polly 弹性管道
- `dotnet-aspire/` → ServiceDefaults 中的 `AddHttpClientDefaults()` 弹性配置

---

### SPEC-015: 工具调用审计日志

**优先级**: P2  
**对应 PRD**: FR-M2-08  

**简述**: 记录每次工具调用的完整审计信息：调用者（AgentId/WorkflowId）、目标工具、请求参数（脱敏）、响应结果、耗时、状态（成功/失败/熔断）。审计日志持久化到数据库，并通过 OTel Logs 导出到 Aspire Dashboard。支持按时间范围、工具、调用者查询审计记录。

**领域模型**: `AuditLog`（实体）  
**端点**: `GET /api/audit-logs?toolId={}&agentId={}&from={}&to={}`  

**第三方库映射**:
- `opentelemetry-dotnet/` → 结构化日志导出
- `agent-framework/` → `DelegatingAIAgent` 装饰器模式拦截调用

---

## 模块 M3：Workflow Engine（工作流编排引擎）

### SPEC-020: 工作流定义 CRUD

**优先级**: P1  
**对应 PRD**: FR-M3-01 ~ FR-M3-03  

**简述**: 支持创建、查询、更新、删除工作流定义。工作流以 JSON 格式描述 DAG 图（有向无环图），包含节点列表和边列表。节点类型有：Agent（引用 Agent Registry）、Tool（引用 Tool Gateway）、Condition（条件分支）、FanOut（并行分发）、FanIn（聚合汇总）。边描述节点间的连接关系和条件。

**领域模型**: `WorkflowDefinition`（聚合根）、`WorkflowGraphVO`、`WorkflowNodeVO`、`WorkflowEdgeVO`  
**端点**: `POST/GET /api/workflows`, `GET/PUT/DELETE /api/workflows/{id}`  

**第三方库映射**:
- `agent-framework/dotnet/src/Microsoft.Agents.AI/Workflows/` → `Workflow`, `WorkflowBuilder`, `ExecutorNode`, `Edge`, `EdgeType`

---

### SPEC-021: 工作流执行引擎（顺序 + 并行 + 条件分支）

**优先级**: P1  
**对应 PRD**: FR-M3-04 ~ FR-M3-06, FR-M3-09  

**简述**: 将 `WorkflowDefinition` 的 JSON DAG 转换为 Agent Framework 的 `Workflow` 对象并执行。支持三种基础编排模式：1) 顺序（Sequential）— 节点按线性顺序执行 2) 并行（FanOut/FanIn）— 多个节点并发执行后聚合结果 3) 条件分支（Conditional Edge）— 根据上一步输出选择下游节点。每次执行创建 `WorkflowExecution` 记录，实时更新各节点执行状态。

**领域模型**: `WorkflowExecution`（聚合根）、`NodeExecutionVO`  
**端点**: `POST /api/workflows/{id}/execute`, `GET /api/workflows/{id}/executions`, `GET /api/workflows/{id}/executions/{execId}`  

**第三方库映射**:
- `agent-framework/` → `AgentWorkflowBuilder.BuildSequential()`, `BuildConcurrent()`, `WorkflowBuilder.AddEdge<T>(condition)`
- `agent-framework/` → `Workflow.AsAgent()` 将工作流转为 Agent 执行

---

### SPEC-022: Agent Handoff 编排（AIOps 核心编排模式）

**优先级**: P1  
**对应 PRD**: FR-M3-07, FR-M4 AIOps 场景  

**简述**: 支持 Agent Handoff（任务交接）模式的工作流编排。一个 Agent 在处理过程中可以将任务交给另一个更合适的 Agent 继续处理。**这是 AIOps 端到端场景的核心编排模式**——告警接收 Agent → 聚合 Agent → 根因分析 Agent → 修复 Agent 的链路通过 Handoff 动态交接实现，每个 Agent 可根据上下文决定交接给谁（如高严重度告警跳过聚合直接分析）。在工作流定义中，通过 `HandoffNodeType` 标记交接节点，运行时由 Agent Framework 的 `HandoffsWorkflowBuilder` 驱动。交接时携带完整的对话上下文。

**领域模型**: `WorkflowNodeVO`（NodeType=Handoff）  
**端点**: 复用 SPEC-021 的执行端点  

**第三方库映射**:
- `agent-framework/dotnet/src/Microsoft.Agents.AI/Workflows/` → `HandoffsWorkflowBuilder`, `HandoffInfo`

---

### SPEC-023: Group Chat 多 Agent 协商编排

**优先级**: P2  
**对应 PRD**: FR-M3-08  

**简述**: 支持 Group Chat（群组讨论）模式的工作流编排。多个 Agent 在同一会话中轮流发言讨论，由 `GroupChatManager` 控制发言顺序和终止条件。适用于需要多角色协商决策的场景（如：SRE 分析 Agent + 安全审计 Agent + 业务 Agent 共同评估修复方案）。

**领域模型**: `WorkflowNodeVO`（NodeType=GroupChat）、GroupChat 配置（MaxRounds, TerminationCondition）  
**端点**: 复用 SPEC-021 的执行端点  

**第三方库映射**:
- `agent-framework/` → `GroupChatWorkflowBuilder`, `GroupChatManager`, `RoundRobinGroupChatManager`

---

### SPEC-024: 工作流执行暂停、取消与回溯

**优先级**: P2  
**对应 PRD**: FR-M3-09, FR-M3-10  

**简述**: 支持对运行中的工作流实例进行暂停（Pause）和取消（Cancel）操作。暂停时保存当前执行状态（正在运行的节点标记为 Suspended），可后续恢复（Resume）。取消时标记所有未完成节点为 Canceled。所有执行历史（包含每个节点的输入/输出、耗时、状态变更）持久化，支持回溯查看。

**领域模型**: `WorkflowExecution.Status`（新增 Paused 状态）、`NodeExecutionVO`  
**端点**: `POST /api/workflows/{wfId}/executions/{execId}/pause`, `/resume`, `/cancel`  

---

### SPEC-025: 短时工具与长时工具统一编排

**优先级**: P2  
**对应 PRD**: FR-M3-11, 课题成果 3  

**简述**: 在工作流中区分两类工具节点：1) 短时工具（Ephemeral）——一次性调用，立即返回结果（如查询 API、执行命令）2) 长时工具（Daemon）——启动后持续运行，定期推送状态（如持续监控、日志采集）。短时工具作为普通节点执行；长时工具作为后台任务启动，通过 A2A Push Notification 或轮询获取状态更新，工作流可设置等待条件或超时。

**领域模型**: `WorkflowNodeVO`（新增 ToolDuration: Ephemeral/Daemon）  
**端点**: 复用 SPEC-021 执行端点  

**第三方库映射**:
- `a2a-protocol/` → Task 生命周期（submitted → working → completed）、PushNotificationConfig
- `mcp-specification/` → Tool annotations（`open_world_hint`, readOnly 等）

---

### SPEC-026: 工作流发布为 WorkflowAgent

**优先级**: P1  
**对应 PRD**: FR-M3-12  

**简述**: 支持将已发布（Published 状态）的 WorkflowDefinition 包装为 WorkflowAgent，注册到 Agent Registry。底层通过 `Workflow.AsAgent()` 创建 `WorkflowHostAgent` 实例。WorkflowAgent 注册后行为与普通 Agent 一致——可被其他工作流引用为节点、可通过 A2A 协议被外部系统调用。发布时验证工作流 DAG 有效性（无环、无悬空节点）。WorkflowAgent 的 AgentType 为 `Workflow`，其 WorkflowRef 字段指向源 WorkflowDefinition。

**领域模型**: `AgentRegistration`（AgentType=Workflow, WorkflowRef=工作流ID）  
**端点**: `POST /api/workflows/{id}/publish-as-agent`  

**第三方库映射**:
- `agent-framework/dotnet/src/Microsoft.Agents.AI/Workflows/` → `WorkflowHostAgent`, `Workflow.AsAgent()`

---

## 模块 M4：AIOps Engine（智能运维引擎）

### SPEC-030: 告警事件接收与聚合

**优先级**: P1  
**对应 PRD**: FR-M4-01, FR-M4-02  

**简述**: 提供 Webhook 端点接收外部告警系统推送的告警事件（兼容 Alertmanager webhook 格式）。接收后创建 `AlertEvent` 记录。实现告警聚合逻辑：基于 Labels（如 service, instance, alertname）在时间窗口内去重和归类，将多条相关告警合并为一个告警组，避免告警风暴。

**领域模型**: `AlertEvent`（聚合根）、`AlertGroupVO`  
**端点**: `POST /api/aiops/alerts/webhook`, `GET /api/aiops/alerts`  

---

### SPEC-031: LLM 驱动的根因分析

**优先级**: P1  
**对应 PRD**: FR-M4-03, FR-M4-04  

**简述**: 对告警事件触发根因分析流程。分析 Agent 自动：1) 拉取关联的指标数据（通过 Tool Gateway 调用 Prometheus/Metrics 工具）2) 拉取关联的日志数据（通过 Tool Gateway 调用日志工具）3) 将告警描述 + 指标 + 日志上下文发送给 LLM 进行根因推理 4) LLM 返回根因判断和修复建议。结果存储为 `RootCauseAnalysis` 记录。

**领域模型**: `RootCauseAnalysis`（实体）、`MetricDataVO`、`LogEntryVO`、`ActionSuggestionVO`  
**端点**: `POST /api/aiops/alerts/{id}/analyze`, `GET /api/aiops/alerts/{id}/analysis`  

**第三方库映射**:
- `agent-framework/` → `ChatClientAgent` 包装 LLM 进行推理
- `dotnet-extensions/` → `IChatClient.CompleteAsync()` 调用 LLM

---

### SPEC-032: 修复操作与人工审批

**优先级**: P1  
**对应 PRD**: FR-M4-05, FR-M4-06  

**简述**: 根因分析完成后生成修复建议（`RemediationAction`）。对于标记为 destructive 的修复操作（如重启服务、回滚版本），强制进入人工审批流程（Human-in-the-loop）。审批通过后，系统通过 Tool Gateway 调用对应的修复工具执行操作。审批和执行全过程记录审计日志。

**领域模型**: `RemediationAction`（实体）、`ApprovalStatus`、`ExecutionStatus`  
**端点**: `POST /api/aiops/actions/{id}/approve`, `/reject`, `/execute`, `GET /api/aiops/actions`  

**第三方库映射**:
- `mcp-specification/` → Tool annotations.destructive 标注需要审批
- `agent-framework/` → `DelegatingAIAgent` 在执行链中插入审批拦截

---

### SPEC-033: AIOps 工作流编排（端到端闭环）

**优先级**: P1  
**对应 PRD**: FR-M4（场景总览）, 课题成果 4  

**简述**: 将 SPEC-030 ~ SPEC-032 的各环节串联为一个完整的 AIOps 工作流定义。使用 **Handoff 任务交接模式**（`HandoffsWorkflowBuilder`）编排端到端流程：告警接收 Agent → [Handoff] 告警聚合 Agent → [Handoff] 根因分析 Agent → [Handoff] 修复建议 Agent → [人工审批卡点，需 JWT 认证] → [Handoff] 自动修复 Agent → [Handoff] 验证 Agent → 通知 Agent。每个 Agent 可根据上下文动态决定交接目标（如高严重度告警跳过聚合直接进入分析）。此工作流发布为 WorkflowAgent（通过 `Workflow.AsAgent()`），可被外部系统通过 A2A 协议调用。

**领域模型**: 复用 M3 和 M4 的领域模型  
**端点**: 复用工作流端点 + AIOps 端点  

**第三方库映射**:
- `agent-framework/` → `HandoffsWorkflowBuilder` 实现 Agent 间动态任务交接
- `agent-framework/` → `Workflow.AsAgent()` 将 AIOps 工作流发布为 WorkflowAgent

---

## 模块 M5：Observability（可观测性）

### SPEC-040: Agent 调用全链路追踪

**优先级**: P1  
**对应 PRD**: FR-M5-01, FR-M5-02, FR-M5-03  

**简述**: 为所有 Agent 调用、工作流执行、工具调用自动生成 OpenTelemetry Trace Span。Span 层级：Workflow Execution → Node Execution → Agent RunAsync → LLM CompleteAsync → Tool Invoke。每层 Span 携带必要属性（agent.name, tool.name, workflow.id 等）。通过 OTLP 导出到 Aspire Dashboard，实现端到端链路可视化。

**领域模型**: `WorkflowExecution.TraceId` 关联 OTel Trace  
**端点**: 无新端点（通过 Aspire Dashboard 查看）  

**第三方库映射**:
- `opentelemetry-dotnet/src/OpenTelemetry/` → TracerProvider, ActivitySource
- `dotnet-extensions/` → GenAI 语义约定（`gen_ai.system`, `gen_ai.operation.name`, `gen_ai.request.model`）
- `dotnet-aspire/` → ServiceDefaults 中 `AddOpenTelemetry()` 自动配置

---

### SPEC-041: Agent 状态可视化面板

**优先级**: P2  
**对应 PRD**: FR-M5-05  

**简述**: 前端实现 Agent 状态全景面板。展示：1) Agent 拓扑图（注册的 Agent 及其连接关系）2) 各 Agent 实时状态（Active/Inactive/Error）3) 最近调用统计（调用次数、成功率、平均延迟）4) 当前活跃的工作流及其进度。数据通过 REST API 获取，可选 WebSocket/SSE 实时推送。

**领域模型**: 复用 M1/M3 的查询数据  
**端点**: `GET /api/dashboard/agents-overview`, `GET /api/dashboard/active-workflows`  

---

## 模块 M6：Security & Governance（安全认证 + 安全与治理）

### SPEC-049: 身份认证与 RBAC（JWT + 角色权限）

**优先级**: P0（前置基础设施）  
**对应 PRD**: FR-M6-01, FR-M6-02  

**简述**: 实现基于 JWT 的身份认证体系，作为平台所有需要身份识别功能的前置依赖。核心流程：1) 用户通过 `POST /api/auth/login` 提交用户名/密码获取 JWT Token（含角色 Claim）2) 后续 API 请求携带 `Authorization: Bearer {token}` 3) ASP.NET Core 中间件自动校验 Token 有效性。角色模型：Admin（全权限）、Operator（执行操作）、Viewer（只读）。用户数据存储在 PostgreSQL，密码使用 BCrypt 哈希。此模块是人工审批（SPEC-051）的前置依赖——审批操作需要明确审批者身份。

**领域模型**: `User`（实体，Username, PasswordHash, Role）、`RefreshToken`（实体）  
**端点**: `POST /api/auth/login`, `POST /api/auth/refresh`, `GET /api/auth/me`  

**第三方库映射**:
- `Microsoft.AspNetCore.Authentication.JwtBearer` → JWT 中间件
- `BCrypt.Net-Next` → 密码哈希

---

### SPEC-050: Agent 工具访问权限控制

**优先级**: P1  
**对应 PRD**: FR-M6-01  

**简述**: 为每个 Agent 配置可访问的工具白名单。Agent 调用 Tool Gateway 时，Gateway 校验该 Agent 是否有权限调用目标工具，无权限返回 403。权限配置通过 API 管理（绑定 Agent ↔ Tool 关系）。

**领域模型**: `AgentToolPermission`（实体，AgentId + ToolId + GrantedAt）  
**端点**: `POST/DELETE /api/agents/{id}/permissions/tools/{toolId}`, `GET /api/agents/{id}/permissions`  

---

### SPEC-051: 危险操作审批流

**优先级**: P1  
**对应 PRD**: FR-M6-02  

**简述**: 工具注册时可标记 `destructive=true`（对应 MCP Tool annotation）。当 Agent 或工作流请求调用 destructive 工具时，调用不直接执行，而是创建一个 `ApprovalRequest` 等待人工审批。**审批操作强制要求 JWT 认证**（依赖 SPEC-049），审批者身份记录到审计日志。管理员通过 API 或前端审批通过/拒绝后，系统才执行或取消调用。

**领域模型**: `ApprovalRequest`（聚合根，Requester, ToolId, Params, Status: Pending/Approved/Rejected）  
**端点**: `GET /api/approvals`, `POST /api/approvals/{id}/approve`, `/reject`  

**第三方库映射**:
- `mcp-specification/` → Tool annotations.destructive

---

### SPEC-052: 操作审计日志查询

**优先级**: P1  
**对应 PRD**: FR-M6-03  

**简述**: 所有关键操作（Agent 注册/注销、工具调用、工作流执行、审批操作）统一记录审计日志。日志包含：操作者、操作类型、目标对象、时间戳、请求参数（脱敏后）、操作结果。提供查询 API，支持按操作者、类型、时间范围、目标对象过滤。

**领域模型**: `AuditLog`（实体，Actor, Action, TargetType, TargetId, Timestamp, Details）  
**端点**: `GET /api/audit-logs`  

---

## 模块 M7：Frontend（前端可视化）

> **开发策略**: 前端页面与后端模块增量协同开发（见 PRD 第 6 节），每个 Sprint 交付全栈功能切片。

### SPEC-059: 登录页面与认证状态管理

**优先级**: P0（前置基础设施，与 SPEC-049 同步开发）  
**对应 PRD**: M7 Login 页面  

**简述**: 实现前端登录页面和全局认证状态管理。登录页使用 shadcn/ui Form 组件，提交用户名/密码调用 `POST /api/auth/login` 获取 JWT Token。Token 存储在 `localStorage`，通过 React Context 管理全局认证状态（isAuthenticated, user, role）。创建 Axios/Fetch 拦截器自动附加 `Authorization` Header。未登录用户重定向到 `/login`。Token 过期自动刷新或重新登录。

**技术**: React Context + JWT + shadcn/ui（Form, Input, Button, Card）  
**页面路由**: `/login`  

---

### SPEC-060: 系统 Dashboard 与布局框架

**优先级**: P1  
**对应 PRD**: M7 Dashboard 页面  

**简述**: 搭建前端 React 项目的路由框架和布局组件（侧边栏导航 + 顶栏 + 内容区）。实现 Dashboard 首页，展示系统概览卡片：已注册 Agent 数量、活跃工作流数量、工具总数、最近告警统计。使用 shadcn/ui Card 组件。数据从后端 API 获取。

**技术**: React Router、shadcn/ui（Card, Button, Badge）、Tailwind CSS  
**页面路由**: `/`  

---

### SPEC-061: Agent 管理页面（多类型支持）

**优先级**: P1  
**对应 PRD**: M7 Agent Registry / Agent Detail 页面  

**简述**: 实现 Agent 管理的前端页面：1) Agent 列表页（表格展示，支持按类型 A2A/ChatClient/Workflow 和状态过滤）2) Agent 注册表单（根据选择的 AgentType 动态切换表单字段：A2A→AgentCard 表单、ChatClient→LLM 配置表单、Workflow→工作流选择器）3) Agent 详情页（根据类型展示不同配置视图、健康状态、最近调用记录）。对接 SPEC-001/002 的 API。

**页面路由**: `/agents`, `/agents/:id`  

---

### SPEC-062: 工具管理页面

**优先级**: P1  
**对应 PRD**: M7 Tool Manager 页面  

**简述**: 实现工具管理的前端页面：1) 工具列表页（表格展示，区分 REST API 和 MCP Server 类型）2) 手动注册工具表单 3) OpenAPI 文档导入功能（上传文件或输入 URL）4) 工具详情页（Schema 展示、调用测试、熔断状态）。对接 SPEC-010 ~ 014 的 API。

**页面路由**: `/tools`, `/tools/:id`  

---

### SPEC-063: 可视化工作流编排器

**优先级**: P1  
**对应 PRD**: M7 Workflow Designer 页面  

**简述**: 实现拖拽式工作流编排画布。左侧面板展示可用的 Agent 和 Tool 节点（从 Registry 和 Gateway 获取列表），用户拖拽到画布创建节点，连线创建边。支持设置节点属性（Agent 配置、条件表达式等）。画布渲染使用 React Flow 库。编辑完成后保存为 SPEC-020 的 WorkflowDefinition JSON 格式。

**技术**: React Flow（@xyflow/react）、shadcn/ui 面板  
**页面路由**: `/workflows/new`, `/workflows/:id/edit`  

---

### SPEC-064: 工作流执行监控页面

**优先级**: P1  
**对应 PRD**: M7 Workflow Detail / Workflow Execution 页面  

**简述**: 实现工作流执行的实时监控页面。在工作流 DAG 画布上，用不同颜色标记各节点的执行状态（灰色=待执行、蓝色=运行中、绿色=完成、红色=失败）。展示每个节点的输入/输出数据。提供暂停/取消操作按钮。下方展示执行历史列表。使用 SSE 或轮询实时更新状态。

**页面路由**: `/workflows/:id/executions/:execId`  

---

### SPEC-065: AIOps 告警与修复页面

**优先级**: P1  
**对应 PRD**: M7 AIOps / Alert Detail 页面  

**简述**: 实现 AIOps 相关前端页面：1) 告警列表页（按严重程度排序，展示状态标签）2) 告警详情页（展示告警信息、根因分析结果、关联指标/日志、修复建议列表）3) 审批操作面板（Approve/Reject 修复动作）。对接 SPEC-030 ~ 032 的 API。

**页面路由**: `/aiops`, `/aiops/alerts/:id`  

---

## 模块 M8：数据源接入（异构数据统一语义层）

### SPEC-070: 异构数据源注册与管理

**优先级**: P2  
**对应课题成果**: 2  

**简述**: 支持注册多种异构数据源（Prometheus 指标、ElasticSearch/Loki 日志、结构化数据库表等）。注册时提供连接配置和数据类型。平台将每个数据源抽象为 MCP Resource 概念（URI 模板 + 内容类型），供 Agent 通过统一语义查询数据，而不需要了解底层存储协议。

**领域模型**: `DataSourceRegistration`（聚合根，Name, Type: Metrics/Logs/Database, ConnectionConfig, ResourceUriTemplate）  
**端点**: `POST/GET /api/datasources`, `GET/PUT/DELETE /api/datasources/{id}`  

**第三方库映射**:
- `mcp-specification/` → Resource 系统（URI 模板、text/binary 内容）

---

## Spec 执行优先级总览

```
P0 (前置基础设施)
  ├── SPEC-000: Aspire AppHost 编排
  ├── SPEC-049: 身份认证与 RBAC (JWT)
  └── SPEC-059: 登录页面与认证状态管理

P1 (MVP 核心 — 增量全栈迭代)
  ├── Sprint 1: Agent Registry 全栈
  │   ├── SPEC-001: Agent CRUD（多类型）
  │   ├── SPEC-004: AgentSession PostgreSQL 持久化
  │   └── SPEC-061: Agent 管理页（多类型）
  │
  ├── Sprint 2: Tool Gateway 全栈
  │   ├── SPEC-010: REST API 工具注册
  │   ├── SPEC-011: MCP Server 工具注册
  │   ├── SPEC-012: OpenAPI 自动导入
  │   ├── SPEC-013: 工具调用代理
  │   └── SPEC-062: 工具管理页
  │
  ├── Sprint 3: Workflow Engine 全栈
  │   ├── SPEC-020: 工作流定义 CRUD
  │   ├── SPEC-021: 工作流执行引擎
  │   ├── SPEC-022: Agent Handoff 编排（AIOps 核心）
  │   ├── SPEC-026: 工作流发布为 WorkflowAgent
  │   ├── SPEC-063: 工作流编排器
  │   └── SPEC-064: 工作流监控页
  │
  ├── Sprint 4: Security 治理 + AIOps 全栈
  │   ├── SPEC-050: 工具权限控制
  │   ├── SPEC-051: 危险操作审批
  │   ├── SPEC-052: 审计日志
  │   ├── SPEC-030: 告警事件接收
  │   ├── SPEC-031: 根因分析
  │   ├── SPEC-032: 修复审批
  │   ├── SPEC-033: AIOps 端到端工作流（Handoff 模式）
  │   └── SPEC-065: AIOps 告警页
  │
  └── Sprint 5: Observability 全栈
      ├── SPEC-040: 全链路追踪
      └── SPEC-060: Dashboard 框架

P1-Upgrade (工作流引擎升级 — MVP 可用) ← NEW
  │   详见 [WORKFLOW-UPGRADE-SPEC-INDEX](WORKFLOW-UPGRADE-SPEC-INDEX.md)
  ├── SPEC-080: 工作流引擎基础修复
  ├── SPEC-081: 数据流模型与执行栈引擎 ★
  ├── SPEC-082: 工作流实时推送 SignalR
  └── SPEC-083: 表达式引擎与错误处理

P2 (增强功能 — 第二轮迭代)
  ├── SPEC-002: Agent 健康检查
  ├── SPEC-014: 配额与熔断
  ├── SPEC-015: 调用审计日志
  ├── SPEC-023: Group Chat
  ├── SPEC-024: 执行暂停/取消/回溯
  ├── SPEC-025: 短时/长时工具编排
  ├── SPEC-041: Agent 状态面板
  ├── SPEC-070: 异构数据源接入
  ├── SPEC-084: 部分执行与数据追踪 ← NEW
  └── SPEC-085: 前端升级与并发执行 ← NEW

P3 (远期增强)
  └── SPEC-003: Agent 能力语义搜索
```

---

**建议开发顺序（增量全栈策略）**:

1. **Sprint 0**: SPEC-000（Aspire 基础设施 + PostgreSQL 编排）→ SPEC-049（JWT 认证）→ SPEC-059（Login 页面 + 路由框架）
2. **Sprint 1**: SPEC-001（Agent CRUD 多类型）→ SPEC-004（AgentSession PostgreSQL 持久化）→ SPEC-061（Agent 管理页面）
3. **Sprint 2**: SPEC-010→011→012→013（Tool Gateway 后端）→ SPEC-062（工具管理页面）
4. **Sprint 3**: SPEC-020→021→022→026（Workflow Engine + Handoff + WorkflowAgent）→ SPEC-063→064（工作流编排/监控页面）
5. **Sprint 4**: SPEC-050→051→052（安全治理）→ SPEC-030→031→032→033（AIOps Handoff 工作流）→ SPEC-065（AIOps 页面）
6. **Sprint 5**: SPEC-040（全链路追踪）→ SPEC-060（Dashboard）

> 每个 Sprint 交付可演示的全栈功能切片，前端与后端同步开发。

*每个 SPEC 展开为详细文档时，遵循 Constitution 五步流程：先写 Spec 详情 → 再写 Test → 再定义 Interface → 最后 Implement。*
