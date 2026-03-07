using System.Text.Json;
using CoreSRE.Domain.Enums;

namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// SOP 步骤执行记录值对象
/// </summary>
public sealed record SopStepExecutionVO
{
    /// <summary>步骤编号（1-based）</summary>
    public int StepNumber { get; init; }

    /// <summary>执行状态</summary>
    public StepExecutionStatus Status { get; init; } = StepExecutionStatus.Pending;

    /// <summary>开始时间</summary>
    public DateTime? StartedAt { get; init; }

    /// <summary>完成时间</summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>工具调用返回结果</summary>
    public JsonElement? ToolCallResult { get; init; }

    /// <summary>Agent 对结果的判断结论</summary>
    public string? AgentJudgment { get; init; }

    /// <summary>重试次数</summary>
    public int RetryCount { get; init; }

    /// <summary>参数偏差记录</summary>
    public List<string>? ParameterDeviations { get; init; }

    /// <summary>错误信息</summary>
    public string? ErrorMessage { get; init; }

    public static SopStepExecutionVO CreatePending(int stepNumber) => new()
    {
        StepNumber = stepNumber,
        Status = StepExecutionStatus.Pending,
    };

    public SopStepExecutionVO MarkRunning() => this with
    {
        Status = StepExecutionStatus.Running,
        StartedAt = DateTime.UtcNow,
    };

    public SopStepExecutionVO MarkCompleted(JsonElement? toolResult, string? agentJudgment) => this with
    {
        Status = StepExecutionStatus.Completed,
        CompletedAt = DateTime.UtcNow,
        ToolCallResult = toolResult,
        AgentJudgment = agentJudgment,
    };

    public SopStepExecutionVO MarkFailed(string errorMessage) => this with
    {
        Status = StepExecutionStatus.Failed,
        CompletedAt = DateTime.UtcNow,
        ErrorMessage = errorMessage,
    };

    public SopStepExecutionVO MarkSkipped(string reason) => this with
    {
        Status = StepExecutionStatus.Skipped,
        CompletedAt = DateTime.UtcNow,
        AgentJudgment = reason,
    };

    public SopStepExecutionVO IncrementRetry() => this with
    {
        RetryCount = RetryCount + 1,
        Status = StepExecutionStatus.Pending,
        CompletedAt = null,
        ErrorMessage = null,
    };
}
