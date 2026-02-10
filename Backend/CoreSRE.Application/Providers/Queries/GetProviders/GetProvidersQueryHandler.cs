using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Providers.DTOs;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Providers.Queries.GetProviders;

/// <summary>
/// 查询所有 Provider 列表处理器
/// </summary>
public class GetProvidersQueryHandler : IRequestHandler<GetProvidersQuery, Result<List<LlmProviderSummaryDto>>>
{
    private readonly ILlmProviderRepository _repository;
    private readonly IMapper _mapper;

    public GetProvidersQueryHandler(ILlmProviderRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Result<List<LlmProviderSummaryDto>>> Handle(
        GetProvidersQuery request,
        CancellationToken cancellationToken)
    {
        var providers = await _repository.GetAllAsync(cancellationToken);
        var dtos = _mapper.Map<List<LlmProviderSummaryDto>>(providers);
        return Result<List<LlmProviderSummaryDto>>.Ok(dtos);
    }
}
