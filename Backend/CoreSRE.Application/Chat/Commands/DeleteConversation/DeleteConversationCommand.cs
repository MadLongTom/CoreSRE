using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Chat.Commands.DeleteConversation;

/// <summary>
/// 删除对话命令 — 同时删除 Conversation 实体和关联的 AgentSessionRecord。
/// </summary>
public record DeleteConversationCommand(Guid Id) : IRequest<Result<bool>>;
