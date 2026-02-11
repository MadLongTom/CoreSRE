using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Tools.DTOs;
using MediatR;

namespace CoreSRE.Application.Tools.Queries.GetToolById;

/// <summary>
/// 按 ID 查询工具详情
/// </summary>
public record GetToolByIdQuery(Guid Id) : IRequest<Result<ToolRegistrationDto>>;
