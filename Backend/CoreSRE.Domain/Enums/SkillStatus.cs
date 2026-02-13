namespace CoreSRE.Domain.Enums;

/// <summary>
/// Skill 状态
/// </summary>
public enum SkillStatus
{
    /// <summary>可被 Agent 引用</summary>
    Active,

    /// <summary>已归档，不可使用</summary>
    Inactive
}
