using AutoMapper;
using CoreSRE.Application.Agents.DTOs;
using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using MediatR;

namespace CoreSRE.Application.Agents.Commands.RegisterAgent;

/// <summary>
/// 注册 Agent 命令处理器
/// </summary>
public class RegisterAgentCommandHandler : IRequestHandler<RegisterAgentCommand, Result<AgentRegistrationDto>>
{
    private readonly IAgentRegistrationRepository _repository;
    private readonly IMapper _mapper;

    public RegisterAgentCommandHandler(IAgentRegistrationRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Result<AgentRegistrationDto>> Handle(
        RegisterAgentCommand request,
        CancellationToken cancellationToken)
    {
        var agentType = Enum.Parse<AgentType>(request.AgentType);

        AgentRegistration agent = agentType switch
        {
            AgentType.A2A => AgentRegistration.CreateA2A(
                request.Name,
                request.Description,
                request.Endpoint!,
                _mapper.Map<AgentCardVO>(request.AgentCard!)),

            AgentType.ChatClient => AgentRegistration.CreateChatClient(
                request.Name,
                request.Description,
                _mapper.Map<LlmConfigVO>(request.LlmConfig!)),

            AgentType.Workflow => AgentRegistration.CreateWorkflow(
                request.Name,
                request.Description,
                request.WorkflowRef!.Value),

            AgentType.Team => AgentRegistration.CreateTeam(
                request.Name,
                request.Description,
                MapTeamConfig(request.TeamConfig!)),

            _ => throw new ArgumentException($"Unsupported agent type: {request.AgentType}")
        };

        // Unique name constraint violation (23505) is caught by ExceptionHandlingMiddleware → 409
        await _repository.AddAsync(agent, cancellationToken);

        var dto = _mapper.Map<AgentRegistrationDto>(agent);
        return Result<AgentRegistrationDto>.Ok(dto);
    }

    private TeamConfigVO MapTeamConfig(TeamConfigDto dto)
    {
        var mode = Enum.Parse<TeamMode>(dto.Mode);

        Dictionary<Guid, List<HandoffTargetVO>>? handoffRoutes = null;
        if (dto.HandoffRoutes is not null)
        {
            handoffRoutes = dto.HandoffRoutes.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Select(t => new HandoffTargetVO(t.TargetAgentId, t.Reason)).ToList());
        }

        return TeamConfigVO.Create(
            mode: mode,
            participantIds: dto.ParticipantIds,
            maxIterations: dto.MaxIterations,
            handoffRoutes: handoffRoutes,
            initialAgentId: dto.InitialAgentId,
            selectorProviderId: dto.SelectorProviderId,
            selectorModelId: dto.SelectorModelId,
            selectorPrompt: dto.SelectorPrompt,
            allowRepeatedSpeaker: dto.AllowRepeatedSpeaker,
            orchestratorProviderId: dto.OrchestratorProviderId,
            orchestratorModelId: dto.OrchestratorModelId,
            maxStalls: dto.MaxStalls,
            finalAnswerPrompt: dto.FinalAnswerPrompt,
            aggregationStrategy: dto.AggregationStrategy);
    }
}
