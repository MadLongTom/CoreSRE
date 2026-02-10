namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// Agent 的单项技能描述，嵌套在 AgentCardVO 中
/// </summary>
public sealed record AgentSkillVO
{
    /// <summary>技能名称</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>技能描述</summary>
    public string? Description { get; init; }
}
