using CoreSRE.Application.Common.Models;
using CoreSRE.Application.DataSources.DTOs;
using CoreSRE.Domain.Enums;
using MediatR;

namespace CoreSRE.Application.DataSources.Queries.GetDataSources;

/// <summary>查询数据源列表（支持分页、分类/状态过滤、关键词搜索）</summary>
public record GetDataSourcesQuery(
    DataSourceCategory? Category = null,
    DataSourceStatus? Status = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20) : IRequest<Result<PagedResult<DataSourceRegistrationDto>>>;
