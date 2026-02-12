namespace CoreSRE.Application.Chat.DTOs;

/// <summary>
/// 聊天消息 DTO — 从 AgentSessionRecord.SessionData JSONB 中投影提取，非独立数据库实体。
/// </summary>
public class ChatMessageDto
{
    /// <summary>消息在对话中的位置（0-based 数组索引）</summary>
    public int Index { get; set; }

    /// <summary>发送方角色：user / assistant / tool</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>消息文本内容</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>工具调用（仅 assistant 消息可能包含）</summary>
    public List<ToolCallDto>? ToolCalls { get; set; }

    /// <summary>
    /// 本次用户提问时注入的语义记忆内容。
    /// 仅在 user 消息上出现，前端可据此显示"已使用记忆"提示。
    /// </summary>
    public string? MemoryContext { get; set; }
}

/// <summary>
/// 工具调用 DTO — 展示一次函数调用的参数和结果。
/// </summary>
public class ToolCallDto
{
    public string ToolCallId { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string Status { get; set; } = "completed";
    public string? Args { get; set; }
    public string? Result { get; set; }
}
