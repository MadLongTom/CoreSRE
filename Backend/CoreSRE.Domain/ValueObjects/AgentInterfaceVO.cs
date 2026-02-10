namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// Agent 支持的通信接口类型，嵌套在 AgentCardVO 中
/// </summary>
public sealed record AgentInterfaceVO
{
    /// <summary>协议类型（如 "HTTP+SSE", "WebSocket"）</summary>
    public string Protocol { get; init; } = string.Empty;

    /// <summary>URL 路径</summary>
    public string? Path { get; init; }
}
