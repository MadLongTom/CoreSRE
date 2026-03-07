namespace CoreSRE.Domain.Entities;

/// <summary>
/// 金丝雀验证结果。记录 Shadow Chain A 与实际处理链路的对比。
/// </summary>
public class CanaryResult : BaseEntity
{
    public Guid AlertRuleId { get; private set; }
    public Guid IncidentId { get; private set; }
    public Guid CanarySopId { get; private set; }

    /// <summary>Shadow 执行的根因结论</summary>
    public string? ShadowRootCause { get; private set; }

    /// <summary>实际处理的根因结论</summary>
    public string? ActualRootCause { get; private set; }

    /// <summary>结论是否一致</summary>
    public bool IsConsistent { get; private set; }

    /// <summary>Shadow 工具调用序列</summary>
    public List<string> ShadowToolCalls { get; private set; } = [];

    /// <summary>Shadow Token 消耗</summary>
    public int ShadowTokenConsumed { get; private set; }

    /// <summary>Shadow 执行耗时（毫秒）</summary>
    public long ShadowDurationMs { get; private set; }

    private CanaryResult() { }

    public static CanaryResult Create(
        Guid alertRuleId,
        Guid incidentId,
        Guid canarySopId,
        string? shadowRootCause,
        string? actualRootCause,
        bool isConsistent,
        List<string> shadowToolCalls,
        int shadowTokenConsumed,
        long shadowDurationMs)
    {
        return new CanaryResult
        {
            AlertRuleId = alertRuleId,
            IncidentId = incidentId,
            CanarySopId = canarySopId,
            ShadowRootCause = shadowRootCause,
            ActualRootCause = actualRootCause,
            IsConsistent = isConsistent,
            ShadowToolCalls = shadowToolCalls,
            ShadowTokenConsumed = shadowTokenConsumed,
            ShadowDurationMs = shadowDurationMs
        };
    }
}
