using System.Diagnostics;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Octokit;

namespace CoreSRE.Infrastructure.Services.DataSources;

/// <summary>
/// GitHub 查询器。使用 Octokit SDK 查询仓库、Commit、Pull Request、Actions 等资源。
/// Organization 字段格式："owner/repo" 或 "owner"（列出所有仓库）。
/// </summary>
public class GitHubQuerier : IDataSourceQuerier
{
    private readonly ILogger<GitHubQuerier> _logger;

    public GitHubQuerier(ILogger<GitHubQuerier> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(DataSourceProduct product) =>
        product is DataSourceProduct.GitHub;

    public async Task<DataSourceResultVO> QueryAsync(
        DataSourceRegistration registration,
        DataSourceQueryVO query,
        CancellationToken ct = default)
    {
        var client = CreateClient(registration);
        var (owner, repo) = ParseOwnerRepo(registration, query);

        // Determine resource kind from Expression or AdditionalParams
        var kind = ExtractKind(query);

        var resources = kind?.ToLowerInvariant() switch
        {
            "commit" or "commits" => await ListCommitsAsync(client, owner, repo, query, ct),
            "pullrequest" or "pull_request" or "pr" or "prs" => await ListPullRequestsAsync(client, owner, repo, query, ct),
            "workflow" or "workflows" or "pipeline" or "pipelines" => await ListWorkflowRunsAsync(client, owner, repo, query, ct),
            "issue" or "issues" => await ListIssuesAsync(client, owner, repo, query, ct),
            "release" or "releases" => await ListReleasesAsync(client, owner, repo, query, ct),
            "repository" or "repositories" or "repo" or "repos" => await ListRepositoriesAsync(client, owner, ct),
            _ => await ListCommitsAsync(client, owner, repo, query, ct) // Default to commits
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
        var sw = Stopwatch.StartNew();
        try
        {
            var client = CreateClient(registration);
            var rateLimit = await client.RateLimit.GetRateLimits();
            sw.Stop();

            return new DataSourceHealthVO
            {
                LastCheckAt = DateTime.UtcNow,
                IsHealthy = true,
                Version = $"Rate limit: {rateLimit.Resources.Core.Remaining}/{rateLimit.Resources.Core.Limit}",
                ResponseTimeMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Health check failed for GitHub datasource {Name}", registration.Name);
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
        var services = new List<string>(); // Repository list

        try
        {
            var org = registration.ConnectionConfig.Organization;
            if (!string.IsNullOrEmpty(org) && !org.Contains('/'))
            {
                // List organization repos
                var repos = await client.Repository.GetAllForOrg(org);
                services = repos.Select(r => r.FullName).ToList();
            }
            else if (!string.IsNullOrEmpty(org) && org.Contains('/'))
            {
                // Single repo — list branches as services
                var (owner, repo) = ParseOwnerRepoFromOrg(org);
                var branches = await client.Repository.Branch.GetAll(owner, repo);
                services = branches.Select(b => b.Name).ToList();
            }
            else
            {
                // List current user repos
                var repos = await client.Repository.GetAllForCurrent();
                services = repos.Select(r => r.FullName).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Metadata discovery failed for GitHub datasource {Name}", registration.Name);
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
        GitHubClient client, string owner, string? repo, DataSourceQueryVO query, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(repo))
            return [];

        var request = new CommitRequest();

        // Apply time range
        if (query.TimeRange is not null)
        {
            request.Since = query.TimeRange.Start;
            request.Until = query.TimeRange.End;
        }

        // Apply branch filter
        if (query.AdditionalParams?.TryGetValue("branch", out var branch) == true)
            request.Sha = branch;

        var options = new ApiOptions { PageSize = query.Pagination?.Limit ?? 30, PageCount = 1 };
        var commits = await client.Repository.Commit.GetAll(owner, repo, request, options);

        return commits.Select(c => new ResourceVO
        {
            Kind = "Commit",
            Name = c.Sha[..7],
            Status = c.Commit.Verification?.Verified == true ? "Verified" : "Unverified",
            Labels = new Dictionary<string, string>
            {
                ["branch"] = request.Sha ?? "main"
            },
            Properties = new Dictionary<string, object?>
            {
                ["sha"] = c.Sha,
                ["message"] = c.Commit.Message?.Split('\n').FirstOrDefault(),
                ["author"] = c.Commit.Author?.Name,
                ["authorEmail"] = c.Commit.Author?.Email,
                ["url"] = c.HtmlUrl
            },
            UpdatedAt = c.Commit.Author?.Date.UtcDateTime
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListPullRequestsAsync(
        GitHubClient client, string owner, string? repo, DataSourceQueryVO query, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(repo))
            return [];

        var stateFilter = ItemStateFilter.All;
        if (query.AdditionalParams?.TryGetValue("state", out var state) == true)
        {
            stateFilter = state.ToLowerInvariant() switch
            {
                "open" => ItemStateFilter.Open,
                "closed" => ItemStateFilter.Closed,
                _ => ItemStateFilter.All
            };
        }

        var request = new PullRequestRequest { State = stateFilter };
        var options = new ApiOptions { PageSize = query.Pagination?.Limit ?? 30, PageCount = 1 };
        var prs = await client.PullRequest.GetAllForRepository(owner, repo, request, options);

        return prs.Select(pr => new ResourceVO
        {
            Kind = "PullRequest",
            Name = $"#{pr.Number}",
            Status = pr.Merged ? "Merged" : pr.State.StringValue,
            Labels = pr.Labels?.ToDictionary(l => l.Name, l => l.Color) ?? new Dictionary<string, string>(),
            Properties = new Dictionary<string, object?>
            {
                ["number"] = pr.Number,
                ["title"] = pr.Title,
                ["author"] = pr.User?.Login,
                ["baseBranch"] = pr.Base?.Ref,
                ["headBranch"] = pr.Head?.Ref,
                ["url"] = pr.HtmlUrl,
                ["additions"] = pr.Additions,
                ["deletions"] = pr.Deletions,
                ["changedFiles"] = pr.ChangedFiles
            },
            UpdatedAt = pr.UpdatedAt.UtcDateTime
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListWorkflowRunsAsync(
        GitHubClient client, string owner, string? repo, DataSourceQueryVO query, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(repo))
            return [];

        var request = new WorkflowRunsRequest();

        if (query.AdditionalParams?.TryGetValue("status", out var status) == true)
        {
            if (Enum.TryParse<CheckRunStatusFilter>(status, ignoreCase: true, out var statusFilter))
                request.Status = statusFilter;
        }

        if (query.AdditionalParams?.TryGetValue("branch", out var branch) == true)
            request.Branch = branch;

        var runs = await client.Actions.Workflows.Runs.List(owner, repo, request);

        var limit = query.Pagination?.Limit ?? 30;
        return runs.WorkflowRuns.Take(limit).Select(r => new ResourceVO
        {
            Kind = "WorkflowRun",
            Name = $"#{r.RunNumber}",
            Status = r.Conclusion.HasValue ? r.Conclusion.Value.StringValue : r.Status.StringValue,
            Properties = new Dictionary<string, object?>
            {
                ["workflowName"] = r.Name,
                ["runNumber"] = r.RunNumber,
                ["event"] = r.Event,
                ["branch"] = r.HeadBranch,
                ["commitSha"] = r.HeadSha?[..7],
                ["url"] = r.HtmlUrl,
                ["conclusion"] = r.Conclusion?.StringValue
            },
            UpdatedAt = r.UpdatedAt.UtcDateTime
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListIssuesAsync(
        GitHubClient client, string owner, string? repo, DataSourceQueryVO query, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(repo))
            return [];

        var stateFilter = ItemStateFilter.Open;
        if (query.AdditionalParams?.TryGetValue("state", out var state) == true)
        {
            stateFilter = state.ToLowerInvariant() switch
            {
                "closed" => ItemStateFilter.Closed,
                "all" => ItemStateFilter.All,
                _ => ItemStateFilter.Open
            };
        }

        var request = new RepositoryIssueRequest { State = stateFilter };
        var options = new ApiOptions { PageSize = query.Pagination?.Limit ?? 30, PageCount = 1 };
        var issues = await client.Issue.GetAllForRepository(owner, repo, request, options);

        // Filter out pull requests (GitHub API returns PRs as issues too)
        return issues.Where(i => i.PullRequest == null).Select(i => new ResourceVO
        {
            Kind = "Issue",
            Name = $"#{i.Number}",
            Status = i.State.StringValue,
            Labels = i.Labels?.ToDictionary(l => l.Name, l => l.Color) ?? new Dictionary<string, string>(),
            Properties = new Dictionary<string, object?>
            {
                ["number"] = i.Number,
                ["title"] = i.Title,
                ["author"] = i.User?.Login,
                ["assignees"] = i.Assignees?.Select(a => a.Login).ToList(),
                ["url"] = i.HtmlUrl,
                ["comments"] = i.Comments
            },
            UpdatedAt = i.UpdatedAt?.UtcDateTime ?? i.CreatedAt.UtcDateTime
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListReleasesAsync(
        GitHubClient client, string owner, string? repo, DataSourceQueryVO query, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(repo))
            return [];

        var options = new ApiOptions { PageSize = query.Pagination?.Limit ?? 20, PageCount = 1 };
        var releases = await client.Repository.Release.GetAll(owner, repo, options);

        return releases.Select(r => new ResourceVO
        {
            Kind = "Release",
            Name = r.TagName,
            Status = r.Prerelease ? "Prerelease" : r.Draft ? "Draft" : "Published",
            Properties = new Dictionary<string, object?>
            {
                ["name"] = r.Name,
                ["tagName"] = r.TagName,
                ["author"] = r.Author?.Login,
                ["url"] = r.HtmlUrl,
                ["body"] = r.Body?.Length > 200 ? r.Body[..200] + "..." : r.Body,
                ["assetsCount"] = r.Assets?.Count
            },
            UpdatedAt = r.PublishedAt?.UtcDateTime ?? r.CreatedAt.UtcDateTime
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListRepositoriesAsync(
        GitHubClient client, string owner, CancellationToken ct)
    {
        IReadOnlyList<Repository> repos;
        try
        {
            repos = await client.Repository.GetAllForOrg(owner);
        }
        catch
        {
            // Fallback: treat as user
            repos = await client.Repository.GetAllForUser(owner);
        }

        return repos.Select(r => new ResourceVO
        {
            Kind = "Repository",
            Name = r.FullName,
            Status = r.Archived ? "Archived" : r.Private ? "Private" : "Public",
            Labels = r.Topics?.ToDictionary(t => t, _ => "topic"),
            Properties = new Dictionary<string, object?>
            {
                ["description"] = r.Description,
                ["language"] = r.Language,
                ["stars"] = r.StargazersCount,
                ["forks"] = r.ForksCount,
                ["openIssues"] = r.OpenIssuesCount,
                ["defaultBranch"] = r.DefaultBranch,
                ["url"] = r.HtmlUrl
            },
            UpdatedAt = r.UpdatedAt.UtcDateTime
        }).ToList();
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static GitHubClient CreateClient(DataSourceRegistration registration)
    {
        var client = new GitHubClient(new ProductHeaderValue("CoreSRE"));

        // Set base address for GitHub Enterprise
        if (!string.IsNullOrEmpty(registration.ConnectionConfig.BaseUrl) &&
            !registration.ConnectionConfig.BaseUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase))
        {
            client = new GitHubClient(new ProductHeaderValue("CoreSRE"),
                new Uri(registration.ConnectionConfig.BaseUrl));
        }

        // Apply auth
        if (!string.IsNullOrEmpty(registration.ConnectionConfig.EncryptedCredential))
        {
            switch (registration.ConnectionConfig.AuthType)
            {
                case "Bearer":
                    client.Credentials = new Credentials(registration.ConnectionConfig.EncryptedCredential, AuthenticationType.Bearer);
                    break;
                case "ApiKey":
                    client.Credentials = new Credentials(registration.ConnectionConfig.EncryptedCredential);
                    break;
                default:
                    client.Credentials = new Credentials(registration.ConnectionConfig.EncryptedCredential);
                    break;
            }
        }

        return client;
    }

    private static (string owner, string? repo) ParseOwnerRepo(DataSourceRegistration registration, DataSourceQueryVO query)
    {
        // Check AdditionalParams for explicit repo
        if (query.AdditionalParams?.TryGetValue("repo", out var repoParam) == true &&
            repoParam.Contains('/'))
        {
            return ParseOwnerRepoFromOrg(repoParam);
        }

        // Use Organization from connection config
        var org = registration.ConnectionConfig.Organization;
        if (!string.IsNullOrEmpty(org))
            return ParseOwnerRepoFromOrg(org);

        return ("", null);
    }

    private static (string owner, string? repo) ParseOwnerRepoFromOrg(string orgString)
    {
        var parts = orgString.Split('/', 2);
        return parts.Length >= 2 ? (parts[0], parts[1]) : (parts[0], null);
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
