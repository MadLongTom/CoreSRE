namespace CoreSRE.Application.Agents.DTOs;

/// <summary>
/// 搜索结果中的单个 Agent 条目，包含摘要信息和匹配到的 skill 列表
/// </summary>
public class AgentSearchResultDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AgentType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<MatchedSkillDto> MatchedSkills { get; set; } = [];

    /// <summary>语义相似度评分（P2 only），关键词模式为 null</summary>
    public double? SimilarityScore { get; set; }
}
