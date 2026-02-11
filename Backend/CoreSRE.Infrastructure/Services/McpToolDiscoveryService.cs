using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// MCP 工具发现服务实现。通过 MCP 协议握手并发现工具源下的所有 Tool。
/// 支持通过 AuthConfigVO 注入认证头（ApiKey / Bearer / OAuth2）。
/// </summary>
public class McpToolDiscoveryService : IMcpToolDiscoveryService
{
    private readonly ICredentialEncryptionService _encryptionService;

    public McpToolDiscoveryService(ICredentialEncryptionService encryptionService)
    {
        _encryptionService = encryptionService;
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<McpToolItem>>> DiscoverToolsAsync(
        ToolRegistration registration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Create MCP client based on transport type
            var clientTransport = registration.ConnectionConfig.TransportType switch
            {
                TransportType.StreamableHttp => CreateHttpTransport(registration, HttpTransportMode.StreamableHttp),
                TransportType.Sse => CreateHttpTransport(registration, HttpTransportMode.Sse),
                TransportType.AutoDetect => CreateHttpTransport(registration, HttpTransportMode.AutoDetect),
                TransportType.Stdio => CreateStdioTransport(registration),
                _ => throw new ArgumentException($"Unsupported transport type for MCP: {registration.ConnectionConfig.TransportType}")
            };

            await using var client = await McpClient.CreateAsync(clientTransport, cancellationToken: cancellationToken);

            // Discover tools via tools/list
            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);

            var mcpToolItems = new List<McpToolItem>();
            foreach (var tool in tools)
            {
                // Get the underlying protocol tool for schema and annotations
                var protocolTool = tool.ProtocolTool;

                JsonElement? inputSchema = null;
                if (protocolTool.InputSchema.ValueKind != JsonValueKind.Undefined)
                {
                    var serialized = JsonSerializer.Serialize(protocolTool.InputSchema);
                    inputSchema = JsonDocument.Parse(serialized).RootElement.Clone();
                }

                ToolAnnotationsVO? annotations = null;
                if (protocolTool.Annotations is not null)
                {
                    annotations = new ToolAnnotationsVO
                    {
                        ReadOnly = protocolTool.Annotations.ReadOnlyHint ?? false,
                        Destructive = protocolTool.Annotations.DestructiveHint ?? false,
                        Idempotent = protocolTool.Annotations.IdempotentHint ?? false,
                        OpenWorldHint = protocolTool.Annotations.OpenWorldHint ?? false
                    };
                }

                var mcpToolItem = McpToolItem.Create(
                    registration.Id,
                    tool.Name,
                    tool.Description,
                    inputSchema,
                    outputSchema: null,
                    annotations);

                mcpToolItems.Add(mcpToolItem);
            }

            return Result<IReadOnlyList<McpToolItem>>.Ok(mcpToolItems);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<McpToolItem>>.Fail(
                $"MCP discovery failed for '{registration.Name}': {ex.Message}");
        }
    }

    private IClientTransport CreateHttpTransport(ToolRegistration registration, HttpTransportMode mode)
    {
        var options = new HttpClientTransportOptions
        {
            Endpoint = new Uri(registration.ConnectionConfig.Endpoint),
            TransportMode = mode,
            AdditionalHeaders = BuildAuthHeaders(registration.AuthConfig)
        };
        return new HttpClientTransport(options);
    }

    private static IClientTransport CreateStdioTransport(ToolRegistration registration)
    {
        // For Stdio transport, the endpoint contains the command to execute
        return new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = registration.Name,
            Command = registration.ConnectionConfig.Endpoint
        });
    }

    /// <summary>
    /// 根据 AuthConfigVO 构建认证头字典，用于 HttpClientTransportOptions.AdditionalHeaders。
    /// </summary>
    private Dictionary<string, string>? BuildAuthHeaders(AuthConfigVO authConfig)
    {
        if (authConfig.AuthType == AuthType.None || string.IsNullOrEmpty(authConfig.EncryptedCredential))
            return null;

        var headers = new Dictionary<string, string>();

        switch (authConfig.AuthType)
        {
            case AuthType.ApiKey:
                var apiKey = _encryptionService.Decrypt(authConfig.EncryptedCredential);
                var headerName = authConfig.ApiKeyHeaderName ?? "X-Api-Key";
                headers[headerName] = apiKey;
                break;

            case AuthType.Bearer:
                var token = _encryptionService.Decrypt(authConfig.EncryptedCredential);
                headers["Authorization"] = $"Bearer {token}";
                break;

            case AuthType.OAuth2:
                // Use stored credential as pre-obtained access token
                var accessToken = _encryptionService.Decrypt(authConfig.EncryptedCredential);
                headers["Authorization"] = $"Bearer {accessToken}";
                break;
        }

        return headers.Count > 0 ? headers : null;
    }
}
