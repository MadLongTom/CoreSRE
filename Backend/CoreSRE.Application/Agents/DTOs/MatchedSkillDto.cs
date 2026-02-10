namespace CoreSRE.Application.Agents.DTOs;

/// <summary>
/// 搜索结果中匹配到的单个 skill 详情
/// </summary>
public class MatchedSkillDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
