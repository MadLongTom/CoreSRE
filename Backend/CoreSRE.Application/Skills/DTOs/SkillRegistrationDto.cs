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
    public string Scope { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<Guid> RequiresTools { get; set; } = [];
    public bool HasFiles { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
