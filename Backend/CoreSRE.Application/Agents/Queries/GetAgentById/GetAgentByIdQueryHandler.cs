using AutoMapper;
using CoreSRE.Application.Agents.DTOs;
using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Agents.Queries.GetAgentById;

/// <summary>
/// 按 ID 查询 Agent 详情处理器
/// </summary>
public class GetAgentByIdQueryHandler : IRequestHandler<GetAgentByIdQuery, Result<AgentRegistrationDto>>
{
    private readonly IAgentRegistrationRepository _repository;
    private readonly IMapper _mapper;

    public GetAgentByIdQueryHandler(IAgentRegistrationRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Result<AgentRegistrationDto>> Handle(
        GetAgentByIdQuery request,
        CancellationToken cancellationToken)
    {
        var agent = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (agent is null)
            return Result<AgentRegistrationDto>.NotFound($"Agent with ID '{request.Id}' not found.");

        var dto = _mapper.Map<AgentRegistrationDto>(agent);
        return Result<AgentRegistrationDto>.Ok(dto);
    }
}
