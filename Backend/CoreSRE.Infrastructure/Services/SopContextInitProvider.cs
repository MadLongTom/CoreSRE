using System.Text;
using System.Text.RegularExpressions;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using CoreSRE.Infrastructure.Services.DataSources;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// SOP 上下文初始化 Provider — 在 Agent 对话开始前，根据 SOP 和 AlertRule 声明的上下文条目，
/// 并行预查数据源并将结果注入到 AIContext.Instructions 中。
///
/// 通过 AgentSession.StateBag 传递参数（由 IncidentDispatcherService 设置）：
/// - "contextInitItems": List&lt;ContextInitItemVO&gt;（来自 AlertRule.ContextProviders ∪ SOP 初始化上下文）
/// - "alertLabels": Dictionary&lt;string, string&gt;（告警标签，用于 ${label} 模板变量替换）
///
/// 执行完成后从 StateBag 移除 key，确保后续对话轮不重复预查。
/// </summary>
public sealed partial class SopContextInitProvider : AIContextProvider
{
    private readonly IDataSourceQuerierFactory _querierFactory;
    private readonly IDataSourceRegistrationRepository _dsRepo;
    private readonly ILogger _logger;

    /// <summary>StateBag key: 上下文初始化条目列表</summary>
    public const string ContextInitItemsKey = "contextInitItems";

    /// <summary>StateBag key: 告警标签字典（用于模板变量替换）</summary>
    public const string AlertLabelsKey = "alertLabels";

    private static readonly TimeSpan PerItemTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(60);

    [GeneratedRegex(@"\$\{(\w+)\}")]
    private static partial Regex TemplateVariableRegex();

    public SopContextInitProvider(
        IDataSourceQuerierFactory querierFactory,
        IDataSourceRegistrationRepository dsRepo,
        ILoggerFactory loggerFactory)
    {
        _querierFactory = querierFactory;
        _dsRepo = dsRepo;
        _logger = loggerFactory.CreateLogger<SopContextInitProvider>();
    }

    /// <inheritdoc />
    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        // 1. 从 Session.StateBag 读取 context init 参数
        if (context.Session is null)
            return new AIContext();

        if (!context.Session.StateBag.TryGetValue<ContextInitState>(ContextInitItemsKey, out var initState)
            || initState?.Items is null or { Count: 0 })
            return new AIContext(); // 无操作

        var items = initState.Items;
        context.Session.StateBag.TryGetValue<AlertLabelsState>(AlertLabelsKey, out var labelsState);
        var labels = labelsState?.Labels ?? new();

        _logger.LogInformation(
            "SopContextInitProvider executing {ItemCount} context init items",
            items.Count);

        // 2. 模板变量替换
        var resolvedItems = items.Select(i => ResolveTemplateVariables(i, labels)).ToList();

        // 3. 去重
        resolvedItems = resolvedItems
            .DistinctBy(i => $"{i.Category}:{i.Expression}")
            .ToList();

        // 4. 并行执行查询(单项30s, 总60s)
        var result = await ExecuteQueriesAsync(resolvedItems, cancellationToken);

        // 5. 格式化为 Instructions markdown
        var instructions = FormatAsInstructions(result);

        // 6. 标记已执行（防止多轮重复查询）
        context.Session.StateBag.TryRemoveValue(ContextInitItemsKey);

        _logger.LogInformation(
            "SopContextInitProvider completed: {SuccessCount}/{TotalCount} queries succeeded in {Duration}ms",
            result.Entries.Count(e => e.Success), result.Entries.Count, result.TotalDuration.TotalMilliseconds);

