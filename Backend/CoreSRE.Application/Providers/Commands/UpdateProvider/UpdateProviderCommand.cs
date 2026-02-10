using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Providers.DTOs;
using MediatR;

namespace CoreSRE.Application.Providers.Commands.UpdateProvider;

/// <summary>
/// 更新 LLM Provider 命令
/// </summary>
public record UpdateProviderCommand : IRequest<Result<LlmProviderDto>>
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>API Key — null 或空字符串表示保留原值</summary>
    public string? ApiKey { get; init; }
}
