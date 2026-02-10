using AutoMapper;
using CoreSRE.Application.Chat.DTOs;
using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Chat.Commands.CreateConversation;

/// <summary>
/// 创建对话命令处理器
/// </summary>
public class CreateConversationCommandHandler : IRequestHandler<CreateConversationCommand, Result<ConversationDto>>
{
    private readonly IConversationRepository _conversationRepository;
    private readonly IAgentRegistrationRepository _agentRepository;
    private readonly IMapper _mapper;

    public CreateConversationCommandHandler(
        IConversationRepository conversationRepository,
        IAgentRegistrationRepository agentRepository,
        IMapper mapper)
    {
        _conversationRepository = conversationRepository;
        _agentRepository = agentRepository;
        _mapper = mapper;
    }

    public async Task<Result<ConversationDto>> Handle(
        CreateConversationCommand request,
        CancellationToken cancellationToken)
    {
        // 验证 Agent 是否存在
        var agent = await _agentRepository.GetByIdAsync(request.AgentId, cancellationToken);
        if (agent is null)
            return Result<ConversationDto>.NotFound($"Agent with ID '{request.AgentId}' not found.");

        // 创建对话
        var conversation = Conversation.Create(request.AgentId);
        await _conversationRepository.AddAsync(conversation, cancellationToken);

        // 映射 DTO
        var dto = _mapper.Map<ConversationDto>(conversation);
        dto.AgentName = agent.Name;
        dto.AgentType = agent.AgentType.ToString();

        return Result<ConversationDto>.Ok(dto);
    }
}
