using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Providers.DTOs;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Providers.Queries.GetProviderById;

/// <summary>
/// 按 ID 查询 Provider 详情处理器
/// </summary>
public class GetProviderByIdQueryHandler : IRequestHandler<GetProviderByIdQuery, Result<LlmProviderDto>>
{
    private readonly ILlmProviderRepository _repository;
    private readonly IMapper _mapper;

    public GetProviderByIdQueryHandler(ILlmProviderRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Result<LlmProviderDto>> Handle(
        GetProviderByIdQuery request,
        CancellationToken cancellationToken)
    {
        var provider = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (provider is null)
            return Result<LlmProviderDto>.NotFound($"Provider with ID '{request.Id}' not found.");

        var dto = _mapper.Map<LlmProviderDto>(provider);
        return Result<LlmProviderDto>.Ok(dto);
    }
}
