using System.Text.Json;

namespace CoreSRE.Application.Tools.DTOs;

/// <summary>
/// MCP 子工具项 DTO
/// </summary>
public class McpToolItemDto
{
    public Guid Id { get; set; }
    public Guid ToolRegistrationId { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public JsonElement? InputSchema { get; set; }
    public JsonElement? OutputSchema { get; set; }
    public ToolAnnotationsDto? Annotations { get; set; }
    public DateTime CreatedAt { get; set; }
}
