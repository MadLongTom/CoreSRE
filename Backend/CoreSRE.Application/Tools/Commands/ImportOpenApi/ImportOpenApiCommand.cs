using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Tools.Commands.RegisterTool;
using CoreSRE.Application.Tools.DTOs;
using MediatR;

namespace CoreSRE.Application.Tools.Commands.ImportOpenApi;

/// <summary>
/// OpenAPI 文档导入命令
/// </summary>
public record ImportOpenApiCommand : IRequest<Result<OpenApiImportResultDto>>
{
    /// <summary>OpenAPI 文档流</summary>
    public Stream Document { get; init; } = Stream.Null;

    /// <summary>可选的 Base URL 覆盖</summary>
    public string? BaseUrl { get; init; }

    /// <summary>可选的认证配置（应用到所有导入的工具）</summary>
    public RegisterToolAuthConfig? AuthConfig { get; init; }

    /// <summary>导入来源标识（文件名或 URL）</summary>
    public string? ImportSource { get; init; }
}
