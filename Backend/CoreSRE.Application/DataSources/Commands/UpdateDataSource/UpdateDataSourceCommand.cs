using CoreSRE.Application.Common.Models;
using CoreSRE.Application.DataSources.Commands.RegisterDataSource;
using CoreSRE.Application.DataSources.DTOs;
using MediatR;

namespace CoreSRE.Application.DataSources.Commands.UpdateDataSource;

/// <summary>更新数据源命令</summary>
public record UpdateDataSourceCommand : IRequest<Result<DataSourceRegistrationDto>>
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public RegisterDataSourceConnectionConfig ConnectionConfig { get; init; } = new();
    public RegisterDataSourceQueryConfig? DefaultQueryConfig { get; init; }
}
