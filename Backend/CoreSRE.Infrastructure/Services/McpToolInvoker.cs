using CoreSRE.Application.Interfaces;
using CoreSRE.Application.Tools.DTOs;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.Text.Json;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// MCP Server 工具调用器。通过 MCP 协议调用 MCP Server 的指定工具。
/// 每次调用创建独立连接（per-invocation，R7 决策）。
/// 支持通过 AuthConfigVO 注入认证头（ApiKey / Bearer / OAuth2）。
/// </summary>
public class McpToolInvoker : IToolInvoker
{
    private readonly ICredentialEncryptionService _encryptionService;

    public McpToolInvoker(ICredentialEncryptionService encryptionService)
    {
        _encryptionService = encryptionService;
    }

    /// <inheritdoc/>
    public bool CanHandle(ToolType toolType) => toolType == ToolType.McpServer;

    /// <inheritdoc/>
    public async Task<ToolInvocationResultDto> InvokeAsync(
        ToolRegistration tool,
        string? mcpToolName,
        IDictionary<string, object?> parameters,
        IDictionary<string, string>? queryParameters = null,
        IDictionary<string, string>? headerParameters = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var invokedAt = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(mcpToolName))
        {
            stopwatch.Stop();
            return new ToolInvocationResultDto
            {
                Success = false,
                Error = "mcpToolName is required for McpServer tool invocation.",
                DurationMs = stopwatch.ElapsedMilliseconds,
                ToolRegistrationId = tool.Id,
                InvokedAt = invokedAt
            };
        }

        try
        {
            // Create per-invocation MCP client with auth headers
            var clientTransport = CreateTransport(tool);

            await using var client = await McpClient.CreateAsync(
                clientTransport,
                cancellationToken: cancellationToken);

            // Convert parameters to the format expected by MCP
            var mcpParams = new Dictionary<string, object?>(parameters);

            // Call the specific tool
            var result = await client.CallToolAsync(mcpToolName, mcpParams, cancellationToken: cancellationToken);
            stopwatch.Stop();

            // Parse result content
            JsonElement? data = null;
            if (result.Content is not null && result.Content.Count > 0)
            {
                var textContent = result.Content
                    .OfType<TextContentBlock>()
                    .Select(c => c.Text)
                    .FirstOrDefault();

                if (textContent is not null)
                {
                    try
                    {
                        data = JsonSerializer.Deserialize<JsonElement>(textContent);
                    }
                    catch
                    {
                        data = JsonSerializer.Deserialize<JsonElement>(
                            JsonSerializer.Serialize(new { raw = textContent }));
                    }
                }
            }

            var isError = result.IsError ?? false;
            return new ToolInvocationResultDto
            {
                Success = !isError,
                Data = data,
                Error = isError ? "MCP tool returned an error." : null,
                DurationMs = stopwatch.ElapsedMilliseconds,
                ToolRegistrationId = tool.Id,
                InvokedAt = invokedAt
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ToolInvocationResultDto
            {
                Success = false,
                Error = $"MCP invocation failed: {ex.Message}",
                DurationMs = stopwatch.ElapsedMilliseconds,
                ToolRegistrationId = tool.Id,
                InvokedAt = invokedAt
            };
        }
    }

    private IClientTransport CreateTransport(ToolRegistration tool)
    {
        return tool.ConnectionConfig.TransportType switch
        {
            TransportType.StreamableHttp => CreateHttpTransport(tool, HttpTransportMode.StreamableHttp),
            TransportType.Sse => CreateHttpTransport(tool, HttpTransportMode.Sse),
            TransportType.AutoDetect => CreateHttpTransport(tool, HttpTransportMode.AutoDetect),
            TransportType.Stdio => new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = tool.Name,
                Command = tool.ConnectionConfig.Endpoint
            }),
            _ => throw new ArgumentException($"Unsupported transport type for MCP: {tool.ConnectionConfig.TransportType}")
        };
    }

    private IClientTransport CreateHttpTransport(ToolRegistration tool, HttpTransportMode mode)
    {
        var options = new HttpClientTransportOptions
        {
            Endpoint = new Uri(tool.ConnectionConfig.Endpoint),
            TransportMode = mode,
            AdditionalHeaders = BuildAuthHeaders(tool.AuthConfig)
        };
        return new HttpClientTransport(options);
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
                var accessToken = _encryptionService.Decrypt(authConfig.EncryptedCredential);
                headers["Authorization"] = $"Bearer {accessToken}";
                break;
        }

        return headers.Count > 0 ? headers : null;
    }
}
