using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Tools.DTOs;
using MediatR;

namespace CoreSRE.Application.Tools.Queries.GetAvailableFunctions;

/// <summary>
/// 查询所有可绑定工具函数（扁平化 REST API + MCP 子工具）
/// </summary>
public record GetAvailableFunctionsQuery(
    string? Search = null,
    string? Status = null) : IRequest<Result<IEnumerable<BindableToolDto>>>;
