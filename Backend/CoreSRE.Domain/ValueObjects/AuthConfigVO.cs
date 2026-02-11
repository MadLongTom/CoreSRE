using CoreSRE.Domain.Enums;

namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 工具的认证配置。存储为 PostgreSQL JSONB 列。凭据字段加密存储。
/// </summary>
public sealed record AuthConfigVO
{
    /// <summary>认证类型</summary>
    public AuthType AuthType { get; init; }

    /// <summary>加密后的凭据 (Base64)，AuthType=None 时为空</summary>
    public string? EncryptedCredential { get; init; }

    /// <summary>ApiKey 自定义头名（默认 "X-Api-Key"）</summary>
    public string? ApiKeyHeaderName { get; init; }

    /// <summary>OAuth2 Token 端点 URL（仅 OAuth2）</summary>
    public string? TokenEndpoint { get; init; }

    /// <summary>OAuth2 Client ID（仅 OAuth2）</summary>
    public string? ClientId { get; init; }

    /// <summary>OAuth2 Client Secret（加密存储，仅 OAuth2）</summary>
    public string? EncryptedClientSecret { get; init; }
}
