namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// Agent 健康检查状态。本 spec 仅定义结构和默认值，行为由 SPEC-002 实现。
/// </summary>
public sealed record HealthCheckVO
{
    /// <summary>最后一次检查时间</summary>
    public DateTime? LastCheckTime { get; init; }

    /// <summary>是否健康</summary>
    public bool IsHealthy { get; init; }

    /// <summary>连续失败次数</summary>
    public int FailureCount { get; init; }

    /// <summary>创建默认的健康检查状态（未检查）</summary>
    public static HealthCheckVO Default() => new()
    {
        LastCheckTime = null,
        IsHealthy = false,
        FailureCount = 0
    };
}
