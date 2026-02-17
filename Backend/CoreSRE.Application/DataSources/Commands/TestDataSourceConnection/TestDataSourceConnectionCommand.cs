using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.ValueObjects;
using MediatR;

namespace CoreSRE.Application.DataSources.Commands.TestDataSourceConnection;

/// <summary>测试数据源连接命令</summary>
public record TestDataSourceConnectionCommand(Guid DataSourceId)
    : IRequest<Result<DataSourceHealthVO>>;
