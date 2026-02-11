using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Tools.DTOs;
using MediatR;

namespace CoreSRE.Application.Tools.Commands.RegisterTool;

/// <summary>
/// 注册工具命令
/// </summary>
public record RegisterToolCommand : IRequest<Result<ToolRegistrationDto>>
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string ToolType { get; init; } = string.Empty;
    public RegisterToolConnectionConfig ConnectionConfig { get; init; } = new();
    public RegisterToolAuthConfig AuthConfig { get; init; } = new();

    /// <summary>输入参数 JSON Schema（可选，REST API 可手动定义）</summary>
    public string? InputSchema { get; init; }
}

/// <summary>
/// 注册工具连接配置
/// </summary>
public record RegisterToolConnectionConfig
{
    public string Endpoint { get; init; } = string.Empty;
    public string TransportType { get; init; } = string.Empty;
    /// <summary>HTTP 请求方法（仅 RestApi 工具，默认 POST）</summary>
    public string HttpMethod { get; init; } = "POST";
}

/// <summary>
/// 注册工具认证配置
/// </summary>
public record RegisterToolAuthConfig
{
    public string AuthType { get; init; } = "None";
    public string? Credential { get; init; }
    public string? ApiKeyHeaderName { get; init; }
    public string? TokenEndpoint { get; init; }
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
}
