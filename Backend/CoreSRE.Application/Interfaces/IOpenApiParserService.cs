using System.Text.Json;
using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.ValueObjects;

namespace CoreSRE.Application.Interfaces;

/// <summary>
/// OpenAPI 文档解析后的单个工具定义。
/// </summary>
public class ParsedToolDefinition
{
    /// <summary>工具名称（operationId 或 {method}_{path}）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>工具描述（operation summary）</summary>
    public string? Description { get; set; }

    /// <summary>工具端点路径</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>HTTP 请求方法（GET/POST/PUT/DELETE/PATCH）</summary>
    public string HttpMethod { get; set; } = "POST";

    /// <summary>工具 Schema</summary>
    public ToolSchemaVO? ToolSchema { get; set; }
}

/// <summary>
/// OpenAPI 文档解析服务接口。解析 OpenAPI JSON/YAML 文档并提取操作信息。
/// Application 层定义接口，Infrastructure 层提供实现。
/// </summary>
public interface IOpenApiParserService
{
    /// <summary>
    /// 解析 OpenAPI 文档流并提取所有操作为工具定义。
    /// </summary>
    /// <param name="document">OpenAPI 文档流（JSON 或 YAML）</param>
    /// <param name="baseUrl">可选的 base URL 覆盖</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>解析后的工具定义列表</returns>
    Task<Result<IReadOnlyList<ParsedToolDefinition>>> ParseAsync(
        Stream document,
        string? baseUrl = null,
        CancellationToken cancellationToken = default);
}
