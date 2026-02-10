namespace CoreSRE.Application.Chat.DTOs;

/// <summary>
/// 聊天消息 DTO — 从 AgentSessionRecord.SessionData JSONB 中投影提取，非独立数据库实体。
/// </summary>
public class ChatMessageDto
{
    /// <summary>消息在对话中的位置（0-based 数组索引）</summary>
    public int Index { get; set; }

    /// <summary>发送方角色：user 或 assistant</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>消息文本内容</summary>
    public string Content { get; set; } = string.Empty;
}
