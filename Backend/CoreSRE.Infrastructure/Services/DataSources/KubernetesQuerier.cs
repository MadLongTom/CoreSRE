using System.Diagnostics;
using System.Net.Security;
using System.Security.Authentication;
using System.Text.Json;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services.DataSources;

/// <summary>
/// Kubernetes 集群资源查询器。
/// 使用 KubernetesClient SDK 查询 Pod、Deployment、Service、Namespace 等资源。
/// 支持按 Kind + Namespace + Label Selector 过滤。
/// </summary>
public class KubernetesQuerier : IDataSourceQuerier
{
    private readonly ILogger<KubernetesQuerier> _logger;

    public KubernetesQuerier(ILogger<KubernetesQuerier> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(DataSourceProduct product) =>
        product is DataSourceProduct.Kubernetes;

    public async Task<DataSourceResultVO> QueryAsync(
        DataSourceRegistration registration,
        DataSourceQueryVO query,
        CancellationToken ct = default)
    {
        using var client = CreateClient(registration);

        // Expression format: "kind=Deployment" or "kind=Pod" etc.
        var kind = ExtractKind(query);
        var ns = ExtractNamespace(query, registration);
        var labelSelector = BuildLabelSelector(query);

        var resources = kind?.ToLowerInvariant() switch
        {
            "pod" or "pods" => await ListPodsAsync(client, ns, labelSelector, query.Pagination, ct),
            "deployment" or "deployments" => await ListDeploymentsAsync(client, ns, labelSelector, query.Pagination, ct),
            "service" or "services" => await ListServicesAsync(client, ns, labelSelector, query.Pagination, ct),
            "namespace" or "namespaces" => await ListNamespacesAsync(client, labelSelector, ct),
            "node" or "nodes" => await ListNodesAsync(client, labelSelector, ct),
            "configmap" or "configmaps" => await ListConfigMapsAsync(client, ns, labelSelector, ct),
            "secret" or "secrets" => await ListSecretsAsync(client, ns, labelSelector, ct),
            "ingress" or "ingresses" => await ListIngressesAsync(client, ns, labelSelector, ct),
            "statefulset" or "statefulsets" => await ListStatefulSetsAsync(client, ns, labelSelector, query.Pagination, ct),
            "daemonset" or "daemonsets" => await ListDaemonSetsAsync(client, ns, labelSelector, ct),
            "job" or "jobs" => await ListJobsAsync(client, ns, labelSelector, ct),
            "cronjob" or "cronjobs" => await ListCronJobsAsync(client, ns, labelSelector, ct),
            "event" or "events" => await ListEventsAsync(client, ns, labelSelector, ct),
            _ => await ListDeploymentsAsync(client, ns, labelSelector, query.Pagination, ct) // Default to deployments
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
            var version = await client.Version.GetCodeAsync(ct);
            sw.Stop();

            return new DataSourceHealthVO
            {
                LastCheckAt = DateTime.UtcNow,
                IsHealthy = true,
                Version = $"{version.GitVersion}",
                ResponseTimeMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Health check failed for Kubernetes datasource {Name}", registration.Name);
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
        var namespaces = new List<string>();
        try
        {
            using var client = CreateClient(registration);
            var nsList = await client.CoreV1.ListNamespaceAsync(cancellationToken: ct);
            namespaces = nsList.Items.Select(n => n.Metadata.Name).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Metadata discovery failed for Kubernetes datasource {Name}", registration.Name);
        }

        return new DataSourceMetadataVO
        {
            DiscoveredAt = DateTime.UtcNow,
            Namespaces = namespaces,
            Services = ["Pod", "Deployment", "Service", "Namespace", "Node", "ConfigMap", "Secret", "Ingress", "StatefulSet", "DaemonSet", "Job", "CronJob", "Event"],
            AvailableFunctions = registration.GenerateAvailableFunctionNames()
        };
    }

    // ─── Resource Listing Methods ───────────────────────────────────────

    private static async Task<List<ResourceVO>> ListPodsAsync(
        k8s.Kubernetes client, string? ns, string? labelSelector, PaginationVO? pagination, CancellationToken ct)
    {
        var limit = pagination?.Limit ?? 100;
        var pods = ns is not null
            ? await client.CoreV1.ListNamespacedPodAsync(ns, labelSelector: labelSelector, limit: limit, cancellationToken: ct)
            : await client.CoreV1.ListPodForAllNamespacesAsync(labelSelector: labelSelector, limit: limit, cancellationToken: ct);

        return pods.Items.Select(p => new ResourceVO
        {
            Kind = "Pod",
            Name = p.Metadata.Name,
            Namespace = p.Metadata.NamespaceProperty,
            Status = p.Status?.Phase,
            Labels = p.Metadata.Labels?.ToDictionary(kv => kv.Key, kv => kv.Value),
            Properties = new Dictionary<string, object?>
            {
                ["nodeName"] = p.Spec?.NodeName,
                ["restartCount"] = p.Status?.ContainerStatuses?.Sum(c => c.RestartCount),
                ["containerCount"] = p.Spec?.Containers?.Count,
                ["startTime"] = p.Status?.StartTime?.ToString("o")
            },
            UpdatedAt = p.Metadata.CreationTimestamp
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListDeploymentsAsync(
        k8s.Kubernetes client, string? ns, string? labelSelector, PaginationVO? pagination, CancellationToken ct)
    {
        var limit = pagination?.Limit ?? 100;
        var deployments = ns is not null
            ? await client.AppsV1.ListNamespacedDeploymentAsync(ns, labelSelector: labelSelector, limit: limit, cancellationToken: ct)
            : await client.AppsV1.ListDeploymentForAllNamespacesAsync(labelSelector: labelSelector, limit: limit, cancellationToken: ct);

        return deployments.Items.Select(d => new ResourceVO
        {
            Kind = "Deployment",
            Name = d.Metadata.Name,
            Namespace = d.Metadata.NamespaceProperty,
            Status = d.Status?.AvailableReplicas == d.Status?.Replicas ? "Available" : "Progressing",
            Labels = d.Metadata.Labels?.ToDictionary(kv => kv.Key, kv => kv.Value),
            Properties = new Dictionary<string, object?>
            {
                ["replicas"] = d.Status?.Replicas,
                ["availableReplicas"] = d.Status?.AvailableReplicas,
                ["readyReplicas"] = d.Status?.ReadyReplicas,
                ["updatedReplicas"] = d.Status?.UpdatedReplicas,
                ["image"] = d.Spec?.Template?.Spec?.Containers?.FirstOrDefault()?.Image
            },
            UpdatedAt = d.Metadata.CreationTimestamp
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListServicesAsync(
        k8s.Kubernetes client, string? ns, string? labelSelector, PaginationVO? pagination, CancellationToken ct)
    {
        var limit = pagination?.Limit ?? 100;
        var services = ns is not null
            ? await client.CoreV1.ListNamespacedServiceAsync(ns, labelSelector: labelSelector, limit: limit, cancellationToken: ct)
            : await client.CoreV1.ListServiceForAllNamespacesAsync(labelSelector: labelSelector, limit: limit, cancellationToken: ct);

        return services.Items.Select(s => new ResourceVO
        {
            Kind = "Service",
            Name = s.Metadata.Name,
            Namespace = s.Metadata.NamespaceProperty,
            Status = s.Spec?.Type,
            Labels = s.Metadata.Labels?.ToDictionary(kv => kv.Key, kv => kv.Value),
            Properties = new Dictionary<string, object?>
            {
                ["type"] = s.Spec?.Type,
                ["clusterIP"] = s.Spec?.ClusterIP,
                ["ports"] = s.Spec?.Ports?.Select(p => $"{p.Port}/{p.Protocol}").ToList()
            },
            UpdatedAt = s.Metadata.CreationTimestamp
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListNamespacesAsync(
        k8s.Kubernetes client, string? labelSelector, CancellationToken ct)
    {
        var nsList = await client.CoreV1.ListNamespaceAsync(labelSelector: labelSelector, cancellationToken: ct);

        return nsList.Items.Select(n => new ResourceVO
        {
            Kind = "Namespace",
            Name = n.Metadata.Name,
            Status = n.Status?.Phase,
            Labels = n.Metadata.Labels?.ToDictionary(kv => kv.Key, kv => kv.Value),
            UpdatedAt = n.Metadata.CreationTimestamp
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListNodesAsync(
        k8s.Kubernetes client, string? labelSelector, CancellationToken ct)
    {
        var nodes = await client.CoreV1.ListNodeAsync(labelSelector: labelSelector, cancellationToken: ct);

        return nodes.Items.Select(n =>
        {
            var readyCondition = n.Status?.Conditions?.FirstOrDefault(c => c.Type == "Ready");
            return new ResourceVO
            {
                Kind = "Node",
                Name = n.Metadata.Name,
                Status = readyCondition?.Status == "True" ? "Ready" : "NotReady",
                Labels = n.Metadata.Labels?.ToDictionary(kv => kv.Key, kv => kv.Value),
                Properties = new Dictionary<string, object?>
                {
                    ["kubeletVersion"] = n.Status?.NodeInfo?.KubeletVersion,
                    ["osImage"] = n.Status?.NodeInfo?.OsImage,
                    ["architecture"] = n.Status?.NodeInfo?.Architecture,
                    ["cpu"] = n.Status?.Capacity?.TryGetValue("cpu", out var cpu) == true ? cpu.ToString() : null,
                    ["memory"] = n.Status?.Capacity?.TryGetValue("memory", out var mem) == true ? mem.ToString() : null
                },
                UpdatedAt = n.Metadata.CreationTimestamp
            };
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListConfigMapsAsync(
        k8s.Kubernetes client, string? ns, string? labelSelector, CancellationToken ct)
    {
        var cms = ns is not null
            ? await client.CoreV1.ListNamespacedConfigMapAsync(ns, labelSelector: labelSelector, cancellationToken: ct)
            : await client.CoreV1.ListConfigMapForAllNamespacesAsync(labelSelector: labelSelector, cancellationToken: ct);

        return cms.Items.Select(c => new ResourceVO
        {
            Kind = "ConfigMap",
            Name = c.Metadata.Name,
            Namespace = c.Metadata.NamespaceProperty,
            Labels = c.Metadata.Labels?.ToDictionary(kv => kv.Key, kv => kv.Value),
            Properties = new Dictionary<string, object?>
            {
                ["dataKeys"] = c.Data?.Keys.ToList()
            },
            UpdatedAt = c.Metadata.CreationTimestamp
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListSecretsAsync(
        k8s.Kubernetes client, string? ns, string? labelSelector, CancellationToken ct)
    {
        var secrets = ns is not null
            ? await client.CoreV1.ListNamespacedSecretAsync(ns, labelSelector: labelSelector, cancellationToken: ct)
            : await client.CoreV1.ListSecretForAllNamespacesAsync(labelSelector: labelSelector, cancellationToken: ct);

        return secrets.Items.Select(s => new ResourceVO
        {
            Kind = "Secret",
            Name = s.Metadata.Name,
            Namespace = s.Metadata.NamespaceProperty,
            Status = s.Type,
            Labels = s.Metadata.Labels?.ToDictionary(kv => kv.Key, kv => kv.Value),
            Properties = new Dictionary<string, object?>
            {
                ["type"] = s.Type,
                ["dataKeys"] = s.Data?.Keys.ToList() // Only keys, never values
            },
            UpdatedAt = s.Metadata.CreationTimestamp
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListIngressesAsync(
        k8s.Kubernetes client, string? ns, string? labelSelector, CancellationToken ct)
    {
        var ingresses = ns is not null
            ? await client.NetworkingV1.ListNamespacedIngressAsync(ns, labelSelector: labelSelector, cancellationToken: ct)
            : await client.NetworkingV1.ListIngressForAllNamespacesAsync(labelSelector: labelSelector, cancellationToken: ct);

        return ingresses.Items.Select(i => new ResourceVO
        {
            Kind = "Ingress",
            Name = i.Metadata.Name,
            Namespace = i.Metadata.NamespaceProperty,
            Labels = i.Metadata.Labels?.ToDictionary(kv => kv.Key, kv => kv.Value),
            Properties = new Dictionary<string, object?>
            {
                ["hosts"] = i.Spec?.Rules?.Select(r => r.Host).ToList(),
                ["ingressClassName"] = i.Spec?.IngressClassName
            },
            UpdatedAt = i.Metadata.CreationTimestamp
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListStatefulSetsAsync(
        k8s.Kubernetes client, string? ns, string? labelSelector, PaginationVO? pagination, CancellationToken ct)
    {
        var limit = pagination?.Limit ?? 100;
        var sets = ns is not null
            ? await client.AppsV1.ListNamespacedStatefulSetAsync(ns, labelSelector: labelSelector, limit: limit, cancellationToken: ct)
            : await client.AppsV1.ListStatefulSetForAllNamespacesAsync(labelSelector: labelSelector, limit: limit, cancellationToken: ct);

        return sets.Items.Select(s => new ResourceVO
        {
            Kind = "StatefulSet",
            Name = s.Metadata.Name,
            Namespace = s.Metadata.NamespaceProperty,
            Status = s.Status?.ReadyReplicas == s.Status?.Replicas ? "Ready" : "NotReady",
            Labels = s.Metadata.Labels?.ToDictionary(kv => kv.Key, kv => kv.Value),
            Properties = new Dictionary<string, object?>
            {
                ["replicas"] = s.Status?.Replicas,
                ["readyReplicas"] = s.Status?.ReadyReplicas
            },
            UpdatedAt = s.Metadata.CreationTimestamp
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListDaemonSetsAsync(
        k8s.Kubernetes client, string? ns, string? labelSelector, CancellationToken ct)
    {
        var sets = ns is not null
            ? await client.AppsV1.ListNamespacedDaemonSetAsync(ns, labelSelector: labelSelector, cancellationToken: ct)
            : await client.AppsV1.ListDaemonSetForAllNamespacesAsync(labelSelector: labelSelector, cancellationToken: ct);

        return sets.Items.Select(d => new ResourceVO
        {
            Kind = "DaemonSet",
            Name = d.Metadata.Name,
            Namespace = d.Metadata.NamespaceProperty,
            Status = d.Status?.NumberReady == d.Status?.DesiredNumberScheduled ? "Ready" : "NotReady",
            Labels = d.Metadata.Labels?.ToDictionary(kv => kv.Key, kv => kv.Value),
            Properties = new Dictionary<string, object?>
            {
                ["desiredNumberScheduled"] = d.Status?.DesiredNumberScheduled,
                ["numberReady"] = d.Status?.NumberReady
            },
            UpdatedAt = d.Metadata.CreationTimestamp
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListJobsAsync(
        k8s.Kubernetes client, string? ns, string? labelSelector, CancellationToken ct)
    {
        var jobs = ns is not null
            ? await client.BatchV1.ListNamespacedJobAsync(ns, labelSelector: labelSelector, cancellationToken: ct)
            : await client.BatchV1.ListJobForAllNamespacesAsync(labelSelector: labelSelector, cancellationToken: ct);

        return jobs.Items.Select(j => new ResourceVO
        {
            Kind = "Job",
            Name = j.Metadata.Name,
            Namespace = j.Metadata.NamespaceProperty,
            Status = j.Status?.Succeeded > 0 ? "Succeeded" : j.Status?.Failed > 0 ? "Failed" : "Running",
            Labels = j.Metadata.Labels?.ToDictionary(kv => kv.Key, kv => kv.Value),
            Properties = new Dictionary<string, object?>
            {
                ["completions"] = j.Spec?.Completions,
                ["succeeded"] = j.Status?.Succeeded,
                ["failed"] = j.Status?.Failed,
                ["startTime"] = j.Status?.StartTime?.ToString("o"),
                ["completionTime"] = j.Status?.CompletionTime?.ToString("o")
            },
            UpdatedAt = j.Metadata.CreationTimestamp
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListCronJobsAsync(
        k8s.Kubernetes client, string? ns, string? labelSelector, CancellationToken ct)
    {
        var cronJobs = ns is not null
            ? await client.BatchV1.ListNamespacedCronJobAsync(ns, labelSelector: labelSelector, cancellationToken: ct)
            : await client.BatchV1.ListCronJobForAllNamespacesAsync(labelSelector: labelSelector, cancellationToken: ct);

        return cronJobs.Items.Select(c => new ResourceVO
        {
            Kind = "CronJob",
            Name = c.Metadata.Name,
            Namespace = c.Metadata.NamespaceProperty,
            Status = c.Spec?.Suspend == true ? "Suspended" : "Active",
            Labels = c.Metadata.Labels?.ToDictionary(kv => kv.Key, kv => kv.Value),
            Properties = new Dictionary<string, object?>
            {
                ["schedule"] = c.Spec?.Schedule,
                ["lastScheduleTime"] = c.Status?.LastScheduleTime?.ToString("o"),
                ["suspend"] = c.Spec?.Suspend
            },
            UpdatedAt = c.Metadata.CreationTimestamp
        }).ToList();
    }

    private static async Task<List<ResourceVO>> ListEventsAsync(
        k8s.Kubernetes client, string? ns, string? labelSelector, CancellationToken ct)
    {
        var events = ns is not null
            ? await client.CoreV1.ListNamespacedEventAsync(ns, labelSelector: labelSelector, cancellationToken: ct)
            : await client.CoreV1.ListEventForAllNamespacesAsync(labelSelector: labelSelector, cancellationToken: ct);

        return events.Items
            .OrderByDescending(e => e.LastTimestamp ?? e.Metadata.CreationTimestamp)
            .Take(100)
            .Select(e => new ResourceVO
            {
                Kind = "Event",
                Name = e.Metadata.Name,
                Namespace = e.Metadata.NamespaceProperty,
                Status = e.Type, // Normal / Warning
                Properties = new Dictionary<string, object?>
                {
                    ["reason"] = e.Reason,
                    ["message"] = e.Message,
                    ["involvedObject"] = $"{e.InvolvedObject?.Kind}/{e.InvolvedObject?.Name}",
                    ["count"] = e.Count,
                    ["source"] = e.Source?.Component
                },
                UpdatedAt = e.LastTimestamp ?? e.Metadata.CreationTimestamp
            }).ToList();
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private k8s.Kubernetes CreateClient(DataSourceRegistration registration)
    {
        KubernetesClientConfiguration config;

        if (!string.IsNullOrEmpty(registration.ConnectionConfig.KubeConfig))
        {
            // Use explicit kubeconfig from connection config (Base64-encoded)
            var kubeConfigBytes = Convert.FromBase64String(registration.ConnectionConfig.KubeConfig);
            using var stream = new MemoryStream(kubeConfigBytes);
            config = KubernetesClientConfiguration.BuildConfigFromConfigFile(stream);
        }
        else if (!string.IsNullOrEmpty(registration.ConnectionConfig.BaseUrl)
                 && registration.ConnectionConfig.BaseUrl != "https://kubernetes.default.svc")
        {
            if (!string.IsNullOrEmpty(registration.ConnectionConfig.EncryptedCredential))
            {
                // BaseUrl + explicit token auth (remote clusters)
                config = new KubernetesClientConfiguration
                {
                    Host = registration.ConnectionConfig.BaseUrl,
                    AccessToken = registration.ConnectionConfig.EncryptedCredential,
                };
            }
            else
            {
                // BaseUrl provided but no token — load default kubeconfig and override host
                // (e.g. Docker Desktop with client-cert auth)
                config = KubernetesClientConfiguration.BuildDefaultConfig();
                config.Host = registration.ConnectionConfig.BaseUrl;
            }
        }
        else
        {
            // Use default (in-cluster or local kubeconfig)
            config = KubernetesClientConfiguration.BuildDefaultConfig();
        }

        // Apply TLS settings from registration
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

    private static string? ExtractKind(DataSourceQueryVO query)
    {
        // Check Expression field: "kind=Deployment", "Pod", "Deployment"
        if (!string.IsNullOrEmpty(query.Expression))
        {
            var expr = query.Expression.Trim();
            if (expr.StartsWith("kind=", StringComparison.OrdinalIgnoreCase))
                return expr["kind=".Length..].Trim();
            return expr; // Assume the expression IS the kind name
        }

        // Check AdditionalParams
        if (query.AdditionalParams?.TryGetValue("kind", out var kind) == true)
            return kind;

        return null;
    }

    private static string? ExtractNamespace(DataSourceQueryVO query, DataSourceRegistration registration)
    {
        // Check Filters first
        var nsFilter = query.Filters?.FirstOrDefault(f =>
            f.Key.Equals("namespace", StringComparison.OrdinalIgnoreCase));
        if (nsFilter is not null)
            return nsFilter.Value;

        // Check AdditionalParams
        if (query.AdditionalParams?.TryGetValue("namespace", out var ns) == true)
            return ns;

        // Fallback to connection config
        return registration.ConnectionConfig.Namespace;
    }

    private static string? BuildLabelSelector(DataSourceQueryVO query)
    {
        if (query.Filters is null or { Count: 0 })
            return null;

        var labelFilters = query.Filters
            .Where(f => !f.Key.Equals("namespace", StringComparison.OrdinalIgnoreCase)
                     && !f.Key.Equals("kind", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Operator switch
            {
                LabelOperator.Eq => $"{f.Key}={f.Value}",
                LabelOperator.Neq => $"{f.Key}!={f.Value}",
                _ => $"{f.Key}={f.Value}"
            })
            .ToList();

        return labelFilters.Count > 0 ? string.Join(",", labelFilters) : null;
    }
}
