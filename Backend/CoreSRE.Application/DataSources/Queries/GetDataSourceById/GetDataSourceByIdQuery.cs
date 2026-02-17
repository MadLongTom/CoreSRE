using CoreSRE.Application.Common.Models;
using CoreSRE.Application.DataSources.DTOs;
using MediatR;

namespace CoreSRE.Application.DataSources.Queries.GetDataSourceById;

/// <summary>按 ID 查询数据源详情</summary>
public record GetDataSourceByIdQuery(Guid Id) : IRequest<Result<DataSourceRegistrationDto>>;
