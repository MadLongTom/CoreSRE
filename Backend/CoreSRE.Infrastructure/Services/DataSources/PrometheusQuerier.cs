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
/// Prometheus / VictoriaMetrics / Mimir 查询器。
/// 三者共享兼容的 HTTP Query API（/api/v1/*）。
/// </summary>
public class PrometheusQuerier : IDataSourceQuerier
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PrometheusQuerier> _logger;

    public PrometheusQuerier(IHttpClientFactory httpClientFactory, ILogger<PrometheusQuerier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public bool CanHandle(DataSourceProduct product) =>
        product is DataSourceProduct.Prometheus
            or DataSourceProduct.VictoriaMetrics
            or DataSourceProduct.Mimir;

    public async Task<DataSourceResultVO> QueryAsync(
        DataSourceRegistration registration,
        DataSourceQueryVO query,
        CancellationToken ct = default)
    {
        var client = CreateClient(registration);
        var expression = query.Expression ?? string.Empty;

        // Decide between instant query and range query
        if (query.TimeRange is not null)
        {
            var start = query.TimeRange.Start.ToUniversalTime().ToString("o");
            var end = query.TimeRange.End.ToUniversalTime().ToString("o");
            var step = query.TimeRange.Step ?? registration.DefaultQueryConfig?.DefaultStep ?? "60s";

            var url = $"/api/v1/query_range?query={Uri.EscapeDataString(expression)}&start={start}&end={end}&step={step}";
            if (query.Pagination?.Limit > 0)
                url += $"&limit={query.Pagination.Limit}";

            var response = await client.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            return ParseRangeResult(json);
        }
        else
        {
            var url = $"/api/v1/query?query={Uri.EscapeDataString(expression)}";
            var response = await client.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            return ParseInstantResult(json);
        }
    }

    public async Task<DataSourceHealthVO> HealthCheckAsync(
        DataSourceRegistration registration,
        CancellationToken ct = default)
    {
        var client = CreateClient(registration);
        var sw = Stopwatch.StartNew();

        try
        {
            // Try build info first (gives version), fallback to /-/healthy
            var response = await client.GetAsync("/api/v1/status/buildinfo", ct);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                var version = json.TryGetProperty("data", out var data) &&
                              data.TryGetProperty("version", out var ver)
                    ? ver.GetString()
                    : null;

                return new DataSourceHealthVO
                {
                    LastCheckAt = DateTime.UtcNow,
                    IsHealthy = true,
                    Version = version,
                    ResponseTimeMs = (int)sw.ElapsedMilliseconds
                };
            }

            // Fallback: simple health endpoint
            response = await client.GetAsync("/-/healthy", ct);
            sw.Stop();

            return new DataSourceHealthVO
            {
                LastCheckAt = DateTime.UtcNow,
                IsHealthy = response.IsSuccessStatusCode,
                ErrorMessage = response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}",
                ResponseTimeMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Health check failed for Prometheus datasource {Name}", registration.Name);
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
        var metricNames = new List<string>();

        try
        {
            // Discover label names
            var labelsResponse = await client.GetAsync("/api/v1/labels", ct);
            if (labelsResponse.IsSuccessStatusCode)
            {
                var json = await labelsResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
                if (json.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    labels = data.EnumerateArray().Select(e => e.GetString()!).ToList();
                }
            }

            // Discover metric names (label values for __name__)
            var namesResponse = await client.GetAsync("/api/v1/label/__name__/values", ct);
            if (namesResponse.IsSuccessStatusCode)
            {
                var json = await namesResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
                if (json.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    metricNames = data.EnumerateArray().Select(e => e.GetString()!).ToList();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Metadata discovery failed for Prometheus datasource {Name}", registration.Name);
        }

        var availableFunctions = registration.GenerateAvailableFunctionNames();

        return new DataSourceMetadataVO
        {
            DiscoveredAt = DateTime.UtcNow,
            Labels = labels,
            Services = metricNames, // Prometheus exposes metric names as "services"
            AvailableFunctions = availableFunctions
        };
    }

    private HttpClient CreateClient(DataSourceRegistration registration)
    {
        var client = _httpClientFactory.CreateClient("DataSourceQuerier");
        client.BaseAddress = new Uri(registration.ConnectionConfig.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(registration.ConnectionConfig.TimeoutSeconds);

        // Apply auth
        ApplyAuth(client, registration.ConnectionConfig);

        // Apply custom headers
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

    private static DataSourceResultVO ParseRangeResult(JsonElement json)
    {
        var timeSeries = new List<TimeSeriesVO>();

        if (json.TryGetProperty("data", out var data) &&
            data.TryGetProperty("result", out var results) &&
            results.ValueKind == JsonValueKind.Array)
        {
            foreach (var series in results.EnumerateArray())
            {
                var labels = new Dictionary<string, string>();
                var metricName = string.Empty;

                if (series.TryGetProperty("metric", out var metric))
                {
                    foreach (var prop in metric.EnumerateObject())
                    {
                        if (prop.Name == "__name__")
                            metricName = prop.Value.GetString() ?? string.Empty;
                        else
                            labels[prop.Name] = prop.Value.GetString() ?? string.Empty;
                    }
                }

                var dataPoints = new List<DataPointVO>();
                if (series.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array)
                {
                    foreach (var point in values.EnumerateArray())
                    {
                        if (point.ValueKind == JsonValueKind.Array && point.GetArrayLength() >= 2)
                        {
                            var timestamp = DateTimeOffset.FromUnixTimeSeconds((long)point[0].GetDouble()).UtcDateTime;
                            var value = double.TryParse(point[1].GetString(), out var v) ? v : 0;
                            dataPoints.Add(new DataPointVO { Timestamp = timestamp, Value = value });
                        }
                    }
                }

                timeSeries.Add(new TimeSeriesVO
                {
                    MetricName = metricName,
                    Labels = labels,
                    DataPoints = dataPoints
                });
            }
        }

        return new DataSourceResultVO
        {
            ResultType = DataSourceResultType.TimeSeries,
            TimeSeries = timeSeries,
            TotalCount = timeSeries.Count
        };
    }

    private static DataSourceResultVO ParseInstantResult(JsonElement json)
    {
        var timeSeries = new List<TimeSeriesVO>();

        if (json.TryGetProperty("data", out var data) &&
            data.TryGetProperty("result", out var results) &&
            results.ValueKind == JsonValueKind.Array)
        {
            foreach (var series in results.EnumerateArray())
            {
                var labels = new Dictionary<string, string>();
                var metricName = string.Empty;

                if (series.TryGetProperty("metric", out var metric))
                {
                    foreach (var prop in metric.EnumerateObject())
                    {
                        if (prop.Name == "__name__")
                            metricName = prop.Value.GetString() ?? string.Empty;
                        else
                            labels[prop.Name] = prop.Value.GetString() ?? string.Empty;
                    }
                }

                var dataPoints = new List<DataPointVO>();
                if (series.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() >= 2)
                {
                    var timestamp = DateTimeOffset.FromUnixTimeSeconds((long)value[0].GetDouble()).UtcDateTime;
                    var val = double.TryParse(value[1].GetString(), out var v) ? v : 0;
                    dataPoints.Add(new DataPointVO { Timestamp = timestamp, Value = val });
                }

                timeSeries.Add(new TimeSeriesVO
                {
                    MetricName = metricName,
                    Labels = labels,
                    DataPoints = dataPoints
                });
            }
        }

        return new DataSourceResultVO
        {
            ResultType = DataSourceResultType.TimeSeries,
            TimeSeries = timeSeries,
            TotalCount = timeSeries.Count
        };
    }
}
