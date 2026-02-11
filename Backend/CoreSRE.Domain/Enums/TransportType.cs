namespace CoreSRE.Domain.Enums;

/// <summary>
/// 工具传输类型枚举
/// </summary>
public enum TransportType
{
    /// <summary>标准 REST HTTP 调用</summary>
    Rest,

    /// <summary>MCP Streamable HTTP 传输</summary>
    StreamableHttp,

    /// <summary>MCP 标准输入/输出传输（本地进程）</summary>
    Stdio,

    /// <summary>MCP HTTP+SSE 传输（旧版）</summary>
    Sse,

    /// <summary>MCP 自动检测传输（先尝试 StreamableHttp，失败回退 SSE）</summary>
    AutoDetect
}
