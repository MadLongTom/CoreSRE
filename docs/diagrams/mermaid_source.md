# CoreSRE 系统图表 — Mermaid 源码备份

> 以下 Mermaid 源码对应 Graphviz 生成的 5 张 PNG 图。可直接粘贴到支持 Mermaid 的 Markdown 渲染器中查看。

---

## 1. 业务流程图 — 告警事故处置全链路

```mermaid
flowchart TD
    subgraph 告警接入
        A[Alertmanager Webhook] -->|POST /api/datasources/webhook| B[AlertPayload 解析]
        B --> C{MatchAlertRules<br/>标签匹配}
        C -->|冷却期内| D[丢弃重复告警]
        C -->|匹配到规则| E{判断链路类型}
    end

    subgraph "链路 A — SOP 自动执行"
        E -->|规则绑定 SOP| F[创建 Incident<br/>SopExecution 路由]
        F --> F1[创建 Conversation]
        F1 --> F2[上下文初始化<br/>预查询 DataSource]
        F2 --> G[IIncidentDispatcher<br/>后台调度]
        G --> H[IAgentCaller 调用<br/>Responder Agent]
        H --> H1[解析 SOP 步骤<br/>SopStructuredParser]
        H1 --> H2[逐步执行 SOP<br/>工具调用 + 数据查询]
        H2 --> I{执行结果}
        I -->|成功| J[Incident.Resolve<br/>记录 MTTR]
        I -->|失败/超时| K[降级到链路 B<br/>FallbackToRca]
        I -->|需人工| L[RequestIntervention<br/>HITL 阻塞等待]
    end

    subgraph "链路 B — 根因分析"
        E -->|规则无 SOP| M[创建 Incident<br/>RCA 路由]
        K --> M
        M --> M1[创建 Conversation]
        M1 --> M2[上下文初始化<br/>预查询 DataSource]
        M2 --> N[IIncidentDispatcher<br/>后台调度]
        N --> O[ITeamOrchestrator<br/>多 Agent 协作]
        O --> P[Team Agent 编排<br/>Sequential/Handoffs/etc]
        P --> Q[根因分析结论<br/>SetRootCause]
        Q --> R{是否自动生成 SOP}
    end

    subgraph "链路 C — SOP 自动生成"
        R -->|有 SummarizerAgent| S[GenerateSopFromIncident]
        S --> T[SopGenerationPromptBuilder<br/>构建提示词]
        T --> U[Summarizer Agent<br/>生成 SOP Markdown]
        U --> V[SopParserService<br/>SOP 结构化解析]
        V --> W[SopValidator<br/>校验工具引用]
        W --> X[创建 SkillRegistration<br/>Draft 状态]
    end

    subgraph "SOP 质量保证"
        X --> Y[ValidateSop<br/>结构化校验]
        Y --> Z[DryRunSop<br/>模拟执行]
        Z --> AA[人工审核<br/>Approve/Reject]
        AA -->|通过| AB[PublishSop<br/>Active 状态]
        AB --> AC[绑定到 AlertRule<br/>BindSop]
        AA -->|驳回| AD[Reject 状态]
    end

    subgraph "金丝雀验证"
        AC --> AE[StartCanary<br/>新旧 SOP 对比]
        AE --> AF[Shadow 执行<br/>记录 CanaryResult]
        AF --> AG{结论一致?}
        AG -->|是| AH[PromoteCanary<br/>切换新 SOP]
        AG -->|否| AI[StopCanary<br/>保留旧 SOP]
    end

    subgraph HITL人工介入
        L --> BA[SignalR 推送<br/>InterventionRequest]
        BA --> BB[前端展示<br/>审批/输入/选择]
        BB --> BC[RespondToIntervention<br/>解除阻塞]
        BC --> H2
    end

    subgraph 评估反馈
        J --> CA[Post-Mortem 标注<br/>RCA 准确率评分]
        CA --> CB[Evaluation Dashboard<br/>自动解决率/MTTR/覆盖率]
        CB --> CC[PromptOptimization<br/>建议优化 Agent 提示词]
    end
```

