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
/// Alertmanager 查询器。使用 Alertmanager v2 HTTP API 查询告警。
/// </summary>
public class AlertmanagerQuerier : IDataSourceQuerier
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AlertmanagerQuerier> _logger;

    public AlertmanagerQuerier(IHttpClientFactory httpClientFactory, ILogger<AlertmanagerQuerier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public bool CanHandle(DataSourceProduct product) =>
        product is DataSourceProduct.Alertmanager;

    public async Task<DataSourceResultVO> QueryAsync(
        DataSourceRegistration registration,
        DataSourceQueryVO query,
        CancellationToken ct = default)
    {
        var client = CreateClient(registration);

        // Build filter query params from Filters
        var queryParams = new List<string>();
        if (query.Filters is { Count: > 0 })
        {
            foreach (var filter in query.Filters)
            {
                // Alertmanager uses filter[]=label=value syntax
                queryParams.Add($"filter={Uri.EscapeDataString($"{filter.Key}=\"{filter.Value}\"")}");
            }
        }

        // Filter by state (active/suppressed/unprocessed)
        if (query.AdditionalParams?.TryGetValue("state", out var state) == true)
        {
            queryParams.Add($"active={state.Contains("active", StringComparison.OrdinalIgnoreCase).ToString().ToLower()}");
            queryParams.Add($"silenced={state.Contains("suppressed", StringComparison.OrdinalIgnoreCase).ToString().ToLower()}");
        }

        var url = "/api/v2/alerts";
        if (queryParams.Count > 0)
            url += "?" + string.Join("&", queryParams);

        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var alerts = ParseAlertmanagerResponse(json);

        // Apply limit
        var limit = query.Pagination?.Limit ?? 100;
        var truncated = alerts.Count > limit;
        if (truncated)
            alerts = alerts.Take(limit).ToList();

        return new DataSourceResultVO
        {
            ResultType = DataSourceResultType.Alerts,
            Alerts = alerts,
            TotalCount = alerts.Count,
            Truncated = truncated
        };
    }

    public async Task<DataSourceHealthVO> HealthCheckAsync(
        DataSourceRegistration registration,
        CancellationToken ct = default)
    {
        var client = CreateClient(registration);
        var sw = Stopwatch.StartNew();

        try
        {
            // Alertmanager v2 status endpoint
            var response = await client.GetAsync("/api/v2/status", ct);
            sw.Stop();

            string? version = null;
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                if (json.TryGetProperty("versionInfo", out var vi) &&
                    vi.TryGetProperty("version", out var ver))
                    version = ver.GetString();
            }

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
            _logger.LogWarning(ex, "Health check failed for Alertmanager datasource {Name}", registration.Name);
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
            // Fetch current alerts to discover label names
            var response = await client.GetAsync("/api/v2/alerts?active=true", ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                var labelSet = new HashSet<string>();
                if (json.ValueKind == JsonValueKind.Array)
                {
                    foreach (var alert in json.EnumerateArray())
                    {
                        if (alert.TryGetProperty("labels", out var alertLabels))
                        {
                            foreach (var prop in alertLabels.EnumerateObject())
                                labelSet.Add(prop.Name);
                        }
                    }
                }
                labels = labelSet.OrderBy(l => l).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Metadata discovery failed for Alertmanager datasource {Name}", registration.Name);
        }

        return new DataSourceMetadataVO
        {
            DiscoveredAt = DateTime.UtcNow,
            Labels = labels,
            AvailableFunctions = registration.GenerateAvailableFunctionNames()
        };
    }

    private static List<AlertVO> ParseAlertmanagerResponse(JsonElement json)
    {
        var alerts = new List<AlertVO>();
        if (json.ValueKind != JsonValueKind.Array) return alerts;

        foreach (var alert in json.EnumerateArray())
        {
            var labels = new Dictionary<string, string>();
            if (alert.TryGetProperty("labels", out var labelObj))
            {
                foreach (var prop in labelObj.EnumerateObject())
                    labels[prop.Name] = prop.Value.GetString() ?? string.Empty;
            }

            var annotations = new Dictionary<string, string>();
            if (alert.TryGetProperty("annotations", out var annObj))
            {
                foreach (var prop in annObj.EnumerateObject())
                    annotations[prop.Name] = prop.Value.GetString() ?? string.Empty;
            }

            var startsAt = alert.TryGetProperty("startsAt", out var sa)
                ? DateTime.TryParse(sa.GetString(), out var dt) ? dt.ToUniversalTime() : DateTime.UtcNow
                : DateTime.UtcNow;

            DateTime? endsAt = null;
            if (alert.TryGetProperty("endsAt", out var ea) && DateTime.TryParse(ea.GetString(), out var endDt))
                endsAt = endDt.ToUniversalTime();

            var status = "firing";
            if (alert.TryGetProperty("status", out var statusObj) &&
                statusObj.TryGetProperty("state", out var stateVal))
                status = stateVal.GetString() ?? "firing";

            var fingerprint = alert.TryGetProperty("fingerprint", out var fp) ? fp.GetString() : null;

            alerts.Add(new AlertVO
            {
                AlertName = labels.TryGetValue("alertname", out var name) ? name : "unknown",
                Severity = labels.TryGetValue("severity", out var sev) ? sev : null,
                Status = status,
                StartsAt = startsAt,
                EndsAt = endsAt,
                Labels = labels,
                Annotations = annotations,
                Fingerprint = fingerprint
            });
        }

        return alerts.OrderByDescending(a => a.StartsAt).ToList();
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
