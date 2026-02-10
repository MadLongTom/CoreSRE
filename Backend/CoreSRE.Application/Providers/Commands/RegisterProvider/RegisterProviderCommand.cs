using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Providers.DTOs;
using MediatR;

namespace CoreSRE.Application.Providers.Commands.RegisterProvider;

/// <summary>
/// 注册 LLM Provider 命令
/// </summary>
public record RegisterProviderCommand : IRequest<Result<LlmProviderDto>>
{
    public string Name { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
}
