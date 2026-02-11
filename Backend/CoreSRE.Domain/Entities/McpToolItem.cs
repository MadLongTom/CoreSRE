using System.Text.Json;
using CoreSRE.Domain.ValueObjects;

namespace CoreSRE.Domain.Entities;

/// <summary>
/// MCP Server 工具源下发现的单个 Tool。通过 MCP tools/list 自动发现，关联到父 ToolRegistration。
/// </summary>
public class McpToolItem : BaseEntity
{
    /// <summary>关联 ToolRegistration 的外键</summary>
    public Guid ToolRegistrationId { get; private set; }

    /// <summary>MCP Tool 名称，max 200 chars，在同一父工具源内唯一</summary>
    public string ToolName { get; private set; } = string.Empty;

    /// <summary>Tool 描述</summary>
    public string? Description { get; private set; }

    /// <summary>输入参数 JSON Schema</summary>
    public JsonElement? InputSchema { get; private set; }

    /// <summary>输出 JSON Schema</summary>
    public JsonElement? OutputSchema { get; private set; }

    /// <summary>Tool 注解 (JSONB)</summary>
    public ToolAnnotationsVO? Annotations { get; private set; }

    /// <summary>导航属性：父 ToolRegistration</summary>
    public ToolRegistration? ToolRegistration { get; private set; }

    // EF Core requires parameterless constructor
    private McpToolItem() { }

    /// <summary>创建 MCP 子工具项</summary>
    public static McpToolItem Create(
        Guid toolRegistrationId,
        string toolName,
        string? description = null,
        JsonElement? inputSchema = null,
        JsonElement? outputSchema = null,
        ToolAnnotationsVO? annotations = null)
    {
        if (toolRegistrationId == Guid.Empty)
            throw new ArgumentException("ToolRegistrationId must not be empty.", nameof(toolRegistrationId));

        ArgumentException.ThrowIfNullOrWhiteSpace(toolName, nameof(toolName));

        if (toolName.Length > 200)
            throw new ArgumentException("ToolName must not exceed 200 characters.", nameof(toolName));

        return new McpToolItem
        {
            ToolRegistrationId = toolRegistrationId,
            ToolName = toolName,
            Description = description,
            InputSchema = inputSchema,
            OutputSchema = outputSchema,
            Annotations = annotations
        };
    }
}
