namespace CoreSRE.Application.Chat.DTOs;

/// <summary>
/// 对话列表摘要 DTO — 用于 GET /api/chat/conversations 列表展示
/// </summary>
public class ConversationSummaryDto
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string AgentType { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? LastMessage { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
