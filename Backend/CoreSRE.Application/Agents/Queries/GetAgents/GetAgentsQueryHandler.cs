using AutoMapper;
using CoreSRE.Application.Agents.DTOs;
using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Agents.Queries.GetAgents;

/// <summary>
/// 查询 Agent 列表处理器
/// </summary>
public class GetAgentsQueryHandler : IRequestHandler<GetAgentsQuery, Result<List<AgentSummaryDto>>>
{
    private readonly IAgentRegistrationRepository _repository;
    private readonly IMapper _mapper;

    public GetAgentsQueryHandler(IAgentRegistrationRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Result<List<AgentSummaryDto>>> Handle(
        GetAgentsQuery request,
        CancellationToken cancellationToken)
    {
        var agents = await _repository.GetByTypeAsync(request.Type, cancellationToken);
        var dtos = _mapper.Map<List<AgentSummaryDto>>(agents);
        return Result<List<AgentSummaryDto>>.Ok(dtos);
    }
}
