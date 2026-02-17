# SRE 数据源集成抽象设计

**Date**: 2026-02-17  
**Status**: Draft  
**Relates**: BRD 3.4（统一语义层）、课题成果 2（异构数据源接入）、PRD M4（AIOps Engine）

## 1. 问题定义

CoreSRE 目前有完整的 Agent 编排层（A2A/ChatClient/Workflow/Team），但 Agent 「看到」的世界为零 —— 没有任何 SRE 可观测数据可供推理。

要实现 AIOps 闭环（告警 → RCA → 修复），Agent 需要统一查询 6 类异构数据源：

| 类别 | 代表产品 | 查询语言 | 传输 |
|------|---------|---------|------|
| **Metrics** | Prometheus / VictoriaMetrics / Mimir | PromQL | Pull (HTTP) |
| **Logs** | Loki / Elasticsearch / OpenSearch | LogQL / Query DSL | Pull (HTTP) |
| **Tracing** | Jaeger / Tempo / Zipkin | TraceID + Tag Search | Pull (HTTP) |
| **Alerting** | Alertmanager / PagerDuty / OpsGenie | Label Matcher | Pull + Push (Webhook) |
| **Deployment** | Kubernetes / ArgoCD / Flux | Label Selector | Pull (HTTP/gRPC) |
| **Git/SCM** | GitHub / GitLab / Azure DevOps | REST | Pull (HTTP) |

## 2. 设计原则

1. **镜像 Tool Gateway 模式** —— DataSource 与 ToolRegistration 拥有相同的领域抽象深度：Entity + VO + Enum + Repository + Factory + Invoker + AIFunction
2. **Domain 零外依赖** —— 所有 HTTP 客户端、SDK 调用在 Infrastructure 层
3. **Strategy 模式** —— 每种产品一个 `IDataSourceQuerier` 实现，通过 `CanHandle(product)` 分发
4. **统一查询语义** —— 四元组 `(TimeRange, Filters, Expression, Pagination)` 覆盖所有类别
5. **统一响应信封** —— 五种标准响应类型，Agent 消费标准化 JSON
6. **Agent 可调用** —— 通过 `DataSourceFunctionFactory` 将数据源查询暴露为 `AIFunction`，Agent 像调用工具一样查询数据

## 3. 领域模型

### 3.1 枚举

```
DataSourceCategory                    DataSourceProduct
├── Metrics                           ├── Prometheus
├── Logs                              ├── VictoriaMetrics
├── Tracing                           ├── Mimir
├── Alerting                          ├── Loki
├── Deployment                        ├── Elasticsearch
└── Git                               ├── Jaeger
                                      ├── Tempo
DataSourceStatus                      ├── Alertmanager
├── Registered                        ├── PagerDuty
├── Connected                         ├── Kubernetes
├── Disconnected                      ├── ArgoCD
└── Error                             ├── GitHub
                                      └── GitLab
```

`DataSourceCategory` 是逻辑分类（告诉 Agent「这是什么类型的数据」），`DataSourceProduct` 是具体实现（决定使用什么协议和查询语言）。二者正交 —— 例如 Loki 属于 Logs 类别，Prometheus 属于 Metrics 类别。

### 3.2 Entity: `DataSourceRegistration`

```
DataSourceRegistration : BaseEntity
├── Name: string (required, ≤200)
├── Description: string?
├── Category: DataSourceCategory
├── Product: DataSourceProduct
├── Status: DataSourceStatus
│
├── ConnectionConfig: DataSourceConnectionVO   ← JSONB
│   ├── BaseUrl: string (required)
│   ├── AuthType: AuthType (None/ApiKey/Bearer/Basic)
│   ├── EncryptedCredential: string?
│   ├── AuthHeaderName: string?           // e.g. "X-API-Key"
│   ├── TlsSkipVerify: bool = false
│   ├── TimeoutSeconds: int = 30
│   └── CustomHeaders: Dictionary<string, string>?
│
├── DefaultQueryConfig: QueryConfigVO?    ← JSONB
│   ├── DefaultNamespace: string?         // K8s namespace, log index
│   ├── DefaultLabels: Dictionary<string, string>?  // 预置标签过滤
│   └── MaxResults: int = 1000
│
├── HealthCheck: DataSourceHealthVO?      ← JSONB
│   ├── LastCheckAt: DateTime?
│   ├── IsHealthy: bool
│   └── ErrorMessage: string?
│
└── Metadata: DataSourceMetadataVO?       ← JSONB（自动发现的元数据缓存）
    ├── DiscoveredAt: DateTime?
    ├── Labels: List<string>?             // Prometheus label names
    ├── Indices: List<string>?            // ES index names
    ├── Services: List<string>?           // Jaeger service names
    └── Version: string?                  // 产品版本
```

