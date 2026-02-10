using AutoMapper;
using CoreSRE.Application.Agents.DTOs;
using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using MediatR;

namespace CoreSRE.Application.Agents.Commands.UpdateAgent;

/// <summary>
/// 更新 Agent 命令处理器
/// </summary>
public class UpdateAgentCommandHandler : IRequestHandler<UpdateAgentCommand, Result<AgentRegistrationDto>>
{
    private readonly IAgentRegistrationRepository _repository;
    private readonly IMapper _mapper;

    public UpdateAgentCommandHandler(IAgentRegistrationRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Result<AgentRegistrationDto>> Handle(
        UpdateAgentCommand request,
        CancellationToken cancellationToken)
    {
        var agent = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (agent is null)
            return Result<AgentRegistrationDto>.NotFound($"Agent with ID '{request.Id}' not found.");

        // Domain entity's Update method enforces type-specific invariants
        agent.Update(
            request.Name,
            request.Description,
            request.Endpoint,
            request.AgentCard is not null ? _mapper.Map<AgentCardVO>(request.AgentCard) : null,
            request.LlmConfig is not null ? _mapper.Map<LlmConfigVO>(request.LlmConfig) : null,
            request.WorkflowRef);

        // Unique name constraint violation (23505) is caught by ExceptionHandlingMiddleware → 409
        await _repository.UpdateAsync(agent, cancellationToken);

        var dto = _mapper.Map<AgentRegistrationDto>(agent);
        return Result<AgentRegistrationDto>.Ok(dto);
    }
}
