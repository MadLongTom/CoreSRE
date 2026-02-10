using AutoMapper;
using CoreSRE.Application.Common.Interfaces;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Providers.DTOs;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Providers.Commands.DiscoverModels;

/// <summary>
/// 触发模型发现命令处理器
/// </summary>
public class DiscoverModelsCommandHandler : IRequestHandler<DiscoverModelsCommand, Result<LlmProviderDto>>
{
    private readonly ILlmProviderRepository _repository;
    private readonly IModelDiscoveryService _discoveryService;
    private readonly IMapper _mapper;

    public DiscoverModelsCommandHandler(
        ILlmProviderRepository repository,
        IModelDiscoveryService discoveryService,
        IMapper mapper)
    {
        _repository = repository;
        _discoveryService = discoveryService;
        _mapper = mapper;
    }

    public async Task<Result<LlmProviderDto>> Handle(
        DiscoverModelsCommand request,
        CancellationToken cancellationToken)
    {
        var provider = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (provider is null)
            return Result<LlmProviderDto>.NotFound($"Provider with ID '{request.Id}' not found.");

        try
        {
            var modelIds = await _discoveryService.DiscoverModelsAsync(
                provider.BaseUrl, provider.ApiKey, cancellationToken);

            provider.UpdateDiscoveredModels(modelIds);
            await _repository.UpdateAsync(provider, cancellationToken);

            var dto = _mapper.Map<LlmProviderDto>(provider);
            return Result<LlmProviderDto>.Ok(dto);
        }
        catch (HttpRequestException ex)
        {
            return Result<LlmProviderDto>.BadGateway(
                $"Failed to connect to provider: {ex.Message}",
                [$"Connection error: {ex.Message}"]);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result<LlmProviderDto>.BadGateway(
                $"Authentication failed: {ex.Message}",
                [$"Unauthorized: {ex.Message}"]);
        }
        catch (TaskCanceledException)
        {
            return Result<LlmProviderDto>.BadGateway(
                "Model discovery request timed out.",
                ["Request timed out"]);
        }
        catch (InvalidOperationException ex)
        {
            return Result<LlmProviderDto>.BadGateway(
                $"Model discovery failed: {ex.Message}",
                [ex.Message]);
        }
    }
}
