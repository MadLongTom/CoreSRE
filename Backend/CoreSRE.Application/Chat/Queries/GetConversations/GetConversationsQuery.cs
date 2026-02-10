using CoreSRE.Application.Chat.DTOs;
using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Chat.Queries.GetConversations;

/// <summary>
/// 查询对话列表（按最近活跃排序）
/// </summary>
public record GetConversationsQuery : IRequest<Result<List<ConversationSummaryDto>>>;
