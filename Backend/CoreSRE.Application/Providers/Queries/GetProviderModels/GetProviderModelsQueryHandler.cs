using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Providers.DTOs;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Providers.Queries.GetProviderModels;

/// <summary>
/// 查询 Provider 已发现模型列表处理器
/// </summary>
public class GetProviderModelsQueryHandler : IRequestHandler<GetProviderModelsQuery, Result<List<DiscoveredModelDto>>>
{
    private readonly ILlmProviderRepository _repository;

    public GetProviderModelsQueryHandler(ILlmProviderRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<List<DiscoveredModelDto>>> Handle(
        GetProviderModelsQuery request,
        CancellationToken cancellationToken)
    {
        var provider = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (provider is null)
            return Result<List<DiscoveredModelDto>>.NotFound($"Provider with ID '{request.Id}' not found.");

        var models = provider.DiscoveredModels
            .Select(id => new DiscoveredModelDto { Id = id })
            .ToList();

        return Result<List<DiscoveredModelDto>>.Ok(models);
    }
}
