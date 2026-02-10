using CoreSRE.Application.Chat.DTOs;
using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Chat.Commands.CreateConversation;

/// <summary>
/// 创建对话命令 — 绑定到指定 Agent（不可变更）
/// </summary>
public record CreateConversationCommand : IRequest<Result<ConversationDto>>
{
    public Guid AgentId { get; init; }
}
