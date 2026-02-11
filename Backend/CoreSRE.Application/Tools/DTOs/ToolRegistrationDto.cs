using System.Text.Json;

namespace CoreSRE.Application.Tools.DTOs;

/// <summary>
/// 工具注册完整详情 DTO
/// </summary>
public class ToolRegistrationDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ToolType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public ConnectionConfigDto ConnectionConfig { get; set; } = new();
    public AuthConfigDto AuthConfig { get; set; } = new();
    public ToolSchemaDto? ToolSchema { get; set; }
    public string? DiscoveryError { get; set; }
    public string? ImportSource { get; set; }
    public int McpToolCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// 连接配置 DTO
/// </summary>
public class ConnectionConfigDto
{
    public string Endpoint { get; set; } = string.Empty;
    public string TransportType { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = "POST";
}

/// <summary>
/// 认证配置 DTO（凭据以 hasCredential 标识替代实际值）
/// </summary>
public class AuthConfigDto
{
    public string AuthType { get; set; } = string.Empty;
    public bool HasCredential { get; set; }
    public string? MaskedCredential { get; set; }
    public string? ApiKeyHeaderName { get; set; }
    public string? TokenEndpoint { get; set; }
    public string? ClientId { get; set; }
    public bool HasClientSecret { get; set; }
}

/// <summary>
/// 工具 Schema DTO
/// </summary>
public class ToolSchemaDto
{
    public JsonElement? InputSchema { get; set; }
    public JsonElement? OutputSchema { get; set; }
    public ToolAnnotationsDto? Annotations { get; set; }
}

/// <summary>
/// 工具注解 DTO
/// </summary>
public class ToolAnnotationsDto
{
    public bool ReadOnly { get; set; }
    public bool Destructive { get; set; }
    public bool Idempotent { get; set; }
    public bool OpenWorldHint { get; set; }
}
