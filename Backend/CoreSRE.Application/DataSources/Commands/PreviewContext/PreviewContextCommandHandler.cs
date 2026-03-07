using System.Diagnostics;
using System.Text.RegularExpressions;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Application.DataSources.Commands.PreviewContext;

public partial class PreviewContextCommandHandler(
    IDataSourceQuerierFactory querierFactory,
    IDataSourceRegistrationRepository dsRepo,
    ILogger<PreviewContextCommandHandler> logger)
    : IRequestHandler<PreviewContextCommand, Result<ContextInitResultVO>>
{
    private static readonly TimeSpan PerItemTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(60);

    [GeneratedRegex(@"\$\{(\w+)\}")]
    private static partial Regex TemplateVariableRegex();

    public async Task<Result<ContextInitResultVO>> Handle(
        PreviewContextCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Items is not { Count: > 0 })
            return Result<ContextInitResultVO>.Fail("At least one context init item is required.");

        var labels = request.TemplateVariables ?? new();

        // Template variable substitution
        var resolvedItems = request.Items
            .Select(i => ResolveTemplateVariables(i, labels))
            .DistinctBy(i => $"{i.Category}:{i.Expression}")
            .ToList();

        // Execute parallel queries
        var sw = Stopwatch.StartNew();

        using var totalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        totalCts.CancelAfter(TotalTimeout);

        var categoryToDatasource = await LoadDataSourcesByCategory(totalCts.Token);

        var tasks = resolvedItems.Select(item =>
            ExecuteSingleQueryAsync(item, categoryToDatasource, totalCts.Token));

        var entries = await Task.WhenAll(tasks);
        sw.Stop();

        var result = new ContextInitResultVO
        {
            Entries = entries.ToList(),
            TotalDuration = sw.Elapsed
        };

        logger.LogInformation(
            "Context preview completed: {SuccessCount}/{TotalCount} in {Duration}ms",
            result.Entries.Count(e => e.Success), result.Entries.Count, result.TotalDuration.TotalMilliseconds);

        return Result<ContextInitResultVO>.Ok(result);
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

    private async Task<Dictionary<DataSourceCategory, DataSourceRegistration>> LoadDataSourcesByCategory(
        CancellationToken ct)
    {
        var allDs = await dsRepo.GetAllAsync(ct);
        var result = new Dictionary<DataSourceCategory, DataSourceRegistration>();
        foreach (var ds in allDs)
        {
            if (ds.Status == DataSourceStatus.Connected && !result.ContainsKey(ds.Category))
                result[ds.Category] = ds;
        }
        return result;
    }

    private async Task<ContextInitEntry> ExecuteSingleQueryAsync(
        ContextInitItemVO item,
        Dictionary<DataSourceCategory, DataSourceRegistration> categoryToDatasource,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var label = item.Label ?? $"{item.Category} query";

        try
        {
            if (!Enum.TryParse<DataSourceCategory>(item.Category, ignoreCase: true, out var category))
                return new ContextInitEntry { Label = label, Category = item.Category, Success = false, ErrorMessage = $"Unknown category: {item.Category}", Duration = sw.Elapsed };

            if (!categoryToDatasource.TryGetValue(category, out var ds))
                return new ContextInitEntry { Label = label, Category = item.Category, Success = false, ErrorMessage = $"No connected data source for category: {item.Category}", Duration = sw.Elapsed };

            var querier = querierFactory.GetQuerier(ds.Product);
            var query = BuildQuery(item, category);

            using var itemCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            itemCts.CancelAfter(PerItemTimeout);

            var result = await querier.QueryAsync(ds, query, itemCts.Token);
            sw.Stop();

            var resultText = FormatQueryResult(result, category);
            // Preview truncation: cap at ~16KB for human readability
            if (resultText.Length > 16000)
                resultText = resultText[..16000] + "\n... [truncated]";

            return new ContextInitEntry { Label = label, Category = item.Category, Success = true, Result = resultText, Duration = sw.Elapsed };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new ContextInitEntry { Label = label, Category = item.Category, Success = false, ErrorMessage = $"Timeout: query exceeded {PerItemTimeout.TotalSeconds}s", Duration = sw.Elapsed };
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning(ex, "Context preview query failed: {Category} - {Expression}", item.Category, item.Expression);
            return new ContextInitEntry { Label = label, Category = item.Category, Success = false, ErrorMessage = ex.Message, Duration = sw.Elapsed };
        }
    }

    private static DataSourceQueryVO BuildQuery(ContextInitItemVO item, DataSourceCategory category)
    {
        var timeRange = ParseLookback(item.Lookback ?? "1h");
        return category switch
        {
            DataSourceCategory.Metrics => new DataSourceQueryVO { Expression = item.Expression, TimeRange = timeRange },
            DataSourceCategory.Logs => new DataSourceQueryVO { Expression = item.Expression, TimeRange = timeRange, Pagination = new PaginationVO { Limit = 50 } },
            DataSourceCategory.Deployment => new DataSourceQueryVO { Expression = item.Expression, Filters = item.ExtraParams?.Select(kv => new LabelFilterVO { Key = kv.Key, Value = kv.Value }).ToList() },
            DataSourceCategory.Git => new DataSourceQueryVO { Expression = item.Expression, TimeRange = timeRange, Pagination = new PaginationVO { Limit = 20 } },
            _ => new DataSourceQueryVO { Expression = item.Expression, TimeRange = timeRange }
        };
    }

    private static string FormatQueryResult(DataSourceResultVO result, DataSourceCategory category) =>
        category switch
        {
            DataSourceCategory.Metrics => System.Text.Json.JsonSerializer.Serialize(result.TimeSeries ?? []),
            DataSourceCategory.Logs => System.Text.Json.JsonSerializer.Serialize(result.LogEntries ?? []),
            DataSourceCategory.Tracing => System.Text.Json.JsonSerializer.Serialize(result.Spans ?? []),
            DataSourceCategory.Alerting => System.Text.Json.JsonSerializer.Serialize(result.Alerts ?? []),
            DataSourceCategory.Deployment or DataSourceCategory.Git => System.Text.Json.JsonSerializer.Serialize(result.Resources ?? []),
            _ => System.Text.Json.JsonSerializer.Serialize(result)
        };

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
        return new TimeRangeVO { Start = now - duration, End = now };
    }
}
