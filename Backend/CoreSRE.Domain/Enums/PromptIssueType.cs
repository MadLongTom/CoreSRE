namespace CoreSRE.Domain.Enums;

/// <summary>
/// Prompt 优化建议的问题类型
/// </summary>
public enum PromptIssueType
{
    /// <summary>重复调用同一工具且参数未变</summary>
    RepeatedToolCalls,

    /// <summary>调用未声明的工具</summary>
    UndeclaredToolUsage,

    /// <summary>Token 消耗过高</summary>
    HighTokenUsage,

    /// <summary>准确率低</summary>
    LowAccuracy,

    /// <summary>其他</summary>
    Other
}
