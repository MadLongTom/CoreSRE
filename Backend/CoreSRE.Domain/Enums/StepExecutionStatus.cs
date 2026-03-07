namespace CoreSRE.Domain.Enums;

/// <summary>
/// SOP 步骤执行状态
/// </summary>
public enum StepExecutionStatus
{
    /// <summary>待执行</summary>
    Pending,

    /// <summary>执行中</summary>
    Running,

    /// <summary>已完成</summary>
    Completed,

    /// <summary>失败</summary>
    Failed,

    /// <summary>已跳过</summary>
    Skipped,
}
