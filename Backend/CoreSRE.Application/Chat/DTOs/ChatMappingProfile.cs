using AutoMapper;
using CoreSRE.Domain.Entities;

namespace CoreSRE.Application.Chat.DTOs;

/// <summary>
/// AutoMapper 映射配置：Conversation 实体 → DTOs
/// 注：Messages 和 Agent 信息由 QueryHandler 手动赋值，不走 AutoMapper
/// </summary>
public class ChatMappingProfile : Profile
{
    public ChatMappingProfile()
    {
        // Entity → Summary DTO (AgentName/AgentType/LastMessage 由 Handler 手动赋值)
        CreateMap<Conversation, ConversationSummaryDto>()
            .ForMember(d => d.AgentName, opt => opt.Ignore())
            .ForMember(d => d.AgentType, opt => opt.Ignore())
            .ForMember(d => d.LastMessage, opt => opt.Ignore())
            .ForMember(d => d.LastMessageAt, opt => opt.Ignore());

        // Entity → Detail DTO (AgentName/AgentType/Messages 由 Handler 手动赋值)
        CreateMap<Conversation, ConversationDto>()
            .ForMember(d => d.AgentName, opt => opt.Ignore())
            .ForMember(d => d.AgentType, opt => opt.Ignore())
            .ForMember(d => d.Messages, opt => opt.Ignore());
    }
}
