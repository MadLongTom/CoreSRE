using System.Text.Json;
using AutoMapper;
using CoreSRE.Application.Chat.DTOs;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Chat.Queries.GetConversationById;

/// <summary>
/// 查询对话详情处理器 — 加载对话元数据 + 从 AgentSessionRecord.SessionData 提取消息历史。
/// SessionData JSON 结构：{ chatHistoryProviderState: { messages: [{ role, contents: [{ text, type }] }] } }
/// </summary>
public class GetConversationByIdQueryHandler : IRequestHandler<GetConversationByIdQuery, Result<ConversationDto>>
{
    private readonly IConversationRepository _conversationRepository;
    private readonly IAgentRegistrationRepository _agentRepository;
    private readonly IChatHistoryReader _chatHistoryReader;
    private readonly IMapper _mapper;

    public GetConversationByIdQueryHandler(
        IConversationRepository conversationRepository,
        IAgentRegistrationRepository agentRepository,
        IChatHistoryReader chatHistoryReader,
        IMapper mapper)
    {
        _conversationRepository = conversationRepository;
        _agentRepository = agentRepository;
        _chatHistoryReader = chatHistoryReader;
        _mapper = mapper;
    }

    public async Task<Result<ConversationDto>> Handle(
        GetConversationByIdQuery request,
        CancellationToken cancellationToken)
    {
        var conversation = await _conversationRepository.GetByIdAsync(request.Id, cancellationToken);
        if (conversation is null)
            return Result<ConversationDto>.NotFound($"Conversation with ID '{request.Id}' not found.");

        var dto = _mapper.Map<ConversationDto>(conversation);

        // 加载 Agent 信息
        var agent = await _agentRepository.GetByIdAsync(conversation.AgentId, cancellationToken);
        if (agent is not null)
        {
            dto.AgentName = agent.Name;
            dto.AgentType = agent.AgentType.ToString();
        }

        // 从 AgentSessionRecord.SessionData 提取消息
        // AgentSessionRecord 使用 (agentId, conversationId) 复合主键
        var agentId = conversation.AgentId.ToString();
        var sessionData = await _chatHistoryReader.GetSessionDataAsync(
            agentId, conversation.Id.ToString(), cancellationToken);

        if (sessionData.HasValue)
        {
            dto.Messages = ExtractMessages(sessionData.Value);
        }

        return Result<ConversationDto>.Ok(dto);
    }

    /// <summary>
    /// 从 SessionData JSON 提取消息列表。
    /// 路径：chatHistoryProviderState.messages[].{role, contents[0].text}
    /// </summary>
    private static List<ChatMessageDto> ExtractMessages(JsonElement sessionData)
    {
        var messages = new List<ChatMessageDto>();

        if (!sessionData.TryGetProperty("chatHistoryProviderState", out var historyState))
            return messages;

        if (!historyState.TryGetProperty("messages", out var messagesArray))
            return messages;

        var index = 0;
        foreach (var msg in messagesArray.EnumerateArray())
        {
            var role = msg.TryGetProperty("role", out var roleProp)
                ? roleProp.GetString() ?? "user"
                : "user";

            // 跳过 system 消息
            if (role == "system") continue;

            var content = "";
            if (msg.TryGetProperty("contents", out var contentsProp))
            {
                foreach (var c in contentsProp.EnumerateArray())
                {
                    if (c.TryGetProperty("text", out var textProp))
                    {
                        content = textProp.GetString() ?? "";
                        break; // 取第一个 text 内容
                    }
                }
            }

            messages.Add(new ChatMessageDto
            {
                Index = index++,
                Role = role,
                Content = content
            });
        }

        return messages;
    }
}
