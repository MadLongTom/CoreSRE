namespace CoreSRE.Application.Agents.DTOs;

/// <summary>
/// Agent 列表摘要 DTO
/// </summary>
public class AgentSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AgentType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
