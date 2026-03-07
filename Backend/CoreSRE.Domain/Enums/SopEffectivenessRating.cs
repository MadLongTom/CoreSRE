namespace CoreSRE.Domain.Enums;

/// <summary>
/// SOP 有效性评级（仅 Chain A 适用）
/// </summary>
public enum SopEffectivenessRating
{
    /// <summary>完全有效</summary>
    Effective,

    /// <summary>部分有效</summary>
    PartiallyEffective,

    /// <summary>无效</summary>
    Ineffective,
}
