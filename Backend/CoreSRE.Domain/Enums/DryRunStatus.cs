namespace CoreSRE.Domain.Enums;

/// <summary>
/// SOP 干运行整体结果状态
/// </summary>
public enum DryRunStatus
{
    /// <summary>全部步骤通过</summary>
    Passed,

    /// <summary>部分步骤通过</summary>
    PartiallyPassed,

    /// <summary>整体失败或超时</summary>
    Failed,
}
