namespace CoreSRE.Domain.Enums;

/// <summary>
/// RCA 准确性评级
/// </summary>
public enum RcaAccuracyRating
{
    /// <summary>根因完全正确</summary>
    Accurate,

    /// <summary>部分正确（方向对但不够精确）</summary>
    PartiallyAccurate,

    /// <summary>不正确</summary>
    Inaccurate,

    /// <summary>不适用（如无对话历史）</summary>
    NotApplicable,
}
