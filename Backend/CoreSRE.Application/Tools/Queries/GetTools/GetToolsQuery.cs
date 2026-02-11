using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Tools.DTOs;
using CoreSRE.Domain.Enums;
using MediatR;

namespace CoreSRE.Application.Tools.Queries.GetTools;

/// <summary>
/// 查询工具列表（支持分页、类型/状态过滤、关键词搜索）
/// </summary>
public record GetToolsQuery(
    ToolType? ToolType = null,
    ToolStatus? Status = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20) : IRequest<Result<PagedResult<ToolRegistrationDto>>>;
