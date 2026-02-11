namespace CoreSRE.Domain.Enums;

/// <summary>
/// 工具类型枚举
/// </summary>
public enum ToolType
{
    /// <summary>外部 REST API 工具，通过 HTTP 调用</summary>
    RestApi,

    /// <summary>MCP Server 工具源，通过 MCP 协议调用</summary>
    McpServer
}
