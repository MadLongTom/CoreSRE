namespace CoreSRE.Domain.Enums;

/// <summary>
/// 数据源语义类别。Agent 按 Category 推理（「问指标」vs「问日志」），不关心底层产品。
/// </summary>
public enum DataSourceCategory
{
    /// <summary>指标数据源 — Prometheus, VictoriaMetrics, Mimir</summary>
    Metrics,

    /// <summary>日志数据源 — Loki, Elasticsearch, OpenSearch</summary>
    Logs,

    /// <summary>链路追踪数据源 — Jaeger, Tempo, Zipkin</summary>
    Tracing,

    /// <summary>告警数据源 — Alertmanager, PagerDuty, OpsGenie</summary>
    Alerting,

    /// <summary>部署数据源 — Kubernetes, ArgoCD, Flux</summary>
    Deployment,

    /// <summary>代码/SCM 数据源 — GitHub, GitLab, Azure DevOps</summary>
    Git
}
