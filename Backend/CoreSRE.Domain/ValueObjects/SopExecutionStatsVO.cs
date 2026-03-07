namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// SOP 执行统计（滚动窗口），嵌入 SkillRegistration 实体。
/// </summary>
public sealed record SopExecutionStatsVO
{
    public int TotalExecutions { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public int TimeoutCount { get; init; }

    /// <summary>最近 20 次执行结果（true = 成功）</summary>
    public List<bool> RecentResults { get; init; } = [];

    /// <summary>滚动成功率（0.0 ~ 1.0）</summary>
    public double RollingSuccessRate { get; init; }

    /// <summary>平均 MTTR（毫秒）</summary>
    public long AverageMttrMs { get; init; }

    public DateTime? LastExecutedAt { get; init; }

    private const int WindowSize = 20;

    public static SopExecutionStatsVO Empty() => new();

    /// <summary>记录一次执行结果</summary>
    public SopExecutionStatsVO RecordExecution(bool success, bool timeout, long mttrMs)
    {
        var newRecent = new List<bool>(RecentResults);
        newRecent.Add(success);
        if (newRecent.Count > WindowSize)
            newRecent.RemoveAt(0);

        var rollingRate = newRecent.Count > 0
            ? (double)newRecent.Count(r => r) / newRecent.Count
            : 0.0;

        var newTotal = TotalExecutions + 1;
        var newAvgMttr = TotalExecutions > 0
            ? (AverageMttrMs * TotalExecutions + mttrMs) / newTotal
            : mttrMs;

        return this with
        {
            TotalExecutions = newTotal,
            SuccessCount = SuccessCount + (success ? 1 : 0),
            FailureCount = FailureCount + (!success && !timeout ? 1 : 0),
            TimeoutCount = TimeoutCount + (timeout ? 1 : 0),
            RecentResults = newRecent,
            RollingSuccessRate = rollingRate,
            AverageMttrMs = newAvgMttr,
            LastExecutedAt = DateTime.UtcNow
        };
    }
}
