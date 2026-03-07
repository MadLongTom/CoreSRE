# Feature Specification: DataSource Precision Tool System

**Feature Branch**: `026-datasource-precision-tools`  
**Created**: 2026-03-08  
**Status**: Draft  
**Input**: AI 对数据源的查询以及变更能力太弱，需要设计一个优雅的、精准的工具体系，使得 LLM 能够精准快速查询

## Problem Statement

当前 `DataSourceFunctionFactory` 按 Category 为每个数据源生成固定的 AIFunction 集合（如 `query_metrics_{name}`、`query_logs_{name}`），存在以下核心问题：

1. **查询参数全靠 LLM 猜测**：PromQL/LogQL 表达式完全由 LLM 生成，容易写错标签名、度量名，导致多次无效查询浪费时间和 Token
2. **无 Schema 感知**：Agent 不知道数据源中有哪些具体的 metric/label/index，必须先调用 `list_metric_names` 发现，流程冗长
3. **无关联查询能力**：告警中出现 `namespace=demo-app`，Agent 无法一次性获取该 namespace 下的所有指标+日志+部署状态，需要逐个工具手动拼装
4. **无变更能力**：当前工具仅支持只读查询，SOP 中的 "重启 Pod"、"扩容 Deployment"、"回滚部署" 等操作没有对应工具
5. **查询结果过大**：时序数据、日志原始返回常常超过 LLM context window，缺少智能截断和摘要

## Clarifications

- Q: 变更操作（写入类工具）是否需要审批？ → A: 是，所有变更类工具必须标记 `RequiresApproval=true`，走工具审批流程
- Q: 自动注入 Schema 信息是否会导致 Token 浪费？ → A: 使用 progressive disclosure — 先提供 Schema 摘要（metric 名称列表），Agent 按需查询 label values
- Q: 关联查询如何触发？ → A: 提供 `query_context` 复合工具，接收 namespace/service/时间范围等参数，一次性从多个数据源拉取关联数据
- Q: 如何防止查询结果过大？ → A: 每个工具返回结果有 maxResultTokens 限制（默认 4000 token），超出时自动摘要
- Q: 变更操作如何保障安全？ → A: 变更工具参数必须显式声明（不接受自由文本），且通过 IncidentDispatcherService 的 ToolApproval 流程审批

## User Scenarios & Testing

### User Story 1 — Schema-Aware Query Hints (Priority: P0)

作为 Agent 使用者，我希望查询工具能自动注入可用的 metric/label/service 信息，使得 LLM 不需要额外调用 list 工具就能写出正确的查询表达式。

**Acceptance Scenarios**:

1. **Given** 数据源 prometheus-main 已注册且 Metadata 中缓存了 metrics/labels，**When** Agent 被解析且绑定了该数据源，**Then** `query_metrics_prometheus_main` 的 Description 包含 "Available metrics: up, http_requests_total, ..." 前 20 个常用指标。
2. **Given** 数据源 loki-main 的 Metadata.Labels 包含 `["namespace", "app", "pod", "container"]`，**When** Agent 调用 `query_logs_loki_main`，**Then** 函数 Description 包含 "Available labels: namespace, app, pod, container"。
3. **Given** 数据源的 Metadata 尚未发现（DiscoveredAt=null），**When** Agent 第一次需要使用该数据源，**Then** 系统自动触发一次 MetadataDiscovery 并缓存结果到 DataSourceRegistration.Metadata。

---

### User Story 2 — Context-Aware Correlated Query (Priority: P0)

作为 SRE Agent，我希望能通过一个关联查询工具，根据告警上下文（namespace/service/时间范围）一次性从多个数据源获取诊断信息。

**Acceptance Scenarios**:

1. **Given** Agent 绑定了 prometheus-main、loki-main、k8s-cluster、github-repo 四个数据源，**When** Agent 调用 `query_correlated_context(namespace="demo-app", service="order-service", lookback="1h")`，**Then** 返回结构化结果：`{ metrics: [...], logs: [...], deployments: [...], recentChanges: [...] }`。
2. **Given** 关联查询指定了 `lookback="30m"`，**When** 工具执行，**Then** 所有子查询使用相同的时间范围（now-30m → now）。
3. **Given** 某个数据源查询失败，**When** 关联查询执行中，**Then** 该数据源结果返回 `{ error: "..." }` 而非阻塞整个关联查询。
4. **Given** 关联查询的某个子结果过大（>4000 tokens），**When** 返回给 Agent，**Then** 自动截断并附加 `"[truncated, showing top 20 entries out of 156]"` 提示。

