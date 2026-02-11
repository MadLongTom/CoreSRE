using CoreSRE.Domain.Enums;

namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 工具的连接配置。存储为 PostgreSQL JSONB 列。
/// </summary>
public sealed record ConnectionConfigVO
{
    /// <summary>工具端点 URL，max 2048 chars</summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>传输类型</summary>
    public TransportType TransportType { get; init; }

    /// <summary>HTTP 请求方法（仅 REST 工具使用），默认 POST</summary>
    public string HttpMethod { get; init; } = "POST";
}
