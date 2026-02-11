using CoreSRE.Application.Interfaces;
using CoreSRE.Application.Tools.DTOs;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// REST API 工具调用器。通过 HTTP 调用外部 REST API，自动注入认证头。
/// </summary>
public class RestApiToolInvoker : IToolInvoker
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICredentialEncryptionService _encryptionService;

    public RestApiToolInvoker(
        IHttpClientFactory httpClientFactory,
        ICredentialEncryptionService encryptionService)
    {
        _httpClientFactory = httpClientFactory;
        _encryptionService = encryptionService;
    }

    /// <inheritdoc/>
    public bool CanHandle(ToolType toolType) => toolType == ToolType.RestApi;

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

        try
        {
            var client = _httpClientFactory.CreateClient("ToolInvoker");

            // Resolve HTTP method from connection config
            var httpMethod = tool.ConnectionConfig.HttpMethod?.ToUpperInvariant() switch
            {
                "GET" => HttpMethod.Get,
                "PUT" => HttpMethod.Put,
                "DELETE" => HttpMethod.Delete,
                "PATCH" => HttpMethod.Patch,
                "HEAD" => HttpMethod.Head,
                "OPTIONS" => HttpMethod.Options,
                _ => HttpMethod.Post // default
            };

            // Build request URI with query parameters
            var endpointUri = tool.ConnectionConfig.Endpoint;
            var allQueryParams = new Dictionary<string, string>();

            // Explicit query parameters always added
            if (queryParameters is { Count: > 0 })
            {
                foreach (var qp in queryParameters)
                    allQueryParams[qp.Key] = qp.Value;
            }

            // For GET/HEAD/DELETE, also treat body parameters as query if no explicit query provided
            if (httpMethod == HttpMethod.Get || httpMethod == HttpMethod.Head || httpMethod == HttpMethod.Delete)
            {
                if (queryParameters is null or { Count: 0 } && parameters.Count > 0)
                {
                    foreach (var p in parameters.Where(p => p.Value is not null))
                        allQueryParams[p.Key] = p.Value?.ToString() ?? "";
                }
            }

            if (allQueryParams.Count > 0)
            {
                var queryString = string.Join("&", allQueryParams
                    .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
                var uriBuilder = new UriBuilder(endpointUri);
                uriBuilder.Query = string.IsNullOrEmpty(uriBuilder.Query)
                    ? queryString
                    : $"{uriBuilder.Query.TrimStart('?')}&{queryString}";
                endpointUri = uriBuilder.Uri.ToString();
            }

            var request = new HttpRequestMessage(httpMethod, endpointUri);

            // Inject auth header
            InjectAuth(request, tool.AuthConfig);

            // Inject custom header parameters
            if (headerParameters is { Count: > 0 })
            {
                foreach (var hp in headerParameters)
                    request.Headers.TryAddWithoutValidation(hp.Key, hp.Value);
            }

            // Set body for methods that support it
            if (httpMethod != HttpMethod.Get && httpMethod != HttpMethod.Head && httpMethod != HttpMethod.Delete
                && parameters.Count > 0)
            {
                var jsonContent = JsonSerializer.Serialize(parameters);
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            }

            // Send request
            var response = await client.SendAsync(request, cancellationToken);
            stopwatch.Stop();

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                JsonElement? data = null;
                try
                {
                    data = JsonSerializer.Deserialize<JsonElement>(responseBody);
                }
                catch
                {
                    // If response is not valid JSON, wrap in a string element
                    data = JsonSerializer.Deserialize<JsonElement>(
                        JsonSerializer.Serialize(new { raw = responseBody }));
                }

                return new ToolInvocationResultDto
                {
                    Success = true,
                    Data = data,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    ToolRegistrationId = tool.Id,
                    InvokedAt = invokedAt
                };
            }
            else
            {
                return new ToolInvocationResultDto
                {
                    Success = false,
                    Error = $"HTTP {(int)response.StatusCode}: {responseBody}",
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    ToolRegistrationId = tool.Id,
                    InvokedAt = invokedAt
                };
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ToolInvocationResultDto
            {
                Success = false,
                Error = $"Invocation failed: {ex.Message}",
                DurationMs = stopwatch.ElapsedMilliseconds,
                ToolRegistrationId = tool.Id,
                InvokedAt = invokedAt
            };
        }
    }

    private void InjectAuth(HttpRequestMessage request, Domain.ValueObjects.AuthConfigVO authConfig)
    {
        switch (authConfig.AuthType)
        {
            case AuthType.ApiKey:
                if (!string.IsNullOrEmpty(authConfig.EncryptedCredential))
                {
                    var apiKey = _encryptionService.Decrypt(authConfig.EncryptedCredential);
                    var headerName = authConfig.ApiKeyHeaderName ?? "X-Api-Key";
                    request.Headers.Add(headerName, apiKey);
                }
                break;

            case AuthType.Bearer:
                if (!string.IsNullOrEmpty(authConfig.EncryptedCredential))
                {
                    var token = _encryptionService.Decrypt(authConfig.EncryptedCredential);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
                break;

            case AuthType.OAuth2:
                // OAuth2 token acquisition would be handled here in production
                // For now, use the stored credential as a pre-obtained access token
                if (!string.IsNullOrEmpty(authConfig.EncryptedCredential))
                {
                    var accessToken = _encryptionService.Decrypt(authConfig.EncryptedCredential);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                }
                break;

            case AuthType.None:
            default:
                break;
        }
    }
}
