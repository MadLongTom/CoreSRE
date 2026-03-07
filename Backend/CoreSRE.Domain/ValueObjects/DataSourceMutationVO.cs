namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 数据源变更操作 VO — 描述一个对数据源的写入/变更操作。
/// </summary>
public sealed record DataSourceMutationVO
{
    /// <summary>操作类型: restart_pod, scale_deployment, rollback_deployment, etc.</summary>
    public string Operation { get; init; } = string.Empty;

    /// <summary>Kubernetes namespace</summary>
    public string? Namespace { get; init; }

    /// <summary>资源名称</summary>
    public string? ResourceName { get; init; }

    /// <summary>资源类型 (Pod, Deployment, etc.)</summary>
    public string? ResourceKind { get; init; }

    /// <summary>额外操作参数</summary>
    public Dictionary<string, string> Parameters { get; init; } = [];
}

/// <summary>
/// 数据源变更操作结果 VO。
/// </summary>
public sealed record DataSourceMutationResultVO
{
    /// <summary>是否成功</summary>
    public bool Success { get; init; }

    /// <summary>结果消息</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>详细信息 (JSON)</summary>
    public string? Detail { get; init; }
}
