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
/// Gitea 查询器。通过 Gitea REST API v1 查询仓库、Commit、Pull Request 等资源。
/// Organization 字段格式："owner/repo"（单仓库）或 "owner"（列出该组织下所有仓库）。
/// </summary>
public class GiteaQuerier : IDataSourceQuerier
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GiteaQuerier> _logger;

    public GiteaQuerier(IHttpClientFactory httpClientFactory, ILogger<GiteaQuerier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public bool CanHandle(DataSourceProduct product) =>
        product is DataSourceProduct.Gitea;

    public async Task<DataSourceResultVO> QueryAsync(
        DataSourceRegistration registration,
        DataSourceQueryVO query,
        CancellationToken ct = default)
    {
        var client = CreateClient(registration);
        var repoPath = GetRepoPath(registration, query);

        var kind = ExtractKind(query);

        var resources = kind?.ToLowerInvariant() switch
        {
            "commit" or "commits" => await ListCommitsAsync(client, repoPath, query, ct),
            "pull" or "pulls" or "pull_request" or "pr" => await ListPullRequestsAsync(client, repoPath, query, ct),
            "release" or "releases" => await ListReleasesAsync(client, repoPath, query, ct),
            "repo" or "repos" or "repository" => await ListReposAsync(client, registration, query, ct),
            "issue" or "issues" => await ListIssuesAsync(client, repoPath, query, ct),
            _ => await ListCommitsAsync(client, repoPath, query, ct)
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
            var response = await client.GetAsync("/api/v1/version", ct);
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
            _logger.LogWarning(ex, "Health check failed for Gitea datasource {Name}", registration.Name);
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
        var repos = new List<string>();

        try
        {
            var org = registration.ConnectionConfig.Organization;
            string url;
            if (!string.IsNullOrEmpty(org) && !org.Contains('/'))
            {
                // List org repos
                url = $"/api/v1/orgs/{Uri.EscapeDataString(org)}/repos?limit=100";
            }
            else
            {
                // List repos visible to current user
                url = "/api/v1/repos/search?limit=100";
            }

            var response = await client.GetAsync(url, ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

                // Gitea /repos/search returns { data: [...] }, org repos returns [...]
                var array = json.ValueKind == JsonValueKind.Array
                    ? json
                    : json.TryGetProperty("data", out var data) ? data : json;

                if (array.ValueKind == JsonValueKind.Array)
                {
                    repos = array.EnumerateArray()
                        .Select(r => r.TryGetProperty("full_name", out var fn) ? fn.GetString() ?? "" : "")
                        .Where(n => !string.IsNullOrEmpty(n))
                        .ToList();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Metadata discovery failed for Gitea datasource {Name}", registration.Name);
        }

        return new DataSourceMetadataVO
        {
            DiscoveredAt = DateTime.UtcNow,
            Services = repos,
            AvailableFunctions = registration.GenerateAvailableFunctionNames()
        };
    }

    // ─── Resource Listing Methods ───────────────────────────────────────

    private static async Task<List<ResourceVO>> ListCommitsAsync(
        HttpClient client, string? repoPath, DataSourceQueryVO query, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(repoPath)) return [];

        var url = $"/api/v1/repos/{repoPath}/commits";
        var queryParams = new List<string>();

        if (query.AdditionalParams?.TryGetValue("branch", out var branch) == true)
            queryParams.Add($"sha={Uri.EscapeDataString(branch)}");

        var limit = query.Pagination?.Limit ?? 30;
        queryParams.Add($"limit={limit}");

        if (queryParams.Count > 0)
            url += "?" + string.Join("&", queryParams);

        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var commits = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (commits.ValueKind != JsonValueKind.Array) return [];

        return commits.EnumerateArray().Select(c => new ResourceVO
        {
            Kind = "Commit",
            Name = c.TryGetProperty("sha", out var sha) ? sha.GetString()?[..7] ?? "" : "",
            Properties = new Dictionary<string, object?>
            {
                ["sha"] = c.TryGetProperty("sha", out var fullSha) ? fullSha.GetString() : null,
                ["message"] = c.TryGetProperty("commit", out var commit) &&
                              commit.TryGetProperty("message", out var msg) ? msg.GetString() : null,
                ["author"] = c.TryGetProperty("commit", out var c2) &&
                             c2.TryGetProperty("author", out var author) &&
                             author.TryGetProperty("name", out var authorName) ? authorName.GetString() : null,
                ["url"] = c.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() : null
            },
            UpdatedAt = c.TryGetProperty("commit", out var c3) &&
                        c3.TryGetProperty("author", out var a) &&
                        a.TryGetProperty("date", out var date) &&
                        DateTime.TryParse(date.GetString(), out var dt)
                ? dt.ToUniversalTime() : null
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListPullRequestsAsync(
        HttpClient client, string? repoPath, DataSourceQueryVO query, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(repoPath)) return [];

        var url = $"/api/v1/repos/{repoPath}/pulls";
        var queryParams = new List<string>();

        var state = query.AdditionalParams?.TryGetValue("state", out var s) == true ? s : "open";
        queryParams.Add($"state={state}");

        var limit = query.Pagination?.Limit ?? 30;
        queryParams.Add($"limit={limit}");

        url += "?" + string.Join("&", queryParams);

        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var pulls = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (pulls.ValueKind != JsonValueKind.Array) return [];

        return pulls.EnumerateArray().Select(pr => new ResourceVO
        {
            Kind = "PullRequest",
            Name = pr.TryGetProperty("number", out var num) ? $"#{num.GetInt32()}" : "",
            Status = pr.TryGetProperty("state", out var st) ? st.GetString() ?? "unknown" : "unknown",
            Properties = new Dictionary<string, object?>
            {
                ["number"] = pr.TryGetProperty("number", out var prNum) ? prNum.GetInt32() : 0,
                ["title"] = pr.TryGetProperty("title", out var title) ? title.GetString() : null,
                ["author"] = pr.TryGetProperty("user", out var user) &&
                             user.TryGetProperty("login", out var login) ? login.GetString() : null,
                ["headBranch"] = pr.TryGetProperty("head", out var head) &&
                                 head.TryGetProperty("ref", out var headRef) ? headRef.GetString() : null,
                ["baseBranch"] = pr.TryGetProperty("base", out var baseProp) &&
                                 baseProp.TryGetProperty("ref", out var baseRef) ? baseRef.GetString() : null,
                ["url"] = pr.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() : null
            },
            UpdatedAt = pr.TryGetProperty("updated_at", out var updatedAt) &&
                        DateTime.TryParse(updatedAt.GetString(), out var dt)
                ? dt.ToUniversalTime() : null
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListIssuesAsync(
        HttpClient client, string? repoPath, DataSourceQueryVO query, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(repoPath)) return [];

        var url = $"/api/v1/repos/{repoPath}/issues";
        var queryParams = new List<string>();

        var state = query.AdditionalParams?.TryGetValue("state", out var s) == true ? s : "open";
        queryParams.Add($"state={state}");
        queryParams.Add("type=issues"); // exclude pull requests

        var limit = query.Pagination?.Limit ?? 30;
        queryParams.Add($"limit={limit}");

        url += "?" + string.Join("&", queryParams);

        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var issues = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (issues.ValueKind != JsonValueKind.Array) return [];

        return issues.EnumerateArray().Select(i => new ResourceVO
        {
            Kind = "Issue",
            Name = i.TryGetProperty("number", out var num) ? $"#{num.GetInt32()}" : "",
            Status = i.TryGetProperty("state", out var st) ? st.GetString() ?? "unknown" : "unknown",
            Properties = new Dictionary<string, object?>
            {
                ["number"] = i.TryGetProperty("number", out var issueNum) ? issueNum.GetInt32() : 0,
                ["title"] = i.TryGetProperty("title", out var title) ? title.GetString() : null,
                ["author"] = i.TryGetProperty("user", out var user) &&
                             user.TryGetProperty("login", out var login) ? login.GetString() : null,
                ["url"] = i.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() : null
            },
            UpdatedAt = i.TryGetProperty("updated_at", out var updatedAt) &&
                        DateTime.TryParse(updatedAt.GetString(), out var dt)
                ? dt.ToUniversalTime() : null
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListReleasesAsync(
        HttpClient client, string? repoPath, DataSourceQueryVO query, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(repoPath)) return [];

        var limit = query.Pagination?.Limit ?? 20;
        var url = $"/api/v1/repos/{repoPath}/releases?limit={limit}";

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
                ["body"] = r.TryGetProperty("body", out var body)
                    ? (body.GetString()?.Length > 200 ? body.GetString()![..200] + "..." : body.GetString())
                    : null,
                ["draft"] = r.TryGetProperty("draft", out var draft) ? draft.GetBoolean() : false,
                ["prerelease"] = r.TryGetProperty("prerelease", out var pre) ? pre.GetBoolean() : false
            },
            UpdatedAt = r.TryGetProperty("published_at", out var pubAt) &&
                        DateTime.TryParse(pubAt.GetString(), out var dt)
                ? dt.ToUniversalTime() : null
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListReposAsync(
        HttpClient client, DataSourceRegistration registration, DataSourceQueryVO query, CancellationToken ct)
    {
        var org = registration.ConnectionConfig.Organization;
        var limit = query.Pagination?.Limit ?? 30;

        string url;
        if (!string.IsNullOrEmpty(org) && !org.Contains('/'))
        {
            url = $"/api/v1/orgs/{Uri.EscapeDataString(org)}/repos?limit={limit}";
        }
        else
        {
            url = $"/api/v1/repos/search?limit={limit}";
        }

        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var array = json.ValueKind == JsonValueKind.Array
            ? json
            : json.TryGetProperty("data", out var data) ? data : json;

        if (array.ValueKind != JsonValueKind.Array) return [];

        return array.EnumerateArray().Select(r => new ResourceVO
        {
            Kind = "Repository",
            Name = r.TryGetProperty("full_name", out var fn) ? fn.GetString() ?? "" : "",
            Status = r.TryGetProperty("archived", out var arch) && arch.GetBoolean() ? "Archived" : "Active",
            Properties = new Dictionary<string, object?>
            {
                ["description"] = r.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                ["defaultBranch"] = r.TryGetProperty("default_branch", out var branch) ? branch.GetString() : null,
                ["stars"] = r.TryGetProperty("stars_count", out var stars) ? stars.GetInt32() : 0,
                ["forks"] = r.TryGetProperty("forks_count", out var forks) ? forks.GetInt32() : 0,
                ["url"] = r.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() : null
            },
            UpdatedAt = r.TryGetProperty("updated_at", out var updatedAt) &&
                        DateTime.TryParse(updatedAt.GetString(), out var dt)
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
                // Gitea uses Authorization: token <token>
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("token", config.EncryptedCredential);
                break;
        }
    }

    private static string? GetRepoPath(DataSourceRegistration registration, DataSourceQueryVO query)
    {
        if (query.AdditionalParams?.TryGetValue("repo", out var repo) == true)
            return repo;

        if (query.AdditionalParams?.TryGetValue("project", out var project) == true)
            return project;

        var org = registration.ConnectionConfig.Organization;
        // Only return Organization if it contains '/' (i.e. owner/repo format)
        return !string.IsNullOrEmpty(org) && org.Contains('/') ? org : null;
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
