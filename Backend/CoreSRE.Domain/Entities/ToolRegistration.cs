using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;

namespace CoreSRE.Domain.Entities;

/// <summary>
/// 工具注册聚合根。代表一个已注册的工具或工具源，通过 ToolType 鉴别器区分 RestApi 和 McpServer 两种类型。
/// </summary>
public class ToolRegistration : BaseEntity
{
    /// <summary>工具名称，全局唯一，最长 200 字符</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>工具描述（可选）</summary>
    public string? Description { get; private set; }

    /// <summary>工具类型，注册后不可变更</summary>
    public ToolType ToolType { get; private set; }

    /// <summary>工具状态</summary>
    public ToolStatus Status { get; private set; }

    /// <summary>连接配置 (JSONB)</summary>
    public ConnectionConfigVO ConnectionConfig { get; private set; } = new();

    /// <summary>认证配置 (JSONB)</summary>
    public AuthConfigVO AuthConfig { get; private set; } = new();

    /// <summary>工具 Schema (JSONB)，OpenAPI 导入时填充，手动注册可空</summary>
    public ToolSchemaVO? ToolSchema { get; private set; }

    /// <summary>MCP 握手/发现失败时的错误信息</summary>
    public string? DiscoveryError { get; private set; }

    /// <summary>OpenAPI 导入来源标识（文件名或 URL）</summary>
    public string? ImportSource { get; private set; }

    /// <summary>关联的 MCP Tool 子项（导航属性）</summary>
    public IReadOnlyCollection<McpToolItem> McpToolItems => _mcpToolItems.AsReadOnly();
    private readonly List<McpToolItem> _mcpToolItems = [];

    // EF Core requires parameterless constructor
    private ToolRegistration() { }

    /// <summary>创建 RestApi 类型工具，Status = Active</summary>
    public static ToolRegistration CreateRestApi(
        string name,
        string? description,
        string endpoint,
        AuthConfigVO authConfig,
        string httpMethod = "POST")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        ArgumentNullException.ThrowIfNull(authConfig, nameof(authConfig));
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint, nameof(endpoint));

        if (name.Length > 200)
            throw new ArgumentException("Name must not exceed 200 characters.", nameof(name));

        if (endpoint.Length > 2048)
            throw new ArgumentException("Endpoint must not exceed 2048 characters.", nameof(endpoint));

        return new ToolRegistration
        {
            Name = name,
            Description = description,
            ToolType = ToolType.RestApi,
            Status = ToolStatus.Active,
            ConnectionConfig = new ConnectionConfigVO
            {
                Endpoint = endpoint,
                TransportType = TransportType.Rest,
                HttpMethod = httpMethod.ToUpperInvariant()
            },
            AuthConfig = authConfig
        };
    }

    /// <summary>创建 McpServer 类型工具，Status = Inactive（握手成功后更新）</summary>
    public static ToolRegistration CreateMcpServer(
        string name,
        string? description,
        string endpoint,
        TransportType transportType,
        AuthConfigVO? authConfig = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint, nameof(endpoint));

        if (name.Length > 200)
            throw new ArgumentException("Name must not exceed 200 characters.", nameof(name));

        if (endpoint.Length > 2048)
            throw new ArgumentException("Endpoint must not exceed 2048 characters.", nameof(endpoint));

        if (transportType is not (TransportType.StreamableHttp or TransportType.Stdio or TransportType.Sse or TransportType.AutoDetect))
            throw new ArgumentException("McpServer transport type must be StreamableHttp, Sse, AutoDetect, or Stdio.", nameof(transportType));

        return new ToolRegistration
        {
            Name = name,
            Description = description,
            ToolType = ToolType.McpServer,
            Status = ToolStatus.Inactive,
            ConnectionConfig = new ConnectionConfigVO
            {
                Endpoint = endpoint,
                TransportType = transportType
            },
            AuthConfig = authConfig ?? new AuthConfigVO { AuthType = AuthType.None }
        };
    }

    /// <summary>批量创建 OpenAPI 导入的 RestApi 工具</summary>
    public static ToolRegistration CreateFromOpenApi(
        string name,
        string? description,
        string endpoint,
        AuthConfigVO? authConfig,
        ToolSchemaVO toolSchema,
        string? importSource = null,
        string httpMethod = "POST")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        ArgumentNullException.ThrowIfNull(toolSchema, nameof(toolSchema));
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint, nameof(endpoint));

        if (name.Length > 200)
            throw new ArgumentException("Name must not exceed 200 characters.", nameof(name));

        if (endpoint.Length > 2048)
            throw new ArgumentException("Endpoint must not exceed 2048 characters.", nameof(endpoint));

        return new ToolRegistration
        {
            Name = name,
            Description = description,
            ToolType = ToolType.RestApi,
            Status = ToolStatus.Active,
            ConnectionConfig = new ConnectionConfigVO
            {
                Endpoint = endpoint,
                TransportType = TransportType.Rest,
                HttpMethod = httpMethod.ToUpperInvariant()
            },
            AuthConfig = authConfig ?? new AuthConfigVO { AuthType = AuthType.None },
            ToolSchema = toolSchema,
            ImportSource = importSource
        };
    }

    /// <summary>
    /// 更新工具配置。toolType 不可变更，按当前类型校验更新数据的合法性。
    /// </summary>
    public void Update(
        string name,
        string? description,
        ConnectionConfigVO connectionConfig,
        AuthConfigVO? authConfig)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        ArgumentNullException.ThrowIfNull(connectionConfig, nameof(connectionConfig));
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionConfig.Endpoint, nameof(connectionConfig.Endpoint));

        if (name.Length > 200)
            throw new ArgumentException("Name must not exceed 200 characters.", nameof(name));

        if (connectionConfig.Endpoint.Length > 2048)
            throw new ArgumentException("Endpoint must not exceed 2048 characters.", nameof(connectionConfig.Endpoint));

        // Type-specific invariant validation
        switch (ToolType)
        {
            case ToolType.RestApi:
                if (connectionConfig.TransportType != TransportType.Rest)
                    throw new ArgumentException("RestApi tool must use Rest transport type.", nameof(connectionConfig));
                break;

            case ToolType.McpServer:
                if (connectionConfig.TransportType is not (TransportType.StreamableHttp or TransportType.Stdio or TransportType.Sse or TransportType.AutoDetect))
                    throw new ArgumentException("McpServer tool must use StreamableHttp, Sse, AutoDetect, or Stdio transport type.", nameof(connectionConfig));
                break;
        }

        Name = name;
        Description = description;
        ConnectionConfig = connectionConfig;
        AuthConfig = authConfig ?? new AuthConfigVO { AuthType = AuthType.None };
    }

    /// <summary>将状态设为 Active（MCP 握手成功后调用）</summary>
    public void MarkActive()
    {
        Status = ToolStatus.Active;
        DiscoveryError = null;
    }

    /// <summary>将状态设为 Inactive（MCP 握手失败时调用）</summary>
    public void MarkInactive(string? error = null)
    {
        Status = ToolStatus.Inactive;
        DiscoveryError = error;
    }

    /// <summary>设置工具 Schema</summary>
    public void SetToolSchema(ToolSchemaVO toolSchema)
    {
        ArgumentNullException.ThrowIfNull(toolSchema, nameof(toolSchema));
        ToolSchema = toolSchema;
    }
}