**工厂方法**（每种 Category 一个，内含 Product 校验）：

```csharp
DataSourceRegistration.CreateMetrics(name, product, connectionConfig)
DataSourceRegistration.CreateLogs(name, product, connectionConfig)
DataSourceRegistration.CreateTracing(name, product, connectionConfig)
DataSourceRegistration.CreateAlerting(name, product, connectionConfig)
DataSourceRegistration.CreateDeployment(name, product, connectionConfig)
DataSourceRegistration.CreateGit(name, product, connectionConfig)
```

每个工厂验证 `Product ∈ Category` 的合法映射（不能把 Prometheus 注册为 Logs 类别）。

### 3.3 Value Objects: 统一查询模型

```
DataSourceQueryVO                          查询的标准化入参
├── TimeRange: TimeRangeVO?
│   ├── Start: DateTimeOffset
│   ├── End: DateTimeOffset
│   └── Step: TimeSpan?                   // Metrics 采样步长
│
├── Filters: List<LabelFilterVO>?
│   ├── Key: string
│   ├── Operator: FilterOperator (Eq/Neq/Regex/NotRegex)
│   └── Value: string
│
├── Expression: string?                   // PromQL / LogQL / KQL / TraceID
├── Pagination: PaginationVO?
│   ├── Offset: int = 0
│   └── Limit: int = 100
│
└── AdditionalParams: Dictionary<string, string>?  // 产品特有参数透传
```

### 3.4 Value Objects: 统一响应模型

五种标准响应类型，覆盖所有 SRE 数据形态：

```
DataSourceResultVO
├── ResultType: DataSourceResultType (TimeSeries/LogEntries/Spans/Alerts/Resources)
├── TimeSeries: List<TimeSeriesVO>?       // Metrics
├── LogEntries: List<LogEntryVO>?         // Logs
├── Spans: List<SpanVO>?                  // Tracing
├── Alerts: List<AlertVO>?               // Alerting
├── Resources: List<ResourceVO>?          // Deployment/Git
├── TotalCount: int?
└── Truncated: bool                       // 结果是否被截断

TimeSeriesVO
├── MetricName: string
├── Labels: Dictionary<string, string>
└── DataPoints: List<DataPointVO>
    ├── Timestamp: DateTimeOffset
    └── Value: double

LogEntryVO
├── Timestamp: DateTimeOffset
├── Level: string?                        // INFO/WARN/ERROR
├── Message: string
├── Labels: Dictionary<string, string>?
├── Source: string?                       // 文件/服务来源
└── TraceId: string?                      // 关联追踪

SpanVO
├── TraceId: string
├── SpanId: string
├── ParentSpanId: string?
├── OperationName: string
├── ServiceName: string
├── StartTime: DateTimeOffset
├── Duration: TimeSpan
├── Status: string                        // OK/ERROR
├── Tags: Dictionary<string, string>?
└── Events: List<SpanEventVO>?

AlertVO
├── AlertId: string
├── AlertName: string
├── Severity: string                      // critical/warning/info
├── Status: string                        // firing/resolved
├── StartsAt: DateTimeOffset
├── EndsAt: DateTimeOffset?
├── Labels: Dictionary<string, string>
├── Annotations: Dictionary<string, string>?
├── Source: string?                       // generatorURL
└── Fingerprint: string?

ResourceVO
├── Kind: string                          // Deployment/Pod/Application/Commit
├── Name: string
├── Namespace: string?
├── Status: string
├── Labels: Dictionary<string, string>?
├── Properties: Dictionary<string, object>?  // 通用 KV
└── UpdatedAt: DateTimeOffset?
```

## 4. 抽象层设计

### 4.1 核心接口

