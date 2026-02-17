namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 数据源默认查询配置。存储为 PostgreSQL JSONB 列。
/// </summary>
public sealed record QueryConfigVO
{
    /// <summary>默认标签过滤 (如 {"env": "production"})</summary>
    public Dictionary<string, string>? DefaultLabels { get; init; }

    /// <summary>K8s 默认命名空间</summary>
    public string? DefaultNamespace { get; init; }

    /// <summary>每次查询最大返回条数</summary>
    public int? MaxResults { get; init; }

    /// <summary>Prometheus/Metrics 默认查询步长（如 "15s", "1m"）</summary>
    public string? DefaultStep { get; init; }

    /// <summary>Elasticsearch 默认索引模式（如 "app-logs-*"）</summary>
    public string? DefaultIndex { get; init; }
}