        return new AIContext { Instructions = instructions };
    }

    private static ContextInitItemVO ResolveTemplateVariables(
        ContextInitItemVO item, Dictionary<string, string> labels)
    {
        var expression = TemplateVariableRegex().Replace(item.Expression, match =>
        {
            var key = match.Groups[1].Value;
            return labels.TryGetValue(key, out var val) ? val : match.Value;
        });

        return item with { Expression = expression };
    }

    private async Task<ContextInitResultVO> ExecuteQueriesAsync(
        List<ContextInitItemVO> items, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        using var totalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        totalCts.CancelAfter(TotalTimeout);

        // Load all data sources by category for routing
        var categoryToDatasource = await LoadDataSourcesByCategory(totalCts.Token);

        var tasks = items.Select(item =>
            ExecuteSingleQueryAsync(item, categoryToDatasource, totalCts.Token));

        var entries = await Task.WhenAll(tasks);

        sw.Stop();
        return new ContextInitResultVO
        {
            Entries = entries.ToList(),
            TotalDuration = sw.Elapsed
        };
    }

    private async Task<ContextInitEntry> ExecuteSingleQueryAsync(
        ContextInitItemVO item,
        Dictionary<DataSourceCategory, DataSourceRegistration> categoryToDatasource,
        CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var label = item.Label ?? $"{item.Category} query";

        try
        {
            // Parse category
            if (!Enum.TryParse<DataSourceCategory>(item.Category, ignoreCase: true, out var category))
            {
                return new ContextInitEntry
                {
                    Label = label,
                    Category = item.Category,
                    Success = false,
                    ErrorMessage = $"Unknown category: {item.Category}",
                    Duration = sw.Elapsed
                };
            }

            if (!categoryToDatasource.TryGetValue(category, out var ds))
            {
                return new ContextInitEntry
                {
                    Label = label,
                    Category = item.Category,
                    Success = false,
                    ErrorMessage = $"No connected data source for category: {item.Category}",
                    Duration = sw.Elapsed
                };
            }

            var querier = _querierFactory.GetQuerier(ds.Product);

            // Build query based on category
            var query = BuildQuery(item, category);

            using var itemCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            itemCts.CancelAfter(PerItemTimeout);

            var result = await querier.QueryAsync(ds, query, itemCts.Token);
            sw.Stop();

            var resultText = FormatQueryResult(result, category);
            resultText = ResultTruncator.TruncatePlainText(resultText);

            return new ContextInitEntry
            {
                Label = label,
                Category = item.Category,
                Success = true,
                Result = resultText,
                Duration = sw.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new ContextInitEntry
            {
                Label = label,
                Category = item.Category,
                Success = false,
                ErrorMessage = $"[timeout: {item.Category} query exceeded {PerItemTimeout.TotalSeconds}s]",
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Context init query failed: {Category} - {Expression}", item.Category, item.Expression);
            return new ContextInitEntry
            {
                Label = label,
                Category = item.Category,
                Success = false,
                ErrorMessage = ex.Message,
                Duration = sw.Elapsed
            };
        }
    }

    private async Task<Dictionary<DataSourceCategory, DataSourceRegistration>> LoadDataSourcesByCategory(
        CancellationToken ct)
    {
        var allDs = await _dsRepo.GetAllAsync(ct);
        var result = new Dictionary<DataSourceCategory, DataSourceRegistration>();

        foreach (var ds in allDs)
        {
            // Use first Connected datasource per category
            if (ds.Status == DataSourceStatus.Connected && !result.ContainsKey(ds.Category))
            {
                result[ds.Category] = ds;
            }
        }

        return result;
    }

    private static DataSourceQueryVO BuildQuery(ContextInitItemVO item, DataSourceCategory category)
    {
        var timeRange = ParseLookback(item.Lookback ?? "1h");

        return category switch
        {
            DataSourceCategory.Metrics => new DataSourceQueryVO
            {
                Expression = item.Expression,
                TimeRange = timeRange
            },
            DataSourceCategory.Logs => new DataSourceQueryVO
            {
                Expression = item.Expression,
                TimeRange = timeRange,
                Pagination = new PaginationVO { Limit = 50 }
            },
            DataSourceCategory.Deployment => new DataSourceQueryVO
            {
                Expression = item.Expression,
                Filters = item.ExtraParams?.Select(kv =>
                    new LabelFilterVO { Key = kv.Key, Value = kv.Value }).ToList()
            },
            DataSourceCategory.Git => new DataSourceQueryVO
            {
                Expression = item.Expression,
                TimeRange = timeRange,
                Pagination = new PaginationVO { Limit = 20 }
            },
            _ => new DataSourceQueryVO { Expression = item.Expression, TimeRange = timeRange }
        };
    }

    private static string FormatQueryResult(DataSourceResultVO result, DataSourceCategory category)
    {
        return category switch
        {
            DataSourceCategory.Metrics =>
                System.Text.Json.JsonSerializer.Serialize(result.TimeSeries ?? []),
            DataSourceCategory.Logs =>
                System.Text.Json.JsonSerializer.Serialize(result.LogEntries ?? []),
            DataSourceCategory.Tracing =>
                System.Text.Json.JsonSerializer.Serialize(result.Spans ?? []),
            DataSourceCategory.Alerting =>
                System.Text.Json.JsonSerializer.Serialize(result.Alerts ?? []),
            DataSourceCategory.Deployment or DataSourceCategory.Git =>
                System.Text.Json.JsonSerializer.Serialize(result.Resources ?? []),
            _ => System.Text.Json.JsonSerializer.Serialize(result)
        };
    }

    private static string FormatAsInstructions(ContextInitResultVO result)
    {
        if (result.Entries.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("## 📊 预加载诊断上下文 (自动查询)");
        sb.AppendLine();

        foreach (var entry in result.Entries)
        {
            sb.AppendLine($"### {entry.Label} ({entry.Category})");

            if (entry.Success)
            {
                sb.AppendLine($"结果: {entry.Result}");
            }
            else
            {
                sb.AppendLine($"查询失败: {entry.ErrorMessage}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("以上数据已预先查询。请在分析和执行过程中优先参考这些数据，如需获取更多信息可调用相应的数据源工具。");

        return sb.ToString();
    }

    private static TimeRangeVO ParseLookback(string lookback)
    {
        var now = DateTime.UtcNow;
        var duration = lookback switch
        {
            var lb when lb.EndsWith('m') && int.TryParse(lb[..^1], out var m) => TimeSpan.FromMinutes(m),
            var lb when lb.EndsWith('h') && int.TryParse(lb[..^1], out var h) => TimeSpan.FromHours(h),
            var lb when lb.EndsWith('d') && int.TryParse(lb[..^1], out var d) => TimeSpan.FromDays(d),
            _ => TimeSpan.FromHours(1)
        };

        return new TimeRangeVO
        {
            Start = now - duration,
            End = now
        };
    }

    /// <summary>StateBag wrapper for context init items (JSON round-trip safe).</summary>
    public sealed class ContextInitState
    {
        public List<ContextInitItemVO> Items { get; set; } = [];
    }

    /// <summary>StateBag wrapper for alert labels (JSON round-trip safe).</summary>
    public sealed class AlertLabelsState
    {
        public Dictionary<string, string> Labels { get; set; } = new();
    }
}
