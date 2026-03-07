namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 上下文初始化执行结果 VO — ContextInitPipeline 的返回值。
/// </summary>
public sealed record ContextInitResultVO
{
    /// <summary>每个上下文条目的执行结果</summary>
    public List<ContextInitEntry> Entries { get; init; } = [];

    /// <summary>总执行时间</summary>
    public TimeSpan TotalDuration { get; init; }

    /// <summary>是否有任何成功的条目</summary>
    public bool HasAnySuccess => Entries.Any(e => e.Success);
}

/// <summary>
/// 单个上下文初始化条目的执行结果。
/// </summary>
public sealed record ContextInitEntry
{
    /// <summary>段落标题（来自 ContextInitItemVO.Label）</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>数据源类别</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>是否成功</summary>
    public bool Success { get; init; }

    /// <summary>查询结果 (JSON or plain text)</summary>
    public string? Result { get; init; }

    /// <summary>错误信息</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>该条目执行耗时</summary>
    public TimeSpan Duration { get; init; }
}
