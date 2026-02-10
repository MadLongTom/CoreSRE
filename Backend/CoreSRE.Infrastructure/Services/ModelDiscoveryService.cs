using System.Net.Http.Headers;
using System.Text.Json;
using CoreSRE.Application.Common.Interfaces;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// 模型发现服务实现。通过 HttpClient 调用 OpenAI 兼容的 GET /models 端点。
/// </summary>
public class ModelDiscoveryService : IModelDiscoveryService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ModelDiscoveryService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<List<string>> DiscoverModelsAsync(
        string baseUrl,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("ModelDiscovery");

        // Normalize base URL — remove trailing slash
        var normalizedBaseUrl = baseUrl.TrimEnd('/');
        var requestUrl = $"{normalizedBaseUrl}/models";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Failed to connect to provider: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"Request to provider timed out: {requestUrl}", ex);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedAccessException(
                "Authentication failed: invalid API key or unauthorized access.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Provider returned error: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (!root.TryGetProperty("data", out var dataArray) || dataArray.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    "Unexpected response format: missing 'data' array property.");
            }

            var modelIds = new List<string>();
            foreach (var item in dataArray.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                {
                    var id = idProp.GetString();
                    if (!string.IsNullOrWhiteSpace(id))
                        modelIds.Add(id);
                }
            }

            return modelIds;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Failed to parse provider response: {ex.Message}", ex);
        }
    }
}
