namespace CoreSRE.Application.Skills.DTOs;

/// <summary>
/// Skill 注册完整详情 DTO
/// </summary>
public class SkillRegistrationDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;

    // Agent Skills 规范字段
    public string? License { get; set; }
    public string? Compatibility { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    public List<Guid> AllowedTools { get; set; } = [];

    public string Scope { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<Guid> RequiresTools { get; set; } = [];
    public bool HasFiles { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
