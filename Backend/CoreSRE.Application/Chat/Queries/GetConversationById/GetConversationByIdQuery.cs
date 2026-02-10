using CoreSRE.Application.Chat.DTOs;
using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Chat.Queries.GetConversationById;

/// <summary>
/// 查询对话详情（含消息历史，从 AgentSessionRecord.SessionData 提取）
/// </summary>
public record GetConversationByIdQuery(Guid Id) : IRequest<Result<ConversationDto>>;
