using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Providers.DTOs;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Providers.Commands.RegisterProvider;

/// <summary>
/// 注册 LLM Provider 命令处理器
/// </summary>
public class RegisterProviderCommandHandler : IRequestHandler<RegisterProviderCommand, Result<LlmProviderDto>>
{
    private readonly ILlmProviderRepository _repository;
    private readonly IMapper _mapper;

    public RegisterProviderCommandHandler(ILlmProviderRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Result<LlmProviderDto>> Handle(
        RegisterProviderCommand request,
        CancellationToken cancellationToken)
    {
        // Check for duplicate name
        if (await _repository.ExistsWithNameAsync(request.Name, cancellationToken: cancellationToken))
            return Result<LlmProviderDto>.Conflict($"Provider with name '{request.Name}' already exists.");

        var provider = LlmProvider.Create(request.Name, request.BaseUrl, request.ApiKey);
        await _repository.AddAsync(provider, cancellationToken);

        var dto = _mapper.Map<LlmProviderDto>(provider);
        return Result<LlmProviderDto>.Ok(dto);
    }
}
