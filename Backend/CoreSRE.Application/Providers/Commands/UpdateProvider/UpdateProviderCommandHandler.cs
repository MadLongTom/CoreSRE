using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Providers.DTOs;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Providers.Commands.UpdateProvider;

/// <summary>
/// 更新 LLM Provider 命令处理器
/// </summary>
public class UpdateProviderCommandHandler : IRequestHandler<UpdateProviderCommand, Result<LlmProviderDto>>
{
    private readonly ILlmProviderRepository _repository;
    private readonly IMapper _mapper;

    public UpdateProviderCommandHandler(ILlmProviderRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Result<LlmProviderDto>> Handle(
        UpdateProviderCommand request,
        CancellationToken cancellationToken)
    {
        var provider = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (provider is null)
            return Result<LlmProviderDto>.NotFound($"Provider with ID '{request.Id}' not found.");

        // Check for duplicate name (exclude self)
        if (await _repository.ExistsWithNameAsync(request.Name, excludeId: request.Id, cancellationToken: cancellationToken))
            return Result<LlmProviderDto>.Conflict($"Provider with name '{request.Name}' already exists.");

        // ApiKey: null or empty means keep existing
        var apiKey = string.IsNullOrWhiteSpace(request.ApiKey) ? null : request.ApiKey;

        provider.Update(request.Name, request.BaseUrl, apiKey);
        await _repository.UpdateAsync(provider, cancellationToken);

        var dto = _mapper.Map<LlmProviderDto>(provider);
        return Result<LlmProviderDto>.Ok(dto);
    }
}
