namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 上下文初始化条目 VO — 声明 Agent 对话开始前需要预查询的数据源数据。
/// 可在 AlertRule.ContextProviders 或 SOP Markdown 的 "## 初始化上下文" 段落中声明。
/// </summary>
public sealed record ContextInitItemVO
{
    /// <summary>数据源类别: metrics, logs, k8s, git, alerting, tracing</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>查询表达式 (PromQL, LogQL, resource selector, etc.)，支持 ${label} 占位符</summary>
    public string Expression { get; init; } = string.Empty;

    /// <summary>人类可读描述，用于消息中的段落标题</summary>
    public string? Label { get; init; }

    /// <summary>时间回溯窗口，如 "1h", "30m"</summary>
    public string? Lookback { get; init; }

    /// <summary>额外参数</summary>
    public Dictionary<string, string>? ExtraParams { get; init; }
}
