using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Providers.DTOs;
using MediatR;

namespace CoreSRE.Application.Providers.Commands.DiscoverModels;

/// <summary>
/// 触发模型发现命令
/// </summary>
public record DiscoverModelsCommand(Guid Id) : IRequest<Result<LlmProviderDto>>;
