using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.ValueObjects;
using MediatR;

namespace CoreSRE.Application.DataSources.Commands.QueryDataSource;

/// <summary>统一数据源查询命令</summary>
public record QueryDataSourceCommand : IRequest<Result<DataSourceResultVO>>
{
    public Guid DataSourceId { get; init; }
    public DataSourceQueryVO Query { get; init; } = new();
}
