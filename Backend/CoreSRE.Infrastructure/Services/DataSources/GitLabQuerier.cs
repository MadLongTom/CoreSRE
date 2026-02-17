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
/// GitLab 查询器。通过 GitLab REST API v4 查询项目、Commit、Merge Request、Pipeline 等资源。
/// Organization 字段格式："group/project" 或 "group"（列出所有项目），或 numeric project ID。
/// </summary>
public class GitLabQuerier : IDataSourceQuerier
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GitLabQuerier> _logger;

    public GitLabQuerier(IHttpClientFactory httpClientFactory, ILogger<GitLabQuerier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public bool CanHandle(DataSourceProduct product) =>
        product is DataSourceProduct.GitLab;

    public async Task<DataSourceResultVO> QueryAsync(
        DataSourceRegistration registration,
        DataSourceQueryVO query,
        CancellationToken ct = default)
    {
        var client = CreateClient(registration);
        var projectId = GetProjectId(registration, query);

        var kind = ExtractKind(query);

        var resources = kind?.ToLowerInvariant() switch
        {
            "commit" or "commits" => await ListCommitsAsync(client, projectId, query, ct),
            "mergerequest" or "merge_request" or "mr" or "mrs" => await ListMergeRequestsAsync(client, projectId, query, ct),
            "pipeline" or "pipelines" => await ListPipelinesAsync(client, projectId, query, ct),
            "issue" or "issues" => await ListIssuesAsync(client, projectId, query, ct),
            "release" or "releases" => await ListReleasesAsync(client, projectId, query, ct),
            "project" or "projects" => await ListProjectsAsync(client, registration, query, ct),
            _ => await ListCommitsAsync(client, projectId, query, ct)
        };

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
            var response = await client.GetAsync("/api/v4/version", ct);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                var version = json.TryGetProperty("version", out var ver) ? ver.GetString() : null;

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
            _logger.LogWarning(ex, "Health check failed for GitLab datasource {Name}", registration.Name);
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
        var services = new List<string>(); // Project list

        try
        {
            var org = registration.ConnectionConfig.Organization;
            if (!string.IsNullOrEmpty(org) && !org.All(char.IsDigit) && !org.Contains('/'))
            {
                // List group projects
                var response = await client.GetAsync(
                    $"/api/v4/groups/{Uri.EscapeDataString(org)}/projects?per_page=100", ct);
                if (response.IsSuccessStatusCode)
                {
                    var projects = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                    if (projects.ValueKind == JsonValueKind.Array)
                    {
                        services = projects.EnumerateArray()
                            .Select(p => p.TryGetProperty("path_with_namespace", out var path) ? path.GetString() ?? "" : "")
                            .Where(n => !string.IsNullOrEmpty(n))
                            .ToList();
                    }
                }
            }
            else
            {
                // List all visible projects
                var response = await client.GetAsync("/api/v4/projects?membership=true&per_page=100", ct);
                if (response.IsSuccessStatusCode)
                {
                    var projects = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                    if (projects.ValueKind == JsonValueKind.Array)
                    {
                        services = projects.EnumerateArray()
                            .Select(p => p.TryGetProperty("path_with_namespace", out var path) ? path.GetString() ?? "" : "")
                            .Where(n => !string.IsNullOrEmpty(n))
                            .ToList();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Metadata discovery failed for GitLab datasource {Name}", registration.Name);
        }

        return new DataSourceMetadataVO
        {
            DiscoveredAt = DateTime.UtcNow,
            Services = services,
            AvailableFunctions = registration.GenerateAvailableFunctionNames()
        };
    }

    // ─── Resource Listing Methods ───────────────────────────────────────

    private static async Task<List<ResourceVO>> ListCommitsAsync(
        HttpClient client, string? projectId, DataSourceQueryVO query, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(projectId)) return [];

        var url = $"/api/v4/projects/{Uri.EscapeDataString(projectId)}/repository/commits";
        var queryParams = new List<string>();

        if (query.TimeRange is not null)
        {
            queryParams.Add($"since={query.TimeRange.Start:o}");
            queryParams.Add($"until={query.TimeRange.End:o}");
        }

        if (query.AdditionalParams?.TryGetValue("branch", out var branch) == true)
            queryParams.Add($"ref_name={Uri.EscapeDataString(branch)}");

        var limit = query.Pagination?.Limit ?? 30;
        queryParams.Add($"per_page={limit}");

        if (queryParams.Count > 0)
            url += "?" + string.Join("&", queryParams);

        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var commits = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (commits.ValueKind != JsonValueKind.Array) return [];

        return commits.EnumerateArray().Select(c => new ResourceVO
        {
            Kind = "Commit",
            Name = c.TryGetProperty("short_id", out var sid) ? sid.GetString() ?? "" : "",
            Properties = new Dictionary<string, object?>
            {
                ["sha"] = c.TryGetProperty("id", out var id) ? id.GetString() : null,
                ["message"] = c.TryGetProperty("title", out var title) ? title.GetString() : null,
                ["author"] = c.TryGetProperty("author_name", out var author) ? author.GetString() : null,
                ["authorEmail"] = c.TryGetProperty("author_email", out var email) ? email.GetString() : null,
                ["url"] = c.TryGetProperty("web_url", out var webUrl) ? webUrl.GetString() : null
            },
            UpdatedAt = c.TryGetProperty("created_at", out var createdAt) && DateTime.TryParse(createdAt.GetString(), out var dt)
                ? dt.ToUniversalTime() : null
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListMergeRequestsAsync(
        HttpClient client, string? projectId, DataSourceQueryVO query, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(projectId)) return [];

        var url = $"/api/v4/projects/{Uri.EscapeDataString(projectId)}/merge_requests";
        var queryParams = new List<string>();

        var state = query.AdditionalParams?.TryGetValue("state", out var s) == true ? s : "all";
        queryParams.Add($"state={state}");

        var limit = query.Pagination?.Limit ?? 30;
        queryParams.Add($"per_page={limit}");

        if (query.TimeRange is not null)
        {
            queryParams.Add($"created_after={query.TimeRange.Start:o}");
            queryParams.Add($"created_before={query.TimeRange.End:o}");
        }

        url += "?" + string.Join("&", queryParams);

        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var mrs = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (mrs.ValueKind != JsonValueKind.Array) return [];

        return mrs.EnumerateArray().Select(mr => new ResourceVO
        {
            Kind = "MergeRequest",
            Name = mr.TryGetProperty("iid", out var iid) ? $"!{iid.GetInt32()}" : "",
            Status = mr.TryGetProperty("state", out var st) ? st.GetString() ?? "unknown" : "unknown",
            Labels = mr.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array
                ? labels.EnumerateArray().Select(l => l.GetString() ?? "").Where(l => l != "").ToDictionary(l => l, _ => "label")
                : null,
            Properties = new Dictionary<string, object?>
            {
                ["iid"] = mr.TryGetProperty("iid", out var mrIid) ? mrIid.GetInt32() : 0,
                ["title"] = mr.TryGetProperty("title", out var title) ? title.GetString() : null,
                ["author"] = mr.TryGetProperty("author", out var auth) && auth.TryGetProperty("username", out var username) ? username.GetString() : null,
                ["sourceBranch"] = mr.TryGetProperty("source_branch", out var src) ? src.GetString() : null,
                ["targetBranch"] = mr.TryGetProperty("target_branch", out var tgt) ? tgt.GetString() : null,
                ["url"] = mr.TryGetProperty("web_url", out var webUrl) ? webUrl.GetString() : null
            },
            UpdatedAt = mr.TryGetProperty("updated_at", out var updatedAt) && DateTime.TryParse(updatedAt.GetString(), out var dt)
                ? dt.ToUniversalTime() : null
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListPipelinesAsync(
        HttpClient client, string? projectId, DataSourceQueryVO query, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(projectId)) return [];

        var url = $"/api/v4/projects/{Uri.EscapeDataString(projectId)}/pipelines";
        var queryParams = new List<string>();

        if (query.AdditionalParams?.TryGetValue("status", out var status) == true)
            queryParams.Add($"status={status}");

        if (query.AdditionalParams?.TryGetValue("branch", out var branch) == true)
            queryParams.Add($"ref={Uri.EscapeDataString(branch)}");

        var limit = query.Pagination?.Limit ?? 30;
        queryParams.Add($"per_page={limit}");

        if (queryParams.Count > 0)
            url += "?" + string.Join("&", queryParams);

        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var pipelines = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (pipelines.ValueKind != JsonValueKind.Array) return [];

        return pipelines.EnumerateArray().Select(p => new ResourceVO
        {
            Kind = "Pipeline",
            Name = p.TryGetProperty("id", out var id) ? $"#{id.GetInt32()}" : "",
            Status = p.TryGetProperty("status", out var st) ? st.GetString() ?? "unknown" : "unknown",
            Properties = new Dictionary<string, object?>
            {
                ["id"] = p.TryGetProperty("id", out var pId) ? pId.GetInt32() : 0,
                ["ref"] = p.TryGetProperty("ref", out var refProp) ? refProp.GetString() : null,
                ["sha"] = p.TryGetProperty("sha", out var sha) ? sha.GetString()?[..7] : null,
                ["source"] = p.TryGetProperty("source", out var src) ? src.GetString() : null,
                ["url"] = p.TryGetProperty("web_url", out var webUrl) ? webUrl.GetString() : null
            },
            UpdatedAt = p.TryGetProperty("updated_at", out var updatedAt) && DateTime.TryParse(updatedAt.GetString(), out var dt)
                ? dt.ToUniversalTime() : null
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListIssuesAsync(
        HttpClient client, string? projectId, DataSourceQueryVO query, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(projectId)) return [];

        var url = $"/api/v4/projects/{Uri.EscapeDataString(projectId)}/issues";
        var queryParams = new List<string>();

        var state = query.AdditionalParams?.TryGetValue("state", out var s) == true ? s : "opened";
        queryParams.Add($"state={state}");

        var limit = query.Pagination?.Limit ?? 30;
        queryParams.Add($"per_page={limit}");

        url += "?" + string.Join("&", queryParams);

        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var issues = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (issues.ValueKind != JsonValueKind.Array) return [];

        return issues.EnumerateArray().Select(i => new ResourceVO
        {
            Kind = "Issue",
            Name = i.TryGetProperty("iid", out var iid) ? $"#{iid.GetInt32()}" : "",
            Status = i.TryGetProperty("state", out var st) ? st.GetString() ?? "unknown" : "unknown",
            Properties = new Dictionary<string, object?>
            {
                ["iid"] = i.TryGetProperty("iid", out var issueIid) ? issueIid.GetInt32() : 0,
                ["title"] = i.TryGetProperty("title", out var title) ? title.GetString() : null,
                ["author"] = i.TryGetProperty("author", out var auth) && auth.TryGetProperty("username", out var username) ? username.GetString() : null,
                ["url"] = i.TryGetProperty("web_url", out var webUrl) ? webUrl.GetString() : null
            },
            UpdatedAt = i.TryGetProperty("updated_at", out var updatedAt) && DateTime.TryParse(updatedAt.GetString(), out var dt)
                ? dt.ToUniversalTime() : null
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListReleasesAsync(
        HttpClient client, string? projectId, DataSourceQueryVO query, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(projectId)) return [];

        var limit = query.Pagination?.Limit ?? 20;
        var url = $"/api/v4/projects/{Uri.EscapeDataString(projectId)}/releases?per_page={limit}";

        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var releases = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (releases.ValueKind != JsonValueKind.Array) return [];

        return releases.EnumerateArray().Select(r => new ResourceVO
        {
            Kind = "Release",
            Name = r.TryGetProperty("tag_name", out var tag) ? tag.GetString() ?? "" : "",
            Properties = new Dictionary<string, object?>
            {
                ["name"] = r.TryGetProperty("name", out var name) ? name.GetString() : null,
                ["tagName"] = r.TryGetProperty("tag_name", out var tagName) ? tagName.GetString() : null,
                ["description"] = r.TryGetProperty("description", out var desc)
                    ? (desc.GetString()?.Length > 200 ? desc.GetString()![..200] + "..." : desc.GetString())
                    : null
            },
            UpdatedAt = r.TryGetProperty("released_at", out var relAt) && DateTime.TryParse(relAt.GetString(), out var dt)
                ? dt.ToUniversalTime() : null
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListProjectsAsync(
        HttpClient client, DataSourceRegistration registration, DataSourceQueryVO query, CancellationToken ct)
    {
        var org = registration.ConnectionConfig.Organization;
        var limit = query.Pagination?.Limit ?? 30;

        string url;
        if (!string.IsNullOrEmpty(org) && !org.All(char.IsDigit) && !org.Contains('/'))
        {
            url = $"/api/v4/groups/{Uri.EscapeDataString(org)}/projects?per_page={limit}";
        }
        else
        {
            url = $"/api/v4/projects?membership=true&per_page={limit}";
        }

        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var projects = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (projects.ValueKind != JsonValueKind.Array) return [];

        return projects.EnumerateArray().Select(p => new ResourceVO
        {
            Kind = "Project",
            Name = p.TryGetProperty("path_with_namespace", out var path) ? path.GetString() ?? "" : "",
            Status = p.TryGetProperty("archived", out var archived) && archived.GetBoolean() ? "Archived" : "Active",
            Properties = new Dictionary<string, object?>
            {
                ["description"] = p.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                ["visibility"] = p.TryGetProperty("visibility", out var vis) ? vis.GetString() : null,
                ["defaultBranch"] = p.TryGetProperty("default_branch", out var branch) ? branch.GetString() : null,
                ["stars"] = p.TryGetProperty("star_count", out var stars) ? stars.GetInt32() : 0,
                ["forks"] = p.TryGetProperty("forks_count", out var forks) ? forks.GetInt32() : 0,
                ["url"] = p.TryGetProperty("web_url", out var webUrl) ? webUrl.GetString() : null
            },
            UpdatedAt = p.TryGetProperty("last_activity_at", out var lastActivity) && DateTime.TryParse(lastActivity.GetString(), out var dt)
                ? dt.ToUniversalTime() : null
        }).ToList();
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private HttpClient CreateClient(DataSourceRegistration registration)
    {
        var client = _httpClientFactory.CreateClient("DataSourceQuerier");

        var baseUrl = registration.ConnectionConfig.BaseUrl.TrimEnd('/');
        client.BaseAddress = new Uri(baseUrl);
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
                // GitLab uses PRIVATE-TOKEN header by default
                var headerName = config.AuthHeaderName ?? "PRIVATE-TOKEN";
                client.DefaultRequestHeaders.TryAddWithoutValidation(headerName, config.EncryptedCredential);
                break;
        }
    }

    private static string? GetProjectId(DataSourceRegistration registration, DataSourceQueryVO query)
    {
        // Check AdditionalParams for explicit project
        if (query.AdditionalParams?.TryGetValue("project", out var project) == true)
            return project;

        if (query.AdditionalParams?.TryGetValue("repo", out var repo) == true)
            return repo;

        // Use Organization from connection config
        return registration.ConnectionConfig.Organization;
    }

    private static string? ExtractKind(DataSourceQueryVO query)
    {
        if (!string.IsNullOrEmpty(query.Expression))
        {
            var expr = query.Expression.Trim();
            if (expr.StartsWith("kind=", StringComparison.OrdinalIgnoreCase))
                return expr["kind=".Length..].Trim();
            return expr;
        }

        if (query.AdditionalParams?.TryGetValue("kind", out var kind) == true)
            return kind;

        return null;
    }
}
