namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 数据源健康检查状态。存储为 PostgreSQL JSONB 列。
/// </summary>
public sealed record DataSourceHealthVO
{
    /// <summary>最后检查时间</summary>
    public DateTime? LastCheckAt { get; init; }

    /// <summary>是否健康</summary>
    public bool IsHealthy { get; init; }

    /// <summary>错误信息</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>数据源版本号（通过 API 获取）</summary>
    public string? Version { get; init; }

    /// <summary>最后健康检查响应耗时（毫秒）</summary>
    public int? ResponseTimeMs { get; init; }

    /// <summary>创建默认的健康检查状态（未检查）</summary>
    public static DataSourceHealthVO Default() => new()
    {
        LastCheckAt = null,
        IsHealthy = false,
        ErrorMessage = null,
        Version = null,
        ResponseTimeMs = null
    };
}