---

### User Story 3 — Mutation Tools with Approval Gate (Priority: P1)

作为 Agent，我需要能够执行 SOP 中的变更操作（重启 Pod、扩容 Deployment、回滚等），并且所有变更操作走审批流程。

**Acceptance Scenarios**:

1. **Given** 数据源为 Kubernetes 类型且 Deployment 类 DataSource 已注册，**When** DataSourceFunctionFactory 生成工具，**Then** 额外生成 `restart_pod_{name}`、`scale_deployment_{name}`、`rollback_deployment_{name}` 等变更工具,且描述中标注 `[REQUIRES APPROVAL]`。
2. **Given** Agent 调用 `restart_pod_k8s_cluster(namespace="demo-app", pod="order-service-xxx")`，**When** IncidentDispatcherService 的 StreamAgentRoundAsync 拦截到这个 FunctionCallContent，**Then** 走 ToolApproval 流程，等待人工批准后才实际执行。
3. **Given** 人工拒绝了 `scale_deployment_k8s_cluster` 的调用，**When** Agent 收到拒绝结果，**Then** Agent 跳过该步骤并记录 "操作被人工拒绝"。
4. **Given** Git 类型数据源已注册，**When** 生成工具，**Then** 包含 `create_rollback_pr_{name}` 工具（创建回滚 PR），标注 `[REQUIRES APPROVAL]`。

---

### User Story 4 — Smart Result Truncation (Priority: P1)

作为系统，我需要确保工具返回的数据不会超出 LLM 的 context window 限制。

**Acceptance Scenarios**:

1. **Given** `query_metrics` 返回 200 个时序样本，**When** 序列化后超过 maxResultTokens（默认 4000），**Then** 保留最新 20 个样本并附加 `"[truncated: showing latest 20 of 200 samples]"`。
2. **Given** `query_logs` 返回 500 条日志，**When** 序列化后超过限制，**Then** 保留最新 30 条并附加截断提示。
3. **Given** Agent 指定了 `limit=10` 参数，**When** 查询执行，**Then** 底层查询使用该 limit，返回 ≤10 条结果，不触发截断。

---

### User Story 5 — Metadata Auto-Discovery & Cache (Priority: P2)

作为系统管理员，我希望数据源的 Metadata（available metrics, labels, services）能自动发现并定期刷新。

**Acceptance Scenarios**:

1. **Given** DataSourceRegistration 的 `Metadata.DiscoveredAt` 为 null 或超过 24 小时，**When** `DataSourceFunctionFactory.CreateFunctionsAsync` 被调用，**Then** 异步触发 `IDataSourceQuerier.DiscoverMetadataAsync` 并更新缓存，不阻塞工具生成。
2. **Given** 管理员在 UI 中点击数据源的 "刷新元数据" 按钮，**When** 请求到达后端，**Then** 立即执行 DiscoverMetadataAsync 并更新 Metadata 字段。
3. **Given** Metadata 发现失败，**When** 系统记录日志，**Then** 工具仍然可用（使用无 hint 版本的 Description），不影响基本功能。

## Architecture / Design

### 1. Schema-Aware Description Enhancement

修改 `DataSourceFunctionFactory` 中每个 AIFunction 的 Description 生成逻辑：

```
Before: "Query metrics from prometheus-main using PromQL expression"
After:  "Query metrics from prometheus-main using PromQL expression. 
         Available metrics: up, http_requests_total, container_cpu_usage_seconds_total, ...
         Available labels: namespace, pod, service, job, instance
         Example: rate(http_requests_total{namespace=\"demo-app\"}[5m])"
```

### 2. Correlated Context Query Tool

新增 `CorrelatedContextFunction` — 一个复合 AIFunction，接收上下文参数后并行查询多个数据源：

