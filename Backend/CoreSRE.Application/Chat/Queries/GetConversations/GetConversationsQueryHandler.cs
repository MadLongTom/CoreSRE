using AutoMapper;
using CoreSRE.Application.Chat.DTOs;
using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Chat.Queries.GetConversations;

/// <summary>
/// 查询对话列表处理器 — 加载所有对话并关联 Agent 信息。
/// 注：LastMessage 预览需要从 AgentSessionRecord.SessionData 读取，
/// 但列表查询为减少 I/O 暂不加载消息预览（前端通过 title 展示即可）。
/// </summary>
public class GetConversationsQueryHandler : IRequestHandler<GetConversationsQuery, Result<List<ConversationSummaryDto>>>
{
    private readonly IConversationRepository _conversationRepository;
    private readonly IAgentRegistrationRepository _agentRepository;
    private readonly IMapper _mapper;

    public GetConversationsQueryHandler(
        IConversationRepository conversationRepository,
        IAgentRegistrationRepository agentRepository,
        IMapper mapper)
    {
        _conversationRepository = conversationRepository;
        _agentRepository = agentRepository;
        _mapper = mapper;
    }

    public async Task<Result<List<ConversationSummaryDto>>> Handle(
        GetConversationsQuery request,
        CancellationToken cancellationToken)
    {
        var conversations = await _conversationRepository.GetAllOrderedByUpdatedAtAsync(cancellationToken);

        var dtos = new List<ConversationSummaryDto>();
        foreach (var conversation in conversations)
        {
            var dto = _mapper.Map<ConversationSummaryDto>(conversation);

            // 关联 Agent 名称和类型
            var agent = await _agentRepository.GetByIdAsync(conversation.AgentId, cancellationToken);
            if (agent is not null)
            {
                dto.AgentName = agent.Name;
                dto.AgentType = agent.AgentType.ToString();
            }

            dto.LastMessageAt = conversation.UpdatedAt ?? conversation.CreatedAt;
            dtos.Add(dto);
        }

        return Result<List<ConversationSummaryDto>>.Ok(dtos);
    }
}
