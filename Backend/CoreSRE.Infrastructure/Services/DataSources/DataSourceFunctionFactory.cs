using System.ComponentModel;
using System.Text.Json;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services.DataSources;

/// <summary>
/// 将 DataSourceRefVO 列表转换为可调用的 AIFunction 集合。
/// 按 Category 为每个数据源生成标准 AIFunction（query_metrics_{name}、query_logs_{name} 等），
/// 并根据 EnabledFunctions 过滤暴露的函数子集。
/// </summary>
public sealed class DataSourceFunctionFactory : IDataSourceFunctionFactory
{
    private readonly IDataSourceRegistrationRepository _repository;
    private readonly IDataSourceQuerierFactory _querierFactory;
    private readonly ILogger<DataSourceFunctionFactory> _logger;

    public DataSourceFunctionFactory(
        IDataSourceRegistrationRepository repository,
        IDataSourceQuerierFactory querierFactory,
        ILogger<DataSourceFunctionFactory> logger)
    {
        _repository = repository;
        _querierFactory = querierFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AIFunction>> CreateFunctionsAsync(
        IReadOnlyList<DataSourceRefVO> dataSourceRefs,
        CancellationToken ct = default)
    {
        if (dataSourceRefs.Count == 0)
            return [];

        var ids = dataSourceRefs.Select(r => r.DataSourceId).Distinct().ToList();
        var dataSources = await _repository.GetByIdsAsync(ids, ct);
        var dsMap = dataSources.ToDictionary(d => d.Id);

        var functions = new List<AIFunction>();

        foreach (var dsRef in dataSourceRefs)
        {
            if (!dsMap.TryGetValue(dsRef.DataSourceId, out var ds))
            {
                _logger.LogWarning("DataSourceRef '{DataSourceId}' not found — datasource may have been deleted", dsRef.DataSourceId);
                continue;
            }

            try
            {
                var querier = _querierFactory.GetQuerier(ds.Product);
                var generatedFunctions = GenerateFunctionsForDataSource(ds, querier, dsRef.EnabledFunctions);
                functions.AddRange(generatedFunctions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create AIFunctions for DataSource '{Name}' (ID: {Id})", ds.Name, ds.Id);
            }
        }

        return functions;
    }

    private List<AIFunction> GenerateFunctionsForDataSource(
        DataSourceRegistration ds,
        IDataSourceQuerier querier,
        List<string>? enabledFunctions)
    {
        var functions = new List<AIFunction>();
        var safeName = ds.Name.Replace(" ", "_").Replace("-", "_").ToLowerInvariant();

        var candidates = ds.Category switch
        {
            DataSourceCategory.Metrics => GenerateMetricsFunctions(ds, querier, safeName),
            DataSourceCategory.Logs => GenerateLogsFunctions(ds, querier, safeName),
            DataSourceCategory.Tracing => GenerateTracingFunctions(ds, querier, safeName),
            DataSourceCategory.Alerting => GenerateAlertingFunctions(ds, querier, safeName),
            DataSourceCategory.Deployment => GenerateDeploymentFunctions(ds, querier, safeName),
            DataSourceCategory.Git => GenerateGitFunctions(ds, querier, safeName),
            _ => []
        };

        // Apply EnabledFunctions filter
        if (enabledFunctions is { Count: > 0 })
        {
            var enabledSet = new HashSet<string>(enabledFunctions, StringComparer.OrdinalIgnoreCase);
            candidates = candidates.Where(f => enabledSet.Contains(f.Name)).ToList();
        }

        functions.AddRange(candidates);
        return functions;
    }

    // ─── Metrics ────────────────────────────────────────────────────────────

    private List<AIFunction> GenerateMetricsFunctions(DataSourceRegistration ds, IDataSourceQuerier querier, string safeName)
    {
        return
        [
            AIFunctionFactory.Create(
                async (
                    [Description("PromQL expression. Examples: 'up', 'rate(http_requests_total[5m])', 'histogram_quantile(0.99, rate(http_request_duration_seconds_bucket[5m]))'. Use list_metric_names first if unsure which metrics exist.")] string expression,
                    [Description("Start time in ISO 8601 format (e.g. '2026-02-17T08:00:00Z'). Defaults to 1 hour ago if omitted. Use a wide range to avoid missing data.")] string? start = null,
                    [Description("End time in ISO 8601 format (e.g. '2026-02-17T12:00:00Z'). Defaults to now if omitted.")] string? end = null,
                    [Description("Step interval (e.g. '15s', '1m', '5m'). Defaults to auto if omitted.")] string? step = null
                ) =>
                {
                    var query = BuildTimeRangeQuery(expression, start, end, step);
                    var result = await querier.QueryAsync(ds, query);
                    return JsonSerializer.Serialize(result.TimeSeries ?? []);
                },
                new AIFunctionFactoryOptions
                {
                    Name = $"query_metrics_{safeName}",
                    Description = $"Query metrics from {ds.Name} ({ds.Product}) using PromQL expression. Returns time series data. Tip: call list_metric_names_{safeName} first to discover available metrics.",

                }),

            AIFunctionFactory.Create(
                async () =>
                {
                    var metadata = await querier.DiscoverMetadataAsync(ds);
                    return JsonSerializer.Serialize(metadata.Labels ?? []);
                },
                new AIFunctionFactoryOptions
                {
                    Name = $"list_metric_names_{safeName}",
                    Description = $"List available metric names from {ds.Name} ({ds.Product}). Call this first before query_metrics to discover what metrics exist.",

                }),

            AIFunctionFactory.Create(
                async () =>
                {
                    var metadata = await querier.DiscoverMetadataAsync(ds);
                    return JsonSerializer.Serialize(metadata.Labels ?? []);
                },
                new AIFunctionFactoryOptions
                {
                    Name = $"list_metric_labels_{safeName}",
                    Description = $"List available label names from {ds.Name} ({ds.Product}). Useful for knowing which dimensions can be used in PromQL queries.",

                })
        ];
    }

    // ─── Logs ───────────────────────────────────────────────────────────────

    private List<AIFunction> GenerateLogsFunctions(DataSourceRegistration ds, IDataSourceQuerier querier, string safeName)
    {
        return
        [
            AIFunctionFactory.Create(
                async (
                    [Description("LogQL stream selector and optional pipeline. Stream selector examples: '{namespace=\"demo-app\"}', '{app=\"order-service\"}', '{app=~\"order.*|payment.*\"}'. Pipeline filters: '|= \"error\"' (contains), '|~ \"(?i)error\"' (case-insensitive regex), '!= \"debug\"' (exclude). Full example: '{namespace=\"demo-app\"} |~ \"(?i)error|fail\"'. Call list_log_labels first to discover available label names.")] string expression,
                    [Description("Start time in ISO 8601 format (e.g. '2026-02-17T08:00:00Z'). Defaults to 1 hour ago if omitted. Use a wide range (hours, not minutes) to avoid missing data.")] string? start = null,
                    [Description("End time in ISO 8601 format (e.g. '2026-02-17T12:00:00Z'). Defaults to now if omitted.")] string? end = null,
                    [Description("Maximum number of log entries to return. Defaults to server default if omitted.")] int? limit = null
                ) =>
                {
                    var query = new DataSourceQueryVO
                    {
                        Expression = expression,
                        TimeRange = ParseTimeRange(start, end, null),
                        Pagination = limit.HasValue ? new PaginationVO { Limit = limit.Value } : null
                    };
                    var result = await querier.QueryAsync(ds, query);
                    return JsonSerializer.Serialize(result.LogEntries ?? []);
                },
                new AIFunctionFactoryOptions
                {
                    Name = $"query_logs_{safeName}",
                    Description = $"Query logs from {ds.Name} ({ds.Product}) using LogQL expression. Returns log entries. IMPORTANT: Use LogQL syntax for the expression — stream selector in curly braces, pipeline stages with '|=', '|~', '!='. Call list_log_labels_{safeName} first to discover available label names and values.",

                }),

            AIFunctionFactory.Create(
                async () =>
                {
                    var metadata = await querier.DiscoverMetadataAsync(ds);
                    return JsonSerializer.Serialize(metadata.Labels ?? []);
                },
                new AIFunctionFactoryOptions
                {
                    Name = $"list_log_labels_{safeName}",
                    Description = $"List available log label names from {ds.Name} ({ds.Product}). Call this first before query_logs to discover available label names (e.g. app, namespace, pod, container) for building LogQL stream selectors.",

                })
        ];
    }

    // ─── Tracing ────────────────────────────────────────────────────────────

    private List<AIFunction> GenerateTracingFunctions(DataSourceRegistration ds, IDataSourceQuerier querier, string safeName)
    {
        return
        [
            AIFunctionFactory.Create(
                async (
                    [Description("The full trace ID string (e.g. '4a0239dcee50c21c174d3d1867a615c9'). You can find trace IDs from search_traces results or from log entries that contain trace_id fields.")] string trace_id
                ) =>
                {
                    var query = new DataSourceQueryVO { Expression = trace_id };
                    var result = await querier.QueryAsync(ds, query);
                    return JsonSerializer.Serialize(result.Spans ?? []);
                },
                new AIFunctionFactoryOptions
                {
                    Name = $"get_trace_{safeName}",
                    Description = $"Get a complete trace with all spans from {ds.Name} ({ds.Product}) by trace ID. Returns the full span tree for a single distributed trace.",

                }),

            AIFunctionFactory.Create(
                async (
                    [Description("Service name to search traces for (e.g. 'order-service', 'payment-service'). Call list_services first to discover available service names.")] string service,
                    [Description("Operation name filter (e.g. 'POST /api/orders', 'GET /health'). Omit to search all operations for the service. Do NOT pass empty string or 'unknown'.")] string? operation = null,
                    [Description("Start time in ISO 8601 format (e.g. '2026-02-17T08:00:00Z'). Defaults to 1 hour ago if omitted. Use a wide range to avoid missing data.")] string? start = null,
                    [Description("End time in ISO 8601 format (e.g. '2026-02-17T12:00:00Z'). Defaults to now if omitted.")] string? end = null,
                    [Description("Maximum number of traces to return. Defaults to 20 if omitted.")] int? limit = null
                ) =>
                {
                    var filters = new List<LabelFilterVO>
                    {
                        new() { Key = "service", Value = service }
                    };
                    if (!string.IsNullOrEmpty(operation))
                        filters.Add(new LabelFilterVO { Key = "operation", Value = operation });

                    var query = new DataSourceQueryVO
                    {
                        Filters = filters,
                        TimeRange = ParseTimeRange(start, end, null),
                        Pagination = limit.HasValue ? new PaginationVO { Limit = limit.Value } : null
                    };
                    var result = await querier.QueryAsync(ds, query);
                    return JsonSerializer.Serialize(result.Spans ?? []);
                },
                new AIFunctionFactoryOptions
                {
                    Name = $"search_traces_{safeName}",
                    Description = $"Search traces from {ds.Name} ({ds.Product}) by service name, with optional operation and time range filters. Omit operation to search all operations. Call list_services_{safeName} first to discover available service names.",

                }),

            AIFunctionFactory.Create(
                async () =>
                {
                    var metadata = await querier.DiscoverMetadataAsync(ds);
                    return JsonSerializer.Serialize(metadata.Services ?? []);
                },
                new AIFunctionFactoryOptions
                {
                    Name = $"list_services_{safeName}",
                    Description = $"List available service names from {ds.Name} ({ds.Product}). Call this first before search_traces to discover valid service names.",

                })
        ];
    }

    // ─── Alerting ───────────────────────────────────────────────────────────

    private List<AIFunction> GenerateAlertingFunctions(DataSourceRegistration ds, IDataSourceQuerier querier, string safeName)
    {
        return
        [
            AIFunctionFactory.Create(
                async (
                    [Description("Optional state filter: 'active', 'suppressed', or 'unprocessed'. Omit to list all alerts.")] string? state = null
                ) =>
                {
                    var additionalParams = new Dictionary<string, string>();
                    if (!string.IsNullOrEmpty(state))
                        additionalParams["state"] = state;

                    var query = new DataSourceQueryVO { AdditionalParams = additionalParams };
                    var result = await querier.QueryAsync(ds, query);
                    return JsonSerializer.Serialize(result.Alerts ?? []);
                },
                new AIFunctionFactoryOptions
                {
                    Name = $"list_alerts_{safeName}",
                    Description = $"List current alerts from {ds.Name} ({ds.Product}). Optional state filter: 'active', 'suppressed', 'unprocessed'.",

                }),

            AIFunctionFactory.Create(
                async (
                    [Description("Alert name to filter by (e.g. 'HighErrorRate', 'PodCrashLooping'). Omit to query all alerts.")] string? alert_name = null,
                    [Description("Start time in ISO 8601 format (e.g. '2026-02-17T08:00:00Z'). Defaults to 1 hour ago if omitted.")] string? start = null,
                    [Description("End time in ISO 8601 format (e.g. '2026-02-17T12:00:00Z'). Defaults to now if omitted.")] string? end = null
                ) =>
                {
                    var filters = new List<LabelFilterVO>();
                    if (!string.IsNullOrEmpty(alert_name))
                        filters.Add(new LabelFilterVO { Key = "alertname", Value = alert_name });

                    var query = new DataSourceQueryVO
                    {
                        Filters = filters.Count > 0 ? filters : null,
                        TimeRange = ParseTimeRange(start, end, null)
                    };
                    var result = await querier.QueryAsync(ds, query);
                    return JsonSerializer.Serialize(result.Alerts ?? []);
                },
                new AIFunctionFactoryOptions
                {
                    Name = $"get_alert_history_{safeName}",
                    Description = $"Get alert history from {ds.Name} ({ds.Product}) for a specific alert name and time range.",

                })
        ];
    }

    // ─── Deployment ─────────────────────────────────────────────────────────

    private List<AIFunction> GenerateDeploymentFunctions(DataSourceRegistration ds, IDataSourceQuerier querier, string safeName)
    {
        return
        [
            AIFunctionFactory.Create(
                async (
                    [Description("Kubernetes resource kind. Valid values: 'Pod', 'Deployment', 'Service', 'Namespace', 'Node', 'ConfigMap', 'Secret', 'StatefulSet', 'DaemonSet', 'Job', 'CronJob', 'Ingress', 'ReplicaSet'.")] string kind,
                    [Description("Kubernetes namespace to filter by (e.g. 'demo-app', 'default', 'kube-system'). Omit to list across all namespaces.")] string? ns = null,
                    [Description("Label selector as comma-separated key=value pairs (e.g. 'app=order-service,version=v1'). Omit to list all resources of the specified kind.")] string? labels = null
                ) =>
                {
                    var filters = new List<LabelFilterVO>();
                    if (!string.IsNullOrEmpty(ns))
                        filters.Add(new LabelFilterVO { Key = "namespace", Value = ns });
                    if (!string.IsNullOrEmpty(labels))
                    {
                        foreach (var pair in labels.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        {
                            var parts = pair.Split('=', 2);
                            if (parts.Length == 2)
                                filters.Add(new LabelFilterVO { Key = parts[0].Trim(), Value = parts[1].Trim() });
                        }
                    }

                    var query = new DataSourceQueryVO
                    {
                        Expression = $"kind={kind}",
                        Filters = filters.Count > 0 ? filters : null
                    };
                    var result = await querier.QueryAsync(ds, query);
                    return JsonSerializer.Serialize(result.Resources ?? []);
                },
                new AIFunctionFactoryOptions
                {
                    Name = $"list_resources_{safeName}",
                    Description = $"List Kubernetes resources from {ds.Name} ({ds.Product}) by kind, with optional namespace and label filters."
                }),

            AIFunctionFactory.Create(
                async (
                    [Description("Kubernetes resource kind (e.g. 'Pod', 'Deployment', 'Service').")] string kind,
                    [Description("Exact resource name (e.g. 'order-service-6bb647cc8-2mxvj' for a Pod, 'order-service' for a Deployment).")] string name,
                    [Description("Kubernetes namespace (e.g. 'demo-app', 'default'). Omit if the resource is cluster-scoped (e.g. Node, Namespace).")] string? ns = null
                ) =>
                {
                    var filters = new List<LabelFilterVO>();
                    if (!string.IsNullOrEmpty(ns))
                        filters.Add(new LabelFilterVO { Key = "namespace", Value = ns });

                    var query = new DataSourceQueryVO
                    {
                        Expression = $"kind={kind}",
                        Filters = filters.Count > 0 ? filters : null,
                        AdditionalParams = new Dictionary<string, string> { ["name"] = name }
                    };
                    var result = await querier.QueryAsync(ds, query);
                    var match = result.Resources?.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    return JsonSerializer.Serialize(match);
                },
                new AIFunctionFactoryOptions
                {
                    Name = $"get_resource_{safeName}",
                    Description = $"Get a specific Kubernetes resource from {ds.Name} ({ds.Product}) by kind, name, and optional namespace."
                })
        ];
    }

    // ─── Git ────────────────────────────────────────────────────────────────

    private List<AIFunction> GenerateGitFunctions(DataSourceRegistration ds, IDataSourceQuerier querier, string safeName)
    {
        return
        [
            AIFunctionFactory.Create(
                async (
                    [Description("Repository path in 'owner/repo' format (e.g. 'microsoft/vscode'). Omit to list across all repos.")] string? repo = null,
                    [Description("Branch name (e.g. 'main', 'develop'). Omit to include all branches.")] string? branch = null,
                    [Description("Start date in ISO 8601 format (e.g. '2026-02-17T00:00:00Z'). Omit for no start filter.")] string? since = null,
                    [Description("End date in ISO 8601 format. Omit for no end filter.")] string? until = null,
                    [Description("Maximum number of commits to return. Omit for server default.")] int? limit = null
                ) =>
                {
                    var additionalParams = new Dictionary<string, string>();
                    if (!string.IsNullOrEmpty(repo)) additionalParams["repo"] = repo;
                    if (!string.IsNullOrEmpty(branch)) additionalParams["branch"] = branch;

                    var query = new DataSourceQueryVO
                    {
                        Expression = "commits",
                        TimeRange = ParseTimeRange(since, until, null),
                        Pagination = limit.HasValue ? new PaginationVO { Limit = limit.Value } : null,
                        AdditionalParams = additionalParams.Count > 0 ? additionalParams : null
                    };
                    var result = await querier.QueryAsync(ds, query);
                    return JsonSerializer.Serialize(result.Resources ?? []);
                },
                new AIFunctionFactoryOptions
                {
                    Name = $"list_commits_{safeName}",
                    Description = $"List recent commits from {ds.Name} ({ds.Product}). Optional: repo (owner/repo), branch, since/until dates, limit."
                }),

            AIFunctionFactory.Create(
                async (
                    [Description("Repository path in 'owner/repo' format. Omit to list across all repos.")] string? repo = null,
                    [Description("Pipeline status filter (e.g. 'success', 'failed', 'running'). Omit to list all statuses.")] string? status = null,
                    [Description("Maximum number of pipelines to return. Omit for server default.")] int? limit = null
                ) =>
                {
                    var additionalParams = new Dictionary<string, string>();
                    if (!string.IsNullOrEmpty(repo)) additionalParams["repo"] = repo;
                    if (!string.IsNullOrEmpty(status)) additionalParams["status"] = status;

                    var query = new DataSourceQueryVO
                    {
                        Expression = "pipelines",
                        Pagination = limit.HasValue ? new PaginationVO { Limit = limit.Value } : null,
                        AdditionalParams = additionalParams.Count > 0 ? additionalParams : null
                    };
                    var result = await querier.QueryAsync(ds, query);
                    return JsonSerializer.Serialize(result.Resources ?? []);
                },
                new AIFunctionFactoryOptions
                {
                    Name = $"list_pipelines_{safeName}",
                    Description = $"List CI/CD pipelines from {ds.Name} ({ds.Product}). Optional: repo (owner/repo), status filter, limit."
                })
        ];
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static DataSourceQueryVO BuildTimeRangeQuery(string expression, string? start, string? end, string? step)
    {
        return new DataSourceQueryVO
        {
            Expression = expression,
            TimeRange = ParseTimeRange(start, end, step)
        };
    }

    private static TimeRangeVO? ParseTimeRange(string? start, string? end, string? step)
    {
        if (string.IsNullOrEmpty(start) && string.IsNullOrEmpty(end))
            return null;

        var startDt = !string.IsNullOrEmpty(start) && DateTime.TryParse(start, out var s)
            ? s.ToUniversalTime()
            : DateTime.UtcNow.AddHours(-1);

        var endDt = !string.IsNullOrEmpty(end) && DateTime.TryParse(end, out var e)
            ? e.ToUniversalTime()
            : DateTime.UtcNow;

        return new TimeRangeVO
        {
            Start = startDt,
            End = endDt,
            Step = step
        };
    }
}