```
┌─────────────────────────────────────────────────────────┐
│                    Domain Layer                          │
│                                                          │
│  IDataSourceRegistrationRepository                       │
│    extends IRepository<DataSourceRegistration>            │
│    + GetByCategoryAsync(category)                         │
│    + GetByProductAsync(product)                           │
│    + GetByNameAsync(name)                                 │
│                                                          │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│                  Application Layer                       │
│                                                          │
│  IDataSourceQuerier                                      │
│    + CanHandle(product: DataSourceProduct): bool          │
│    + QueryAsync(registration, query): DataSourceResultVO  │
│    + HealthCheckAsync(registration): DataSourceHealthVO   │
│    + DiscoverMetadataAsync(registration):                 │
│        DataSourceMetadataVO                               │
│                                                          │
│  IDataSourceQuerierFactory                               │
│    + GetQuerier(product): IDataSourceQuerier              │
│                                                          │
│  IDataSourceFunctionFactory                              │
│    + CreateFunctionsAsync(dataSourceIds: List<Guid>):     │
│        IReadOnlyList<AIFunction>                          │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

### 4.2 Strategy 实现矩阵

```
IDataSourceQuerier
├── PrometheusQuerier          ── PromQL → /api/v1/query_range
├── LokiQuerier                ── LogQL → /loki/api/v1/query_range
├── ElasticsearchQuerier       ── Query DSL → POST /_search
├── JaegerQuerier              ── /api/traces, /api/services
├── TempoQuerier               ── /api/search, /api/traces/{id}
├── AlertmanagerQuerier        ── /api/v2/alerts, /api/v2/silences
├── KubernetesQuerier          ── KubernetesClient SDK
├── ArgoCDQuerier              ── /api/v1/applications
├── GitHubQuerier              ── /repos/{owner}/{repo}/commits
└── GitLabQuerier              ── /api/v4/projects/{id}/...
```

### 4.3 AIFunction 桥接

与 ToolFunctionFactory 平行：

```
Agent LlmConfig
  ├── ToolRefs: [guid1, guid2]            → ToolFunctionFactory → AIFunction[]
  └── DataSourceRefs: [guid3, guid4]      → DataSourceFunctionFactory → AIFunction[]

DataSourceFunctionFactory 为每个数据源生成 2-3 个 AIFunction：
  ├── query_{name}         主查询（参数: expression, start, end, filters）
  ├── metadata_{name}      元数据发现（列出可用标签/索引/服务）
  └── health_{name}        健康检查
```

Agent 调用示例（自然语言 → tool call）：
```
User: "最近 5 分钟 nginx 的 5xx 错误率是多少？"
Agent → tool_call: query_prometheus(
    expression="rate(nginx_http_requests_total{status=~'5..'}[5m])",
    start="2026-02-17T10:00:00Z",
    end="2026-02-17T10:05:00Z"
)
→ DataSourceFunctionFactory → PrometheusQuerier.QueryAsync()
→ TimeSeriesVO → Agent 推理
```

## 5. 完整分层架构

```
┌───────────────────────────────────────────────────────────────────┐
│ Agent（ChatClient / Team）                                        │
│   LlmConfig.DataSourceRefs: [Guid]                                │
│          │                                                         │
│          ▼                                                         │
│   DataSourceFunctionFactory.CreateFunctionsAsync(refs)             │
│          │                                                         │
│          ├─ IDataSourceRegistrationRepository.GetByIdsAsync()      │
│          │                                                         │
│          ▼                                                         │
│   DataSourceAIFunction(registration, querier)                     │
│          │  extends Microsoft.Extensions.AI.AIFunction             │
│          ▼                                                         │
│   IDataSourceQuerierFactory.GetQuerier(product)                   │
│          │  Strategy: IEnumerable<IDataSourceQuerier>.CanHandle()  │
│          ▼                                                         │
│   IDataSourceQuerier.QueryAsync(registration, query)              │
│          │                                                         │
├──────────┼────────────────────────────────────────────────────────┤
│ Infrastructure │                                                   │
│          ▼                                                         │
│   PrometheusQuerier / LokiQuerier / ElasticsearchQuerier / ...    │
│          │                                                         │
│          ▼                                                         │
│   HttpClient → Prometheus API / Loki API / ES API / ...           │
└───────────────────────────────────────────────────────────────────┘
```

## 6. CQRS 命令/查询设计

### 6.1 数据源管理（CRUD）

```
Commands/
├── RegisterDataSource/         POST /api/datasources
│   ├── RegisterDataSourceCommand { Name, Category, Product, ConnectionConfig }
│   ├── RegisterDataSourceCommandHandler → DataSourceRegistration.Create*()
│   └── RegisterDataSourceCommandValidator (FluentValidation)
├── UpdateDataSource/           PUT /api/datasources/{id}
├── DeleteDataSource/           DELETE /api/datasources/{id}
├── TestConnection/             POST /api/datasources/{id}/test
│   └── → IDataSourceQuerier.HealthCheckAsync()
└── DiscoverMetadata/           POST /api/datasources/{id}/discover
    └── → IDataSourceQuerier.DiscoverMetadataAsync()

