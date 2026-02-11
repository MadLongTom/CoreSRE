namespace CoreSRE.Application.Tools.DTOs;

/// <summary>
/// 可绑定工具 DTO — 用于工具选择器 UI，扁平化 REST API 工具和 MCP 子工具为统一列表。
/// </summary>
public class BindableToolDto
{
    /// <summary>ToolRegistration.Id (RestApi) 或 McpToolItem.Id (McpTool)</summary>
    public Guid Id { get; set; }

    /// <summary>ToolRegistration.Name 或 McpToolItem.ToolName</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>工具描述</summary>
    public string? Description { get; set; }

    /// <summary>"RestApi" 或 "McpTool"（注意：不是 "McpServer"）</summary>
    public string ToolType { get; set; } = string.Empty;

    /// <summary>MCP 子工具的父服务器名称；REST API 工具为 null</summary>
    public string? ParentName { get; set; }

    /// <summary>工具状态</summary>
    public string Status { get; set; } = string.Empty;
}
