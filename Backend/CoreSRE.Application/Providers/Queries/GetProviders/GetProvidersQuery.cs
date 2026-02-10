using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Providers.DTOs;
using MediatR;

namespace CoreSRE.Application.Providers.Queries.GetProviders;

/// <summary>
/// 查询所有 Provider 列表
/// </summary>
public record GetProvidersQuery : IRequest<Result<List<LlmProviderSummaryDto>>>;
