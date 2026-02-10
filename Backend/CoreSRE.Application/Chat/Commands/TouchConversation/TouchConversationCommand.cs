using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Chat.Commands.TouchConversation;

/// <summary>
/// 触碰对话命令 — 更新 UpdatedAt 时间戳，首次时设置标题
/// </summary>
public record TouchConversationCommand : IRequest<Result<bool>>
{
    public Guid ConversationId { get; init; }

    /// <summary>用户发送的第一条消息文本（用于自动生成标题，仅首次有效）</summary>
    public string? FirstMessage { get; init; }
}