---

## 2. 业务链路图 — 系统交互全景

```mermaid
flowchart LR
    subgraph 外部系统
        AM[Alertmanager]
        PROM[Prometheus]
        LOKI[Loki]
        JAEGER[Jaeger]
        K8S[Kubernetes]
        ARGO[ArgoCD]
        GH[GitHub/GitLab]
        LLM[LLM Provider<br/>OpenAI API]
        MCP_EXT[外部 MCP Server]
        REST_EXT[REST API 工具]
    end

    subgraph "CoreSRE 平台"
        direction TB
        subgraph "API 层"
            WH[Webhook 端点]
            AGENT_API[Agent API]
            CHAT_API[Chat API]
            WF_API[Workflow API]
            INC_API[Incident API]
            TOOL_API[Tool API]
            DS_API[DataSource API]
            SK_API[Skill API]
            SB_API[Sandbox API]
            EVAL_API[Evaluation API]
        end

        subgraph "告警处置引擎"
            MATCH[AlertRule 匹配]
            DISP_A[链路A 调度<br/>SOP 执行]
            DISP_B[链路B 调度<br/>根因分析]
            DISP_C[链路C 生成<br/>SOP 生成]
            HITL[HITL 人工介入<br/>SignalR]
        end

        subgraph "Agent 运行时"
            AR[AgentResolver<br/>构建 Agent]
            TO[TeamOrchestrator<br/>多 Agent 编排]
            AC[AgentCaller<br/>Agent 调用]
            TFF[ToolFunctionFactory<br/>工具绑定]
            DFF[DataSourceFunctionFactory<br/>数据源绑定]
            STP[SandboxToolProvider<br/>沙箱绑定]
        end

        subgraph "工作流引擎"
            WE[WorkflowEngine<br/>DAG 执行]
            CE[ConditionEvaluator]
            EE[V8ExpressionEvaluator]
            WBG[Background Worker<br/>Channel 消费]
        end

        subgraph "基础设施"
            PG[(PostgreSQL<br/>pgvector)]
            MINIO[(MinIO S3)]
            SR[SignalR Hub]
        end
    end

    subgraph 前端
        FE_CHAT[对话页面<br/>AG-UI 流式]
        FE_INC[事故管理页面<br/>SignalR 实时]
        FE_WF[工作流编辑器<br/>DAG 可视化]
        FE_MGMT[资源管理<br/>Agent/Tool/Skill/DS]
        FE_EVAL[评估仪表板]
    end

    AM -->|Webhook| WH
    WH --> MATCH
    MATCH --> DISP_A
    MATCH --> DISP_B
    DISP_A --> AC
    DISP_B --> TO
    TO --> AC
    AC --> AR
    AR -->|构建 ChatClient| LLM
    AR --> TFF
    AR --> DFF
    AR --> STP
    TFF -->|REST| REST_EXT
    TFF -->|MCP| MCP_EXT
    DFF --> PROM
    DFF --> LOKI
    DFF --> JAEGER
    DFF --> K8S
    DFF --> ARGO
    DFF --> GH
    STP --> K8S
    DISP_A --> DISP_C
    DISP_C --> AC
    HITL --> SR

    WF_API --> WBG
    WBG --> WE
    WE --> CE
    WE --> EE
    WE --> AC

    AGENT_API --> PG
    CHAT_API --> PG
    WF_API --> PG
    INC_API --> PG
    TOOL_API --> PG
    DS_API --> PG
    SK_API --> PG
    SK_API --> MINIO
    SB_API --> K8S

    FE_CHAT -->|AG-UI SSE| CHAT_API
    FE_INC -->|SignalR| SR
    FE_WF -->|SignalR| SR
    FE_MGMT --> AGENT_API
    FE_MGMT --> TOOL_API
    FE_MGMT --> DS_API
    FE_MGMT --> SK_API
    FE_EVAL --> EVAL_API
```

---

## 3. 数据流转图 — 全系统数据生命周期

