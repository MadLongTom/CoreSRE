using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Providers.DTOs;
using MediatR;

namespace CoreSRE.Application.Providers.Queries.GetProviderModels;

/// <summary>
/// 查询 Provider 的已发现模型列表
/// </summary>
public record GetProviderModelsQuery(Guid Id) : IRequest<Result<List<DiscoveredModelDto>>>;
