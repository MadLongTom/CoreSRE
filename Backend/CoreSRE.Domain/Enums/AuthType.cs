namespace CoreSRE.Domain.Enums;

/// <summary>
/// 工具认证类型枚举
/// </summary>
public enum AuthType
{
    /// <summary>无认证</summary>
    None,

    /// <summary>API Key 认证，注入 X-Api-Key 头</summary>
    ApiKey,

    /// <summary>Bearer Token 认证，注入 Authorization: Bearer 头</summary>
    Bearer,

    /// <summary>OAuth2 Client Credentials Grant</summary>
    OAuth2
}