Queries/
├── GetDataSources/             GET /api/datasources?category=Metrics
├── GetDataSourceById/          GET /api/datasources/{id}
└── GetDataSourceMetadata/      GET /api/datasources/{id}/metadata
```

### 6.2 数据查询（Agent & API 共用）

```
Commands/
└── QueryDataSource/            POST /api/datasources/{id}/query
    ├── QueryDataSourceCommand { DataSourceId, Expression, StartTime, EndTime, 
    │                            Filters, Step, Limit }
    ├── QueryDataSourceCommandHandler
    │   └── IDataSourceQuerierFactory.GetQuerier(product).QueryAsync(...)
    └── Returns DataSourceResultDto
```

## 7. LlmConfig 扩展（Agent ↔ DataSource 绑定）

与 `ToolRefs` 平行，在 `LlmConfigVO` 中新增：

```diff
 public sealed record LlmConfigVO
 {
     ...
     public List<Guid> ToolRefs { get; init; } = [];
+    public List<Guid> DataSourceRefs { get; init; } = [];
     ...
 }
```

在 `AgentResolverService` 中，解析 Agent 时同时加载 ToolRefs 和 DataSourceRefs：

```csharp
var toolFunctions = await _toolFunctionFactory.CreateFunctionsAsync(llmConfig.ToolRefs);
var dsFunctions = await _dataSourceFunctionFactory.CreateFunctionsAsync(llmConfig.DataSourceRefs);
var allFunctions = toolFunctions.Concat(dsFunctions).ToList();
```

## 8. 数据库变更

### 新表: `data_source_registrations`

| Column | Type | Nullable | Note |
|--------|------|----------|------|
| id | uuid | PK | |
| name | varchar(200) | NOT NULL | unique |
| description | text | NULL | |
| category | int | NOT NULL | enum → int |
| product | int | NOT NULL | enum → int |
| status | int | NOT NULL | |
| connection_config | jsonb | NOT NULL | DataSourceConnectionVO |
| default_query_config | jsonb | NULL | QueryConfigVO |
| health_check | jsonb | NULL | DataSourceHealthVO |
| metadata | jsonb | NULL | DataSourceMetadataVO |
| created_at | timestamptz | NOT NULL | |
| updated_at | timestamptz | NULL | |

### 修改: `agent_registrations.llm_config` JSONB

新增 `dataSourceRefs` 数组字段（JSONB 内部，无 DDL 变更，向后兼容）。

## 9. Category ↔ Product 合法映射

```
Metrics:     [Prometheus, VictoriaMetrics, Mimir]
Logs:        [Loki, Elasticsearch]
Tracing:     [Jaeger, Tempo]
Alerting:    [Alertmanager, PagerDuty]
Deployment:  [Kubernetes, ArgoCD]
Git:         [GitHub, GitLab]
```

此映射在领域层的 `DataSourceRegistration.Create*()` 工厂方法中强制校验。新增产品仅需：
1. 加枚举值
2. 加映射条目
3. 实现 `IDataSourceQuerier`
4. DI 注册

## 10. 实施路线

### Phase 1: 领域基础（SPEC-200）
- `DataSourceCategory`, `DataSourceProduct`, `DataSourceStatus` 枚举
- `DataSourceConnectionVO`, `QueryConfigVO`, `DataSourceHealthVO`, `DataSourceMetadataVO` VO
- `DataSourceRegistration` 实体 + 工厂方法
- `IDataSourceRegistrationRepository`
- EF Core 配置 + 迁移
- CQRS: RegisterDataSource / UpdateDataSource / DeleteDataSource / GetDataSources / GetDataSourceById
- 前端 DataSource 管理页面

### Phase 2: 查询抽象 + 首个实现（SPEC-201）
- `DataSourceQueryVO`, `DataSourceResultVO` 及子 VO
- `IDataSourceQuerier`, `IDataSourceQuerierFactory`
- `PrometheusQuerier` 实现（首个 Strategy）
- `QueryDataSource` Command + API endpoint
- `TestConnection` / `DiscoverMetadata` Commands
- 单元测试 + 集成测试

### Phase 3: 数据源扩展（SPEC-202）
- `LokiQuerier`, `ElasticsearchQuerier`
- `JaegerQuerier`, `TempoQuerier`
- `AlertmanagerQuerier`
- 每个 Querier 独立可测，可并行开发

### Phase 4: Agent 绑定（SPEC-203）
- `LlmConfigVO.DataSourceRefs` 扩展
- `DataSourceFunctionFactory` + `DataSourceAIFunction`
- `AgentResolverService` 集成
- 前端 Agent 编辑页 DataSource 选择器
- 端到端场景测试

### Phase 5: 高级功能（SPEC-204）
- `KubernetesQuerier`, `ArgoCDQuerier`
- `GitHubQuerier`, `GitLabQuerier`
- Webhook 推送（Alertmanager → CoreSRE）
- 定时健康检查后台服务
- 数据源连接池 / 缓存

## 11. 与 Tool Gateway 的对比

| 维度 | Tool Gateway | DataSource Gateway |
|------|-------------|-------------------|
| 实体 | `ToolRegistration` | `DataSourceRegistration` |
| 类型鉴别 | `ToolType` (RestApi/MCP) | `DataSourceProduct` (Prometheus/Loki/...) |
| 调用接口 | `IToolInvoker` | `IDataSourceQuerier` |
| 工厂 | `ToolInvokerFactory` | `DataSourceQuerierFactory` |
| AI 桥接 | `ToolFunctionFactory` → `AIFunction` | `DataSourceFunctionFactory` → `AIFunction` |
| Agent 绑定 | `LlmConfig.ToolRefs` | `LlmConfig.DataSourceRefs` |
| 连接配置 | `ConnectionConfigVO` + `AuthConfigVO` | `DataSourceConnectionVO` (合并) |
| 查询模型 | 无（工具调用即查询） | `DataSourceQueryVO`（统一四元组） |
| 响应模型 | `ToolInvocationResultDto` (raw JSON) | `DataSourceResultVO`（类型化五种） |

关键区别：Tool 是**通用 API 调用**（任意 HTTP/MCP），DataSource 是**结构化数据查询**（有时间轴、有标签、有语义）。因此 DataSource 需要额外的统一查询/响应模型，而 Tool 不需要。

## 12. 设计决策记录

| # | 决策 | 理由 |
|---|------|------|
| D1 | Category 与 Product 分离为两个枚举 | Category 是 Agent 视角的语义分类（「问指标」vs「问日志」），Product 是基础设施实现细节；同一 Category 可对应多个 Product |
| D2 | 统一查询 VO 而非每种 Category 独立查询类型 | 减少 Agent 学习成本，所有数据源查询参数一致；产品特有参数通过 `AdditionalParams` 透传 |
| D3 | 响应用 union 类型（一个 VO 含所有可能） | 简化 AIFunction 的返回类型定义；Agent 总是拿到同样结构的 JSON，根据 `ResultType` 判断解析哪个字段 |
| D4 | ConnectionConfig 合并 auth 字段（不拆 AuthConfigVO） | 数据源认证模式简单（99% 是 Bearer/APIKey），不需要 OAuth2 流程，合并减少 VO 数量 |
| D5 | 元数据缓存在实体 JSONB 字段 | 标签/索引列表随时间变化，但变化频率低；缓存在 DB 避免每次 Agent 调用都做发现请求 |
| D6 | 不复用 ToolRegistration | Tool 是「执行动作」，DataSource 是「查询数据」——语义不同、查询模型不同、响应模型不同；强行复用会导致 ToolRegistration 膨胀 |
