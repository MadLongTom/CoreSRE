namespace CoreSRE.Application.Agents.DTOs;

/// <summary>
/// 从远程 A2A Agent 端点解析得到的 AgentCard DTO。
/// 包含 AgentCard 中表单需要的所有字段，用于前端自动填充。
/// </summary>
public class ResolvedAgentCardDto
{
    /// <summary>AgentCard 中的 Agent 名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>AgentCard 中的 Agent 描述</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>AgentCard 中记录的 URL（可能与用户输入不同）</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>AgentCard 中的版本号</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>映射后的技能列表</summary>
    public List<AgentSkillDto> Skills { get; set; } = [];

    /// <summary>映射后的接口列表</summary>
    public List<AgentInterfaceDto> Interfaces { get; set; } = [];

    /// <summary>映射后的安全方案列表</summary>
    public List<SecuritySchemeDto> SecuritySchemes { get; set; } = [];
}
