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
/// ArgoCD 查询器。通过 ArgoCD REST API (/api/v1/*) 查询 Application 资源。
/// </summary>
public class ArgoCDQuerier : IDataSourceQuerier
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ArgoCDQuerier> _logger;

    public ArgoCDQuerier(IHttpClientFactory httpClientFactory, ILogger<ArgoCDQuerier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public bool CanHandle(DataSourceProduct product) =>
        product is DataSourceProduct.ArgoCD;

    public async Task<DataSourceResultVO> QueryAsync(
        DataSourceRegistration registration,
        DataSourceQueryVO query,
        CancellationToken ct = default)
    {
        var client = CreateClient(registration);

        // Build query URL
        var url = "/api/v1/applications";
        var queryParams = new List<string>();

        // Label selector from filters
        var labelSelector = BuildLabelSelector(query);
        if (!string.IsNullOrEmpty(labelSelector))
            queryParams.Add($"selector={Uri.EscapeDataString(labelSelector)}");

        // Project filter from AdditionalParams
        if (query.AdditionalParams?.TryGetValue("project", out var project) == true)
            queryParams.Add($"projects={Uri.EscapeDataString(project)}");

        // Namespace filter
        if (query.AdditionalParams?.TryGetValue("namespace", out var ns) == true)
            queryParams.Add($"appNamespace={Uri.EscapeDataString(ns)}");

        if (queryParams.Count > 0)
            url += "?" + string.Join("&", queryParams);

        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var resources = ParseApplications(json);

        // Apply expression filter if provided (filter by app name)
        if (!string.IsNullOrEmpty(query.Expression))
        {
            resources = resources
                .Where(r => r.Name.Contains(query.Expression, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return new DataSourceResultVO
        {
            ResultType = DataSourceResultType.Resources,
            Resources = resources,
            TotalCount = resources.Count
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
            // ArgoCD version endpoint
            var response = await client.GetAsync("/api/version", ct);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                var version = json.TryGetProperty("Version", out var ver) ? ver.GetString() : null;

                return new DataSourceHealthVO
                {
                    LastCheckAt = DateTime.UtcNow,
                    IsHealthy = true,
                    Version = version,
                    ResponseTimeMs = (int)sw.ElapsedMilliseconds
                };
            }

            return new DataSourceHealthVO
            {
                LastCheckAt = DateTime.UtcNow,
                IsHealthy = false,
                ErrorMessage = $"HTTP {(int)response.StatusCode}",
                ResponseTimeMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Health check failed for ArgoCD datasource {Name}", registration.Name);
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
        var namespaces = new List<string>();
        var services = new List<string>(); // ArgoCD projects

        try
        {
            // Discover projects
            var projectsResponse = await client.GetAsync("/api/v1/projects", ct);
            if (projectsResponse.IsSuccessStatusCode)
            {
                var json = await projectsResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
                if (json.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    services = items.EnumerateArray()
                        .Select(p => p.TryGetProperty("metadata", out var meta) &&
                                     meta.TryGetProperty("name", out var name)
                            ? name.GetString() ?? ""
                            : "")
                        .Where(n => !string.IsNullOrEmpty(n))
                        .ToList();
                }
            }

            // Discover unique namespaces from applications
            var appsResponse = await client.GetAsync("/api/v1/applications", ct);
            if (appsResponse.IsSuccessStatusCode)
            {
                var json = await appsResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
                if (json.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    namespaces = items.EnumerateArray()
                        .Select(app =>
                        {
                            if (app.TryGetProperty("spec", out var spec) &&
                                spec.TryGetProperty("destination", out var dest) &&
                                dest.TryGetProperty("namespace", out var ns))
                                return ns.GetString();
                            return null;
                        })
                        .Where(n => !string.IsNullOrEmpty(n))
                        .Distinct()
                        .ToList()!;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Metadata discovery failed for ArgoCD datasource {Name}", registration.Name);
        }

        return new DataSourceMetadataVO
        {
            DiscoveredAt = DateTime.UtcNow,
            Namespaces = namespaces,
            Services = services, // ArgoCD projects
            AvailableFunctions = registration.GenerateAvailableFunctionNames()
        };
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static List<ResourceVO> ParseApplications(JsonElement json)
    {
        var resources = new List<ResourceVO>();

        if (!json.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return resources;

        foreach (var app in items.EnumerateArray())
        {
            var name = app.TryGetProperty("metadata", out var meta) &&
                       meta.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";

            var ns = meta.TryGetProperty("namespace", out var nsEl) ? nsEl.GetString() : null;

            // ArgoCD health status
            var healthStatus = app.TryGetProperty("status", out var status) &&
                               status.TryGetProperty("health", out var health) &&
                               health.TryGetProperty("status", out var hs)
                ? hs.GetString()
                : "Unknown";

            // ArgoCD sync status
            var syncStatus = status.TryGetProperty("sync", out var sync) &&
                             sync.TryGetProperty("status", out var ss)
                ? ss.GetString()
                : "Unknown";

            // Labels
            var labels = new Dictionary<string, string>();
            if (meta.TryGetProperty("labels", out var labelEl) && labelEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in labelEl.EnumerateObject())
                    labels[prop.Name] = prop.Value.GetString() ?? "";
            }

            // Destination
            string? destServer = null;
            string? destNs = null;
            string? repoUrl = null;
            string? project = null;

            if (app.TryGetProperty("spec", out var spec))
            {
                if (spec.TryGetProperty("destination", out var dest))
                {
                    destServer = dest.TryGetProperty("server", out var srv) ? srv.GetString() : null;
                    destNs = dest.TryGetProperty("namespace", out var destNsEl) ? destNsEl.GetString() : null;
                }
                repoUrl = spec.TryGetProperty("source", out var source) &&
                          source.TryGetProperty("repoURL", out var repo)
                    ? repo.GetString()
                    : null;
                project = spec.TryGetProperty("project", out var projEl) ? projEl.GetString() : null;
            }

            resources.Add(new ResourceVO
            {
                Kind = "Application",
                Name = name,
                Namespace = ns,
                Status = $"{healthStatus}/{syncStatus}",
                Labels = labels.Count > 0 ? labels : null,
                Properties = new Dictionary<string, object?>
                {
                    ["healthStatus"] = healthStatus,
                    ["syncStatus"] = syncStatus,
                    ["project"] = project,
                    ["repoUrl"] = repoUrl,
                    ["destinationServer"] = destServer,
                    ["destinationNamespace"] = destNs
                },
                UpdatedAt = status.TryGetProperty("operationState", out var opState) &&
                            opState.TryGetProperty("finishedAt", out var finAt) &&
                            DateTime.TryParse(finAt.GetString(), out var dt)
                    ? dt.ToUniversalTime()
                    : null
            });
        }

        return resources;
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
        }
    }

    private static string? BuildLabelSelector(DataSourceQueryVO query)
    {
        if (query.Filters is null or { Count: 0 })
            return null;

        var selectors = query.Filters
            .Where(f => !f.Key.Equals("namespace", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Operator switch
            {
                LabelOperator.Eq => $"{f.Key}={f.Value}",
                LabelOperator.Neq => $"{f.Key}!={f.Value}",
                _ => $"{f.Key}={f.Value}"
            })
            .ToList();

        return selectors.Count > 0 ? string.Join(",", selectors) : null;
    }
}