```mermaid
flowchart TB
    subgraph "数据输入"
        IN1[Alertmanager<br/>告警 JSON]
        IN2[用户对话消息<br/>AG-UI SSE]
        IN3[前端表单<br/>REST API]
        IN4[OpenAPI 规范<br/>工具导入]
    end

    subgraph "API 网关层"
        GW1[WebhookEndpoints<br/>告警接入]
        GW2[AgentChatEndpoints<br/>流式对话]
        GW3[各资源 CRUD Endpoints]
        GW4[ToolEndpoints<br/>工具导入/调用]
    end

    subgraph "命令/查询处理 (MediatR)"
        direction TB
        CMD[Command Handler<br/>写操作]
        QRY[Query Handler<br/>读操作]
        VAL[ValidationBehavior<br/>FluentValidation 前置校验]
    end

    subgraph "领域层数据流"
        direction TB
        AG_REG[AgentRegistration<br/>4类: A2A/ChatClient/Workflow/Team]
        CONV[Conversation<br/>对话元数据]
        SESS[AgentSessionRecord<br/>会话序列化 JSONB]
        INC[Incident<br/>事故生命周期]
        AR[AlertRule<br/>告警路由规则]
        WF_DEF[WorkflowDefinition<br/>DAG 图定义]
        WF_EXE[WorkflowExecution<br/>执行快照+结果]
        TOOL[ToolRegistration<br/>工具源注册]
        MCP_ITEM[McpToolItem<br/>MCP 子工具]
        SKILL[SkillRegistration<br/>SOP/技能文档]
        DS[DataSourceRegistration<br/>数据源配置]
        LLM_P[LlmProvider<br/>API 密钥+模型]
        SANDBOX[SandboxInstance<br/>K8s Pod 状态]
        CANARY[CanaryResult<br/>金丝雀结果]
        PROMPT[PromptOptimizationSuggestion<br/>提示词优化]
    end

    subgraph "持久化层"
        PG[(PostgreSQL + pgvector<br/>EF Core)]
        S3[(MinIO S3<br/>文件存储)]
    end

    subgraph "外部数据源查询"
        DS_Q[DataSourceQuerier<br/>Prometheus/Loki/Jaeger/K8s/ArgoCD/GitHub/GitLab]
    end

    subgraph "Agent 运行时数据流"
        AGENT_RT[AIAgent 实例]
        LLM_CALL[LLM API 调用<br/>Chat Completion]
        TOOL_CALL[工具函数调用<br/>AIFunction]
        DS_CALL[数据源函数调用<br/>AIFunction]
        SB_CALL[沙箱函数调用<br/>AIFunction]
    end

    subgraph "实时推送"
        SR_WF[SignalR WorkflowHub<br/>节点执行状态]
        SR_INC[SignalR IncidentHub<br/>事故事件+HITL]
    end

    subgraph "数据输出"
        OUT1[前端 UI 渲染]
        OUT2[Evaluation 指标<br/>MTTR/覆盖率/准确率]
        OUT3[通知渠道<br/>Slack/Teams]
    end

    IN1 --> GW1
    IN2 --> GW2
    IN3 --> GW3
    IN4 --> GW4

    GW1 --> VAL --> CMD
    GW2 --> CMD
    GW3 --> VAL
    GW4 --> VAL

    CMD --> AG_REG & CONV & INC & AR & WF_DEF & TOOL & SKILL & DS & LLM_P & SANDBOX
    QRY --> AG_REG & CONV & INC & AR & WF_DEF & TOOL & SKILL & DS

    INC -->|创建对话| CONV
    CONV -->|关联| SESS
    AR -->|匹配触发| INC
    INC -->|引用| SKILL
    INC -->|生成新| SKILL
    SKILL -->|绑定到| AR
    AR -->|金丝雀对比| CANARY
    INC -->|反馈| PROMPT

    AG_REG --> AGENT_RT
    AGENT_RT --> LLM_CALL
    AGENT_RT --> TOOL_CALL
    AGENT_RT --> DS_CALL
    AGENT_RT --> SB_CALL
    TOOL_CALL --> TOOL --> MCP_ITEM
    DS_CALL --> DS --> DS_Q
    SB_CALL --> SANDBOX

    WF_DEF -->|执行| WF_EXE
    WF_EXE --> AGENT_RT

    AG_REG & CONV & INC & AR & WF_DEF & WF_EXE & TOOL & MCP_ITEM & SKILL & DS & LLM_P & SANDBOX & CANARY & PROMPT --> PG
    SKILL -->|文件包| S3

    WF_EXE --> SR_WF
    INC --> SR_INC

    SR_WF --> OUT1
    SR_INC --> OUT1
    QRY --> OUT1
    INC --> OUT2
    INC --> OUT3
```

