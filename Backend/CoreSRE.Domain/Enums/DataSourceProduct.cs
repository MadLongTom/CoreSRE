namespace CoreSRE.Domain.Enums;

/// <summary>
/// 数据源具体产品。每个 Product 对应不同的查询 API 和认证方式。
/// </summary>
public enum DataSourceProduct
{
    // ── Metrics ──
    /// <summary>Prometheus 及兼容 API（VictoriaMetrics、Mimir 共用同一 Querier）</summary>
    Prometheus,

    /// <summary>VictoriaMetrics — Prometheus 兼容 API</summary>
    VictoriaMetrics,

    /// <summary>Grafana Mimir — Prometheus 兼容 API</summary>
    Mimir,

    // ── Logs ──
    /// <summary>Grafana Loki — LogQL 查询</summary>
    Loki,

    /// <summary>Elasticsearch / OpenSearch — Query DSL</summary>
    Elasticsearch,

    // ── Tracing ──
    /// <summary>Jaeger — 分布式追踪查询</summary>
    Jaeger,

    /// <summary>Grafana Tempo — 分布式追踪查询</summary>
    Tempo,

    // ── Alerting ──
    /// <summary>Prometheus Alertmanager — 告警管理</summary>
    Alertmanager,

    /// <summary>PagerDuty — 告警管理</summary>
    PagerDuty,

    // ── Deployment ──
    /// <summary>Kubernetes — 集群资源管理</summary>
    Kubernetes,

    /// <summary>ArgoCD — GitOps 持续部署</summary>
    ArgoCD,

    // ── Git / SCM ──
    /// <summary>GitHub — 代码仓库与 CI/CD</summary>
    GitHub,

    /// <summary>GitLab — 代码仓库与 CI/CD</summary>
    GitLab
}
