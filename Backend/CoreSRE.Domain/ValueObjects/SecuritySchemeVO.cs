namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// Agent 的安全认证方案，嵌套在 AgentCardVO 中
/// </summary>
public sealed record SecuritySchemeVO
{
    /// <summary>方案类型（如 "apiKey", "oauth2", "bearer"）</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>配置参数（JSON string）</summary>
    public string? Parameters { get; init; }
}
