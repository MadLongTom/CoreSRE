using CoreSRE.Domain.Enums;

namespace CoreSRE.Application.Skills.DTOs;

/// <summary>
/// SOP 干运行结果
/// </summary>
public sealed record DryRunResultDto
{
    /// <summary>整体状态</summary>
    public DryRunStatus OverallStatus { get; init; }

    /// <summary>每步执行结果</summary>
    public List<DryRunStepResultDto> Steps { get; init; } = [];

    /// <summary>总耗时（毫秒）</summary>
    public long TotalDurationMs { get; init; }

    /// <summary>Agent 完整推理日志</summary>
    public string AgentReasoningLog { get; init; } = string.Empty;
}

/// <summary>
/// 单步干运行结果
/// </summary>
public sealed record DryRunStepResultDto
{
    /// <summary>步骤序号（1-based）</summary>
    public int StepNumber { get; init; }

    /// <summary>步骤状态</summary>
    public DryRunStepStatus Status { get; init; }

    /// <summary>Agent 输出摘要</summary>
    public string AgentOutput { get; init; } = string.Empty;

    /// <summary>该步骤耗时（毫秒）</summary>
    public long DurationMs { get; init; }
}

/// <summary>
/// 单步干运行状态
/// </summary>
public enum DryRunStepStatus
{
    Passed,
    Skipped,
    Failed,
}
