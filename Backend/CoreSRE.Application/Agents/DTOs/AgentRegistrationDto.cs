namespace CoreSRE.Application.Agents.DTOs;

/// <summary>
/// Agent 完整详情 DTO，包含所有类型特有字段
/// </summary>
public class AgentRegistrationDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string AgentType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
    public AgentCardDto? AgentCard { get; set; }
    public LlmConfigDto? LlmConfig { get; set; }
    public Guid? WorkflowRef { get; set; }
    public TeamConfigDto? TeamConfig { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
