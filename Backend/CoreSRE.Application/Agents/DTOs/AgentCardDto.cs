namespace CoreSRE.Application.Agents.DTOs;

/// <summary>
/// A2A Agent 的 AgentCard DTO
/// </summary>
public class AgentCardDto
{
    public List<AgentSkillDto> Skills { get; set; } = [];
    public List<AgentInterfaceDto> Interfaces { get; set; } = [];
    public List<SecuritySchemeDto> SecuritySchemes { get; set; } = [];
}

public class AgentSkillDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class AgentInterfaceDto
{
    public string Protocol { get; set; } = string.Empty;
    public string? Path { get; set; }
}

public class SecuritySchemeDto
{
    public string Type { get; set; } = string.Empty;
    public string? Parameters { get; set; }
}
