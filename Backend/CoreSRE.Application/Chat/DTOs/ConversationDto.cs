namespace CoreSRE.Application.Chat.DTOs;

/// <summary>
/// 对话完整详情 DTO，包含消息列表（从 AgentSessionRecord.SessionData 提取）
/// </summary>
public class ConversationDto
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string AgentType { get; set; } = string.Empty;
    public string? Title { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<ChatMessageDto> Messages { get; set; } = [];
}