```
Input:  { namespace: "demo-app", service: "order-service", lookback: "1h" }
Output: {
  metrics: { key_metrics: [...], anomalies: [...] },
  logs:    { error_logs: [...], warning_logs: [...] },
  k8s:     { pods: [...], deployments: [...], events: [...] },
  changes: { recent_commits: [...], recent_deployments: [...] }
}
```

### 3. Mutation Tool Generation

扩展 `DataSourceFunctionFactory` 对 Deployment 和 Git 类 DataSource 生成变更工具：

| Category   | Tool Name                          | Parameters                        | Approval |
|------------|-----------------------------------|-----------------------------------|----------|
| Deployment | `restart_pod_{name}`              | namespace, pod                    | Yes      |
| Deployment | `scale_deployment_{name}`         | namespace, deployment, replicas   | Yes      |
| Deployment | `rollback_deployment_{name}`      | namespace, deployment, revision   | Yes      |
| Deployment | `get_pod_logs_{name}`             | namespace, pod, container, tail   | No       |
| Deployment | `describe_resource_{name}`        | namespace, kind, name             | No       |
| Git        | `get_diff_{name}`                 | repo, base, head                  | No       |
| Git        | `create_rollback_pr_{name}`       | repo, targetBranch, commitSha     | Yes      |

### 4. Smart Result Truncation

新增 `ResultTruncator` 工具类，每个 AIFunction 返回前调用：

```csharp
public static string TruncateForLlm(string jsonResult, int maxTokenEstimate = 4000)
```

Token 估算使用 `characters / 4` 近似，超出时按数据类型策略截断（时序保留最新 N 条，日志保留最新 N 条，资源保留 status+events）。

### 5. Mutation Approval Integration

变更工具通过 `AIFunctionFactory.Create` 的返回值 JSON 中包含 `"__requires_approval": true` 元数据标记。`StreamAgentRoundAsync` 已有 `FunctionCallContent` 拦截和 `ToolApproval` 流程，变更工具只需在注册时标注即可。  

实际中，mutation 工具的 `IDataSourceQuerier` 实现会新增 `MutateAsync` 方法，只有通过审批后才调用。

## Entities & Value Objects

### New: `IDataSourceMutator` Interface

```csharp
public interface IDataSourceMutator
{
    bool CanHandle(DataSourceProduct product);
    Task<DataSourceMutationResultVO> ExecuteAsync(
        DataSourceRegistration registration,
        DataSourceMutationVO mutation,
        CancellationToken ct = default);
}
```

### New: `DataSourceMutationVO`

```csharp
public record DataSourceMutationVO
{
    public string Operation { get; init; } = string.Empty;       // restart_pod, scale_deployment, etc.
    public string? Namespace { get; init; }
    public string? ResourceName { get; init; }
    public string? ResourceKind { get; init; }
    public Dictionary<string, string> Parameters { get; init; } = [];
}
```

### New: `DataSourceMutationResultVO`

```csharp
public record DataSourceMutationResultVO
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? Detail { get; init; }
}
```

### Modified: `DataSourceRegistration`

- 新增 `GenerateAvailableMutationNames()` 方法，与现有 `GenerateAvailableFunctionNames()` 并列

### Modified: `DataSourceFunctionFactory`

- 注入 `IDataSourceMutatorFactory`
- `CreateFunctionsAsync` 同时生成只读查询和变更工具
- 变更工具 Description 带 `[REQUIRES APPROVAL]` 标记

## API Endpoints

### `POST /api/datasources/{id}/discover-metadata`

手动触发元数据发现，返回更新后的 Metadata。

### `POST /api/datasources/{id}/test-query`

用于前端测试数据源的查询能力，接收 `DataSourceQueryVO`，返回 `DataSourceResultVO`。

## Notes

- 关联查询工具名固定为 `query_correlated_context`（不带 datasource 后缀），它内部根据 Agent 绑定的所有数据源自动路由
- Metadata 自动发现使用 fire-and-forget 模式，不阻塞 Agent 解析流程
- 变更工具仅在 DataSourceRefVO 中 `EnableMutations=true` 时才生成
- 结果截断日志记录截断前后的 token 估算值，用于后续调优 maxResultTokens
