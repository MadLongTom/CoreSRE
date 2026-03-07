namespace CoreSRE.Domain.Enums;

/// <summary>
/// Prompt 优化建议状态
/// </summary>
public enum SuggestionStatus
{
    /// <summary>待处理</summary>
    Pending,

    /// <summary>已应用</summary>
    Applied,

    /// <summary>已驳回</summary>
    Rejected,

    /// <summary>自动回退</summary>
    AutoReverted
}
