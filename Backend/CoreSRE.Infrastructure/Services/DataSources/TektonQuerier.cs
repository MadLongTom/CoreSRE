using System.Diagnostics;
using System.Net.Security;
using System.Text.Json;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;
using k8s;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services.DataSources;

/// <summary>
/// Tekton 查询器。通过 Kubernetes API (CustomObjects) 查询 Tekton CRD 资源（PipelineRun、TaskRun、Pipeline、Task）。
/// 使用 ConnectionConfig.Namespace 指定 Tekton 管线所在命名空间。
/// </summary>
public class TektonQuerier : IDataSourceQuerier
{
    private const string TektonGroup = "tekton.dev";
    private const string TektonVersion = "v1";

    private readonly ILogger<TektonQuerier> _logger;

    public TektonQuerier(ILogger<TektonQuerier> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(DataSourceProduct product) =>
        product is DataSourceProduct.Tekton;

    public async Task<DataSourceResultVO> QueryAsync(
        DataSourceRegistration registration,
        DataSourceQueryVO query,
        CancellationToken ct = default)
    {
        using var client = CreateClient(registration);
        var ns = ExtractNamespace(query, registration);
        var kind = ExtractKind(query);

        var resources = kind?.ToLowerInvariant() switch
        {
            "pipelinerun" or "pipelineruns" => await ListCrdResourcesAsync(client, "pipelineruns", "PipelineRun", ns, query, ct),
            "taskrun" or "taskruns" => await ListCrdResourcesAsync(client, "taskruns", "TaskRun", ns, query, ct),
            "pipeline" or "pipelines" => await ListCrdResourcesAsync(client, "pipelines", "Pipeline", ns, query, ct),
            "task" or "tasks" => await ListCrdResourcesAsync(client, "tasks", "Task", ns, query, ct),
            _ => await ListCrdResourcesAsync(client, "pipelineruns", "PipelineRun", ns, query, ct)
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
            using var client = CreateClient(registration);
            // Step 1: verify K8s API server is reachable
            var version = await client.Version.GetCodeAsync(ct);

            // Step 2: check if Tekton CRDs are installed by listing API resources
            string tektonStatus;
            try
            {
                var ns = registration.ConnectionConfig.Namespace ?? "default";
                await client.CustomObjects.ListNamespacedCustomObjectAsync(
                    TektonGroup, TektonVersion, ns, "pipelineruns",
                    limit: 1, cancellationToken: ct);
                tektonStatus = "Tekton CRDs installed";
            }
            catch
            {
                tektonStatus = "Connected (Tekton CRDs not installed)";
            }

            sw.Stop();
            return new DataSourceHealthVO
            {
                LastCheckAt = DateTime.UtcNow,
                IsHealthy = true,
                Version = $"{version.GitVersion} — {tektonStatus}",
                ResponseTimeMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Health check failed for Tekton datasource {Name}", registration.Name);
            return new DataSourceHealthVO
            {
                LastCheckAt = DateTime.UtcNow,
                IsHealthy = false,
                ErrorMessage = $"连接测试失败: {ex.Message}",
                ResponseTimeMs = (int)sw.ElapsedMilliseconds
            };
        }
    }

    public Task<DataSourceMetadataVO> DiscoverMetadataAsync(
        DataSourceRegistration registration,
        CancellationToken ct = default)
    {
        return Task.FromResult(new DataSourceMetadataVO
        {
            DiscoveredAt = DateTime.UtcNow,
            Services = ["PipelineRun", "TaskRun", "Pipeline", "Task"],
            AvailableFunctions = registration.GenerateAvailableFunctionNames()
        });
    }

    // ─── Resource Listing via CustomObjects API ─────────────────────────

    private async Task<List<ResourceVO>> ListCrdResourcesAsync(
        k8s.Kubernetes client, string plural, string kind, string? ns,
        DataSourceQueryVO query, CancellationToken ct)
    {
        var limit = query.Pagination?.Limit ?? 30;

        try
        {
            object result;
            if (ns is not null)
            {
                result = await client.CustomObjects.ListNamespacedCustomObjectAsync(
                    TektonGroup, TektonVersion, ns, plural, cancellationToken: ct);
            }
            else
            {
                result = await client.CustomObjects.ListClusterCustomObjectAsync(
                    TektonGroup, TektonVersion, plural, cancellationToken: ct);
            }

            var json = JsonSerializer.Serialize(result);
            return ParseK8sResourceList(json, kind, limit);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list Tekton {Kind} resources in namespace {Namespace}", kind, ns);
            return [];
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private List<ResourceVO> ParseK8sResourceList(string json, string kind, int limit)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return [];

            return items.EnumerateArray()
                .Take(limit)
                .Select(item => ParseSingleResource(item, kind))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse Tekton API response for {Kind}", kind);
            return [];
        }
    }

    private static ResourceVO ParseSingleResource(JsonElement item, string kind)
    {
        var metadata = item.TryGetProperty("metadata", out var m) ? m : default;
        var status = item.TryGetProperty("status", out var s) ? s : default;

        var name = metadata.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var itemNs = metadata.TryGetProperty("namespace", out var nsVal) ? nsVal.GetString() : null;

        // Extract status from conditions
        string? statusStr = null;
        if (status.ValueKind == JsonValueKind.Object &&
            status.TryGetProperty("conditions", out var conditions) &&
            conditions.ValueKind == JsonValueKind.Array)
        {
            var succeededCondition = conditions.EnumerateArray()
                .FirstOrDefault(c => c.TryGetProperty("type", out var t) && t.GetString() == "Succeeded");

            if (succeededCondition.ValueKind == JsonValueKind.Object)
            {
                var condStatus = succeededCondition.TryGetProperty("status", out var cs) ? cs.GetString() : null;
                var reason = succeededCondition.TryGetProperty("reason", out var r) ? r.GetString() : null;
                statusStr = condStatus == "True" ? "Succeeded"
                    : condStatus == "False" ? reason ?? "Failed"
                    : reason ?? "Running";
            }
        }

        var props = new Dictionary<string, object?>();

        // PipelineRun-specific: pipelineRef
        if (item.TryGetProperty("spec", out var spec))
        {
            if (spec.TryGetProperty("pipelineRef", out var pipelineRef) &&
                pipelineRef.TryGetProperty("name", out var pipelineName))
                props["pipelineRef"] = pipelineName.GetString();

            if (spec.TryGetProperty("taskRef", out var taskRef) &&
                taskRef.TryGetProperty("name", out var taskName))
                props["taskRef"] = taskName.GetString();
        }

        // Start time / completion time
        if (status.ValueKind == JsonValueKind.Object)
        {
            if (status.TryGetProperty("startTime", out var startTime))
                props["startTime"] = startTime.GetString();
            if (status.TryGetProperty("completionTime", out var completionTime))
                props["completionTime"] = completionTime.GetString();
        }

        // Labels
        Dictionary<string, string>? labels = null;
        if (metadata.TryGetProperty("labels", out var lbls) && lbls.ValueKind == JsonValueKind.Object)
        {
            labels = lbls.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
        }

        DateTime? updatedAt = null;
        if (metadata.TryGetProperty("creationTimestamp", out var created) &&
            DateTime.TryParse(created.GetString(), out var dt))
        {
            updatedAt = dt.ToUniversalTime();
        }

        return new ResourceVO
        {
            Kind = kind,
            Name = name,
            Namespace = itemNs,
            Status = statusStr,
            Labels = labels,
            Properties = props.Count > 0 ? props : null,
            UpdatedAt = updatedAt
        };
    }

    private static k8s.Kubernetes CreateClient(DataSourceRegistration registration)
    {
        KubernetesClientConfiguration config;

        if (!string.IsNullOrEmpty(registration.ConnectionConfig.KubeConfig))
        {
            var kubeConfigBytes = Convert.FromBase64String(registration.ConnectionConfig.KubeConfig);
            using var stream = new MemoryStream(kubeConfigBytes);
            config = KubernetesClientConfiguration.BuildConfigFromConfigFile(stream);
        }
        else if (!string.IsNullOrEmpty(registration.ConnectionConfig.BaseUrl)
                 && registration.ConnectionConfig.BaseUrl != "https://kubernetes.default.svc")
        {
            if (!string.IsNullOrEmpty(registration.ConnectionConfig.EncryptedCredential))
            {
                config = new KubernetesClientConfiguration
                {
                    Host = registration.ConnectionConfig.BaseUrl,
                    AccessToken = registration.ConnectionConfig.EncryptedCredential,
                };
            }
            else
            {
                config = KubernetesClientConfiguration.BuildDefaultConfig();
                config.Host = registration.ConnectionConfig.BaseUrl;
            }
        }
        else
        {
            config = KubernetesClientConfiguration.BuildDefaultConfig();
        }

        config.SkipTlsVerify = registration.ConnectionConfig.TlsSkipVerify;

        if (registration.ConnectionConfig.TlsSkipVerify)
        {
            config.FirstMessageHandlerSetup = handler =>
            {
                handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            };
        }

        return new k8s.Kubernetes(config);
    }

    private static string? ExtractNamespace(DataSourceQueryVO query, DataSourceRegistration registration)
    {
        if (query.Filters?.FirstOrDefault(f => f.Key == "namespace") is { } nsFilter)
            return nsFilter.Value;

        if (query.AdditionalParams?.TryGetValue("namespace", out var ns) == true)
            return ns;

        return registration.ConnectionConfig.Namespace;
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