---

## 4. 系统架构图 — 分层架构 + 基础设施

```mermaid
graph TB
    subgraph "客户端层 (React + TypeScript)"
        FE[React SPA<br/>Vite + shadcn/ui]
        AGUI[AG-UI Protocol<br/>SSE 流式对话]
        SRClient[SignalR Client<br/>实时推送]
    end

    subgraph "API 网关层 (.NET Minimal API)"
        EP[16 组 Endpoints<br/>Minimal API Routes]
        MW[ExceptionHandling<br/>Middleware]
        WS[WebSocket Handler<br/>沙箱终端]
        HUBS[SignalR Hubs<br/>Workflow + Incident]
    end

    subgraph "应用层 (CQRS + MediatR)"
        MR[MediatR Pipeline]
        VB[ValidationBehavior<br/>FluentValidation]
        CMD[Command Handlers<br/>写操作]
        QRY[Query Handlers<br/>读操作]
        AM[AutoMapper<br/>DTO 映射]
    end

    subgraph "领域层 (DDD)"
        ENT["16 实体 (聚合根)"]
        VO["39 值对象 (JSONB)"]
        ENUM["33 枚举"]
        REPO_IF["18 仓储接口"]
    end

    subgraph "基础设施层"
        direction TB
        subgraph "Agent 运行时"
            ARS[AgentResolverService]
            TOS[TeamOrchestratorService]
            ACS[AgentCallerService]
        end

        subgraph "工具网关"
            REST_INV[RestApiToolInvoker]
            MCP_INV[McpToolInvoker]
            TFF2[ToolFunctionFactory]
            OAP[OpenApiParserService]
        end

        subgraph "数据源集成"
            DSF[DataSourceQuerierFactory]
            PROM_Q[PrometheusQuerier]
            LOKI_Q[LokiQuerier]
            JAEGER_Q[JaegerQuerier]
            K8S_Q[KubernetesQuerier]
            ARGO_Q[ArgoCDQuerier]
            GH_Q[GitHubQuerier]
            GL_Q[GitLabQuerier]
            AM_Q[AlertmanagerQuerier]
        end

        subgraph "SRE 告警引擎"
            AMP[AlertmanagerPayloadParser]
            IDS[IncidentDispatcherService]
            AIST[ActiveIncidentSessionTracker]
            SPS[SopParserService]
            SVS[SopValidatorService]
        end

        subgraph "工作流引擎"
            WE2[WorkflowEngine]
            COND[ConditionEvaluator]
            V8[V8ExpressionEvaluator<br/>ClearScript]
            WBGS[Background Worker]
        end

        subgraph "K8s 沙箱"
            K8SC[Kubernetes Client]
            SPP[SandboxPodPool]
            PSM[PersistentSandboxManager]
        end

        subgraph "持久化"
            EF[EF Core + Npgsql]
            REPO[15 仓储实现]
            PGSS[PostgresAgentSessionStore]
        end

        subgraph "存储"
            MINIO2[MinioFileStorage]
            SKILL_S3[S3AgentSkillsProvider]
        end
    end

    subgraph "基础设施 (Aspire 编排)"
        PG2[(PostgreSQL<br/>pgvector:pg17)]
        S3_2[(MinIO<br/>S3 对象存储)]
    end

    subgraph "外部依赖"
        LLM2[LLM Providers<br/>OpenAI 兼容]
        EXT_MCP[MCP Servers]
        EXT_REST[REST APIs]
        EXT_OBS[可观测性栈<br/>Prometheus/Loki/Jaeger]
        EXT_K8S[K8s Cluster]
        EXT_GIT[Git Platforms]
    end

    FE --> EP
    AGUI --> EP
    SRClient --> HUBS

    EP --> MW --> MR
    MR --> VB --> CMD & QRY
    CMD --> AM
    QRY --> AM

    CMD & QRY --> ENT & VO
    ENT --> REPO_IF

    REPO_IF --> REPO --> EF --> PG2
    MINIO2 --> S3_2

    ARS --> LLM2
    TOS --> ARS
    REST_INV --> EXT_REST
    MCP_INV --> EXT_MCP
    DSF --> PROM_Q & LOKI_Q & JAEGER_Q & K8S_Q & ARGO_Q & GH_Q & GL_Q & AM_Q
    PROM_Q & LOKI_Q & JAEGER_Q & AM_Q --> EXT_OBS
    K8S_Q & ARGO_Q --> EXT_K8S
    GH_Q & GL_Q --> EXT_GIT

    K8SC --> EXT_K8S
```

