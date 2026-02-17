using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services.DataSources;

/// <summary>
/// Grafana Loki 查询器。使用 LogQL HTTP API 查询日志。
/// </summary>
public class LokiQuerier : IDataSourceQuerier
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LokiQuerier> _logger;

    public LokiQuerier(IHttpClientFactory httpClientFactory, ILogger<LokiQuerier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public bool CanHandle(DataSourceProduct product) =>
        product is DataSourceProduct.Loki;

    public async Task<DataSourceResultVO> QueryAsync(
        DataSourceRegistration registration,
        DataSourceQueryVO query,
        CancellationToken ct = default)
    {
        var client = CreateClient(registration);
        var expression = query.Expression ?? string.Empty;
        var limit = query.Pagination?.Limit ?? registration.DefaultQueryConfig?.MaxResults ?? 100;

        string url;
        if (query.TimeRange is not null)
        {
            var start = query.TimeRange.Start.ToUniversalTime().ToString("o");
            var end = query.TimeRange.End.ToUniversalTime().ToString("o");
            url = $"/loki/api/v1/query_range?query={Uri.EscapeDataString(expression)}&start={start}&end={end}&limit={limit}";

            if (query.TimeRange.Step is not null)
                url += $"&step={query.TimeRange.Step}";
        }
        else
        {
            // Default to last 1 hour range — Loki instant query returns 400 for stream selectors
            var end = DateTime.UtcNow.ToString("o");
            var start = DateTime.UtcNow.AddHours(-1).ToString("o");
            url = $"/loki/api/v1/query_range?query={Uri.EscapeDataString(expression)}&start={start}&end={end}&limit={limit}";
        }

        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return ParseLokiResult(json);
    }

    public async Task<DataSourceHealthVO> HealthCheckAsync(
        DataSourceRegistration registration,
        CancellationToken ct = default)
    {
        var client = CreateClient(registration);
        var sw = Stopwatch.StartNew();

        try
        {
            var response = await client.GetAsync("/ready", ct);
            sw.Stop();

            string? version = null;
            // Try getting build info for version
            try
            {
                var buildResponse = await client.GetAsync("/loki/api/v1/status/buildinfo", ct);
                if (buildResponse.IsSuccessStatusCode)
                {
                    var json = await buildResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
                    if (json.TryGetProperty("version", out var ver))
                        version = ver.GetString();
                }
            }
            catch { /* version is optional */ }

            return new DataSourceHealthVO
            {
                LastCheckAt = DateTime.UtcNow,
                IsHealthy = response.IsSuccessStatusCode,
                ErrorMessage = response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}",
                Version = version,
                ResponseTimeMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Health check failed for Loki datasource {Name}", registration.Name);
            return new DataSourceHealthVO
            {
                LastCheckAt = DateTime.UtcNow,
                IsHealthy = false,
                ErrorMessage = ex.Message,
                ResponseTimeMs = (int)sw.ElapsedMilliseconds
            };
        }
    }

    public async Task<DataSourceMetadataVO> DiscoverMetadataAsync(
        DataSourceRegistration registration,
        CancellationToken ct = default)
    {
        var client = CreateClient(registration);
        var labels = new List<string>();

        try
        {
            var response = await client.GetAsync("/loki/api/v1/labels", ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                if (json.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    labels = data.EnumerateArray().Select(e => e.GetString()!).ToList();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Metadata discovery failed for Loki datasource {Name}", registration.Name);
        }

        var availableFunctions = registration.GenerateAvailableFunctionNames();

        return new DataSourceMetadataVO
        {
            DiscoveredAt = DateTime.UtcNow,
            Labels = labels,
            AvailableFunctions = availableFunctions
        };
    }

    private HttpClient CreateClient(DataSourceRegistration registration)
    {
        var client = _httpClientFactory.CreateClient("DataSourceQuerier");
        client.BaseAddress = new Uri(registration.ConnectionConfig.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(registration.ConnectionConfig.TimeoutSeconds);
        ApplyAuth(client, registration.ConnectionConfig);

        if (registration.ConnectionConfig.CustomHeaders is { Count: > 0 })
        {
            foreach (var (key, value) in registration.ConnectionConfig.CustomHeaders)
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
            }
        }

        return client;
    }

    private static void ApplyAuth(HttpClient client, DataSourceConnectionVO config)
    {
        if (string.IsNullOrEmpty(config.EncryptedCredential))
            return;

        switch (config.AuthType)
        {
            case "Bearer":
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.EncryptedCredential);
                break;
            case "ApiKey":
                var headerName = config.AuthHeaderName ?? "X-Api-Key";
                client.DefaultRequestHeaders.TryAddWithoutValidation(headerName, config.EncryptedCredential);
                break;
            case "BasicAuth":
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
                        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(config.EncryptedCredential)));
                break;
        }
    }

    private static DataSourceResultVO ParseLokiResult(JsonElement json)
    {
        var logEntries = new List<LogEntryVO>();

        if (json.TryGetProperty("data", out var data) &&
            data.TryGetProperty("result", out var results) &&
            results.ValueKind == JsonValueKind.Array)
        {
            foreach (var stream in results.EnumerateArray())
            {
                var labels = new Dictionary<string, string>();
                if (stream.TryGetProperty("stream", out var streamLabels))
                {
                    foreach (var prop in streamLabels.EnumerateObject())
                    {
                        labels[prop.Name] = prop.Value.GetString() ?? string.Empty;
                    }
                }

                if (stream.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in values.EnumerateArray())
                    {
                        if (entry.ValueKind == JsonValueKind.Array && entry.GetArrayLength() >= 2)
                        {
                            // Loki timestamps are nanoseconds since epoch
                            var tsNano = long.Parse(entry[0].GetString()!);
                            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(tsNano / 1_000_000).UtcDateTime;
                            var message = entry[1].GetString() ?? string.Empty;

                            logEntries.Add(new LogEntryVO
                            {
                                Timestamp = timestamp,
                                Message = message,
                                Labels = new Dictionary<string, string>(labels),
                                Level = labels.TryGetValue("level", out var level) ? level : null,
                                Source = labels.TryGetValue("job", out var job) ? job : null
                            });
                        }
                    }
                }
            }
        }

        // Sort by timestamp descending
        logEntries = logEntries.OrderByDescending(e => e.Timestamp).ToList();

        return new DataSourceResultVO
        {
            ResultType = DataSourceResultType.LogEntries,
            LogEntries = logEntries,
            TotalCount = logEntries.Count
        };
    }
}
