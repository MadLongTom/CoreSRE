using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Providers.DTOs;
using MediatR;

namespace CoreSRE.Application.Providers.Queries.GetProviderById;

/// <summary>
/// 按 ID 查询 Provider 详情
/// </summary>
public record GetProviderByIdQuery(Guid Id) : IRequest<Result<LlmProviderDto>>;
