using CoreSRE.Domain.Enums;

namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// Incident Post-mortem 标注值对象。由 SRE 在 Incident 关闭后填写。
/// </summary>
public sealed record PostMortemAnnotationVO
{
    /// <summary>实际根因描述</summary>
    public string ActualRootCause { get; init; } = string.Empty;

    /// <summary>RCA 准确性评级</summary>
    public RcaAccuracyRating RcaAccuracy { get; init; }

    /// <summary>SOP 有效性评级（仅 Chain A 适用）</summary>
    public SopEffectivenessRating? SopEffectiveness { get; init; }

    /// <summary>改进建议</summary>
    public string? ImprovementNotes { get; init; }

    /// <summary>标注人</summary>
    public string AnnotatedBy { get; init; } = string.Empty;

    /// <summary>标注时间</summary>
    public DateTime AnnotatedAt { get; init; } = DateTime.UtcNow;

    public static PostMortemAnnotationVO Create(
        string actualRootCause,
        RcaAccuracyRating rcaAccuracy,
        string annotatedBy,
        SopEffectivenessRating? sopEffectiveness = null,
        string? improvementNotes = null) => new()
    {
        ActualRootCause = actualRootCause,
        RcaAccuracy = rcaAccuracy,
        SopEffectiveness = sopEffectiveness,
        ImprovementNotes = improvementNotes,
        AnnotatedBy = annotatedBy,
        AnnotatedAt = DateTime.UtcNow
    };
}
