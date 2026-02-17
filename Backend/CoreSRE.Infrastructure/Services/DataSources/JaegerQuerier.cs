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
/// Jaeger 查询器。使用 Jaeger HTTP Query API 查询追踪数据。
/// 也兼容 Tempo（Grafana Tempo 提供 Jaeger 兼容 API）。
/// </summary>
public class JaegerQuerier : IDataSourceQuerier
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<JaegerQuerier> _logger;

    public JaegerQuerier(IHttpClientFactory httpClientFactory, ILogger<JaegerQuerier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public bool CanHandle(DataSourceProduct product) =>
        product is DataSourceProduct.Jaeger or DataSourceProduct.Tempo;

    public async Task<DataSourceResultVO> QueryAsync(
        DataSourceRegistration registration,
        DataSourceQueryVO query,
        CancellationToken ct = default)
    {
        var client = CreateClient(registration);

        // If expression looks like a TraceID (hex string), fetch single trace
        if (!string.IsNullOrEmpty(query.Expression) && IsTraceId(query.Expression))
        {
            return await GetTraceById(client, query.Expression, ct);
        }

        // Otherwise, search traces
        return await SearchTraces(client, query, ct);
    }

    public async Task<DataSourceHealthVO> HealthCheckAsync(
        DataSourceRegistration registration,
        CancellationToken ct = default)
    {
        var client = CreateClient(registration);
        var sw = Stopwatch.StartNew();

        try
        {
            // Jaeger health endpoint
            var response = await client.GetAsync("/", ct);
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
            _logger.LogWarning(ex, "Health check failed for Jaeger datasource {Name}", registration.Name);
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
        var services = new List<string>();

        try
        {
            var response = await client.GetAsync("/api/services", ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                if (json.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    services = data.EnumerateArray().Select(e => e.GetString()!).ToList();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Metadata discovery failed for Jaeger datasource {Name}", registration.Name);
        }

        return new DataSourceMetadataVO
        {
            DiscoveredAt = DateTime.UtcNow,
            Services = services,
            AvailableFunctions = registration.GenerateAvailableFunctionNames()
        };
    }

    private async Task<DataSourceResultVO> GetTraceById(HttpClient client, string traceId, CancellationToken ct)
    {
        var response = await client.GetAsync($"/api/traces/{traceId}", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var spans = ParseJaegerTraceResponse(json);

        return new DataSourceResultVO
        {
            ResultType = DataSourceResultType.Spans,
            Spans = spans,
            TotalCount = spans.Count
        };
    }

    private async Task<DataSourceResultVO> SearchTraces(HttpClient client, DataSourceQueryVO query, CancellationToken ct)
    {
        var queryParams = new List<string>();

        // Extract service from filters
        var serviceFilter = query.Filters?.FirstOrDefault(f =>
            f.Key.Equals("service", StringComparison.OrdinalIgnoreCase) ||
            f.Key.Equals("service.name", StringComparison.OrdinalIgnoreCase));

        if (serviceFilter is not null)
            queryParams.Add($"service={Uri.EscapeDataString(serviceFilter.Value)}");

        // Extract operation from filters
        var operationFilter = query.Filters?.FirstOrDefault(f =>
            f.Key.Equals("operation", StringComparison.OrdinalIgnoreCase));

        if (operationFilter is not null)
            queryParams.Add($"operation={Uri.EscapeDataString(operationFilter.Value)}");

        // Tags
        var tagFilters = query.Filters?.Where(f =>
            !f.Key.Equals("service", StringComparison.OrdinalIgnoreCase) &&
            !f.Key.Equals("service.name", StringComparison.OrdinalIgnoreCase) &&
            !f.Key.Equals("operation", StringComparison.OrdinalIgnoreCase)).ToList();

        if (tagFilters is { Count: > 0 })
        {
            var tags = string.Join(" ", tagFilters.Select(f => $"{f.Key}={f.Value}"));
            queryParams.Add($"tags={Uri.EscapeDataString(tags)}");
        }

        if (query.TimeRange is not null)
        {
            var startMicros = new DateTimeOffset(query.TimeRange.Start.ToUniversalTime()).ToUnixTimeMilliseconds() * 1000;
            var endMicros = new DateTimeOffset(query.TimeRange.End.ToUniversalTime()).ToUnixTimeMilliseconds() * 1000;
            queryParams.Add($"start={startMicros}");
            queryParams.Add($"end={endMicros}");
        }

        var limit = query.Pagination?.Limit ?? 20;
        queryParams.Add($"limit={limit}");

        var url = $"/api/traces?{string.Join("&", queryParams)}";
        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var spans = ParseJaegerTraceResponse(json);

        return new DataSourceResultVO
        {
            ResultType = DataSourceResultType.Spans,
            Spans = spans,
            TotalCount = spans.Count
        };
    }

    private static List<SpanVO> ParseJaegerTraceResponse(JsonElement json)
    {
        var spans = new List<SpanVO>();

        if (!json.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return spans;

        foreach (var trace in data.EnumerateArray())
        {
            // Build process map (processID → serviceName)
            var processMap = new Dictionary<string, string>();
            if (trace.TryGetProperty("processes", out var processes))
            {
                foreach (var proc in processes.EnumerateObject())
                {
                    if (proc.Value.TryGetProperty("serviceName", out var sn))
                        processMap[proc.Name] = sn.GetString() ?? string.Empty;
                }
            }

            if (!trace.TryGetProperty("spans", out var jaegerSpans) || jaegerSpans.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var span in jaegerSpans.EnumerateArray())
            {
                var traceId = span.TryGetProperty("traceID", out var tid) ? tid.GetString() ?? string.Empty : string.Empty;
                var spanId = span.TryGetProperty("spanID", out var sid) ? sid.GetString() ?? string.Empty : string.Empty;
                var operationName = span.TryGetProperty("operationName", out var op) ? op.GetString() ?? string.Empty : string.Empty;
                var duration = span.TryGetProperty("duration", out var dur) ? dur.GetInt64() : 0;
                var startTime = span.TryGetProperty("startTime", out var st) ? st.GetInt64() : 0;

                // Get process → service name
                var processId = span.TryGetProperty("processID", out var pid) ? pid.GetString() ?? string.Empty : string.Empty;
                processMap.TryGetValue(processId, out var serviceName);

                // References → parent span
                string? parentSpanId = null;
                if (span.TryGetProperty("references", out var refs) && refs.ValueKind == JsonValueKind.Array)
                {
                    var childOf = refs.EnumerateArray().FirstOrDefault(r =>
                        r.TryGetProperty("refType", out var rt) && rt.GetString() == "CHILD_OF");
                    if (childOf.TryGetProperty("spanID", out var psid))
                        parentSpanId = psid.GetString();
                }

                // Tags
                var tags = new Dictionary<string, string>();
                if (span.TryGetProperty("tags", out var tagArray) && tagArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tag in tagArray.EnumerateArray())
                    {
                        var key = tag.TryGetProperty("key", out var k) ? k.GetString() ?? string.Empty : string.Empty;
                        var value = tag.TryGetProperty("value", out var v) ? v.ToString() : string.Empty;
                        tags[key] = value;
                    }
                }

                spans.Add(new SpanVO
                {
                    TraceId = traceId,
                    SpanId = spanId,
                    ParentSpanId = parentSpanId,
                    OperationName = operationName,
                    ServiceName = serviceName ?? string.Empty,
                    DurationMicros = duration,
                    StartTime = DateTimeOffset.FromUnixTimeMilliseconds(startTime / 1000).UtcDateTime,
                    Tags = tags,
                    Status = tags.TryGetValue("otel.status_code", out var statusCode) ? statusCode : null
                });
            }
        }

        return spans;
    }

    private static bool IsTraceId(string value) =>
        value.Length is >= 16 and <= 32 && value.All(c => char.IsAsciiHexDigit(c));

    private HttpClient CreateClient(DataSourceRegistration registration)
    {
        var client = _httpClientFactory.CreateClient("DataSourceQuerier");
        client.BaseAddress = new Uri(registration.ConnectionConfig.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(registration.ConnectionConfig.TimeoutSeconds);
        ApplyAuth(client, registration.ConnectionConfig);

        if (registration.ConnectionConfig.CustomHeaders is { Count: > 0 })
        {
            foreach (var (key, value) in registration.ConnectionConfig.CustomHeaders)
                client.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
        }

        return client;
    }

    private static void ApplyAuth(HttpClient client, DataSourceConnectionVO config)
    {
        if (string.IsNullOrEmpty(config.EncryptedCredential)) return;

        switch (config.AuthType)
        {
            case "Bearer":
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.EncryptedCredential);
                break;
            case "ApiKey":
                client.DefaultRequestHeaders.TryAddWithoutValidation(config.AuthHeaderName ?? "X-Api-Key", config.EncryptedCredential);
                break;
            case "BasicAuth":
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
                        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(config.EncryptedCredential)));
                break;
        }
    }
}