---

## 5. 概念图 — 领域模型关系

```mermaid
graph TB
    subgraph "核心概念域"
        AGENT["🤖 Agent<br/>智能体"]
        TOOL["🔧 Tool<br/>工具"]
        SKILL["📋 Skill/SOP<br/>技能/标准操作程序"]
        DS["📊 DataSource<br/>数据源"]
        WF["🔄 Workflow<br/>工作流"]
        LLM["🧠 LLM Provider<br/>大模型服务"]
        SANDBOX["📦 Sandbox<br/>沙箱环境"]
    end

    subgraph "事故响应域"
        ALERT["🚨 Alert<br/>告警"]
        RULE["📐 AlertRule<br/>告警路由规则"]
        INC["🔥 Incident<br/>故障事故"]
        SOP_EXEC["▶️ SOP 执行<br/>标准流程自动化"]
        RCA["🔍 RCA<br/>根因分析"]
        HITL["👤 HITL<br/>人工介入"]
        CANARY["🐦 Canary<br/>金丝雀验证"]
    end

    subgraph "协作与编排域"
        CONV["💬 Conversation<br/>对话"]
        SESSION["💾 Session<br/>会话持久化"]
        TEAM["👥 Team<br/>多Agent协作"]
    end

    subgraph "质量与评估域"
        EVAL["📈 Evaluation<br/>效能评估"]
        POSTMORTEM["📝 Post-Mortem<br/>事后复盘"]
        PROMPT_OPT["✨ Prompt优化<br/>提示词改进建议"]
    end

    AGENT -->|"使用"| TOOL
    AGENT -->|"装备"| SKILL
    AGENT -->|"查询"| DS
    AGENT -->|"调用"| LLM
    AGENT -->|"操作"| SANDBOX
    AGENT -->|"参与"| CONV
    AGENT -->|"编排为"| WF

    AGENT -.->|"类型: Team"| TEAM
    TEAM -->|"编排多个"| AGENT

    ALERT -->|"匹配"| RULE
    RULE -->|"创建"| INC
    RULE -->|"绑定"| SKILL
    INC -->|"链路A"| SOP_EXEC
    INC -->|"链路B"| RCA
    SOP_EXEC -->|"执行"| SKILL
    SOP_EXEC -->|"失败降级"| RCA
    RCA -->|"链路C 生成"| SKILL
    SOP_EXEC -->|"需要时触发"| HITL
    RULE -->|"新旧对比"| CANARY

    CONV -->|"存储于"| SESSION
    INC -->|"关联"| CONV

    INC -->|"产生"| EVAL
    INC -->|"标注"| POSTMORTEM
    POSTMORTEM -->|"驱动"| PROMPT_OPT
    PROMPT_OPT -->|"优化"| AGENT
    EVAL -->|"衡量"| SKILL

    WF -->|"节点引用"| AGENT
    WF -->|"节点引用"| TOOL
```
