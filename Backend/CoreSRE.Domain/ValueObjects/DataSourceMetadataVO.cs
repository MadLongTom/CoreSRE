namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 数据源元数据缓存。通过 POST /discover 更新。存储为 PostgreSQL JSONB 列。
/// </summary>
public sealed record DataSourceMetadataVO
{
    /// <summary>元数据发现时间</summary>
    public DateTime? DiscoveredAt { get; init; }

    /// <summary>可用标签名/键列表（Prometheus labels, Loki labels）</summary>
    public List<string>? Labels { get; init; }

    /// <summary>可用索引列表（Elasticsearch indices）</summary>
    public List<string>? Indices { get; init; }

    /// <summary>可用服务名列表（Jaeger/Tempo services）</summary>
    public List<string>? Services { get; init; }

    /// <summary>可用命名空间列表（Kubernetes namespaces）</summary>
    public List<string>? Namespaces { get; init; }

    /// <summary>该数据源可生成的 AIFunction 名称列表（由 Category + Product + Name 决定）</summary>
    public List<string>? AvailableFunctions { get; init; }
}
