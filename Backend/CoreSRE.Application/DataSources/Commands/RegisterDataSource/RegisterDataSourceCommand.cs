using CoreSRE.Application.Common.Models;
using CoreSRE.Application.DataSources.DTOs;
using MediatR;

namespace CoreSRE.Application.DataSources.Commands.RegisterDataSource;

/// <summary>注册数据源命令</summary>
public record RegisterDataSourceCommand : IRequest<Result<DataSourceRegistrationDto>>
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Product { get; init; } = string.Empty;
    public RegisterDataSourceConnectionConfig ConnectionConfig { get; init; } = new();
    public RegisterDataSourceQueryConfig? DefaultQueryConfig { get; init; }
}

/// <summary>注册数据源连接配置</summary>
public record RegisterDataSourceConnectionConfig
{
    public string BaseUrl { get; init; } = string.Empty;
    public string AuthType { get; init; } = "None";
    public string? Credential { get; init; }
    public string? AuthHeaderName { get; init; }
    public bool TlsSkipVerify { get; init; }
    public int TimeoutSeconds { get; init; } = 30;
    public Dictionary<string, string>? CustomHeaders { get; init; }
    public string? Namespace { get; init; }
    public string? Organization { get; init; }
    public string? KubeConfig { get; init; }
}

/// <summary>注册数据源默认查询配置</summary>
public record RegisterDataSourceQueryConfig
{
    public Dictionary<string, string>? DefaultLabels { get; init; }
    public string? DefaultNamespace { get; init; }
    public int? MaxResults { get; init; }
    public string? DefaultStep { get; init; }
    public string? DefaultIndex { get; init; }
}
