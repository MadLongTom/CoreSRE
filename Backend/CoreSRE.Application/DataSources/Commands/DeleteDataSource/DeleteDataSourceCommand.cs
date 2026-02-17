using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.DataSources.Commands.DeleteDataSource;

/// <summary>删除数据源命令</summary>
public record DeleteDataSourceCommand(Guid Id) : IRequest<Result<bool>>;
