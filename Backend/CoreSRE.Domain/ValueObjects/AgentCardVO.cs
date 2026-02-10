namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// A2A Agent 的协议描述卡片。存储为 PostgreSQL JSONB 列。
/// </summary>
public sealed record AgentCardVO
{
    /// <summary>技能列表</summary>
    public List<AgentSkillVO> Skills { get; init; } = [];

    /// <summary>通信接口列表</summary>
    public List<AgentInterfaceVO> Interfaces { get; init; } = [];

    /// <summary>安全认证方案列表</summary>
    public List<SecuritySchemeVO> SecuritySchemes { get; init; } = [];
}
