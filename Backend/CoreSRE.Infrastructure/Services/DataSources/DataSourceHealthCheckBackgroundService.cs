using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services.DataSources;

/// <summary>
/// 定时健康检查后台服务。每 60 秒扫描所有已注册数据源，
/// 调用 IDataSourceQuerier.HealthCheckAsync 更新 HealthCheck 状态。
/// 连续失败 3 次 → 状态转 Error，恢复 → 状态转 Connected。
/// </summary>
public class DataSourceHealthCheckBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DataSourceHealthCheckBackgroundService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(60);
    private readonly int _maxConsecutiveFailures = 3;

    // Track consecutive failures per datasource
    private readonly Dictionary<Guid, int> _failureCounters = new();

    public DataSourceHealthCheckBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<DataSourceHealthCheckBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DataSource health check background service started (interval: {Interval}s)", _interval.TotalSeconds);

        // Wait a bit before first check to let the app fully start
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunHealthChecksAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during health check cycle");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("DataSource health check background service stopped");
    }

    private async Task RunHealthChecksAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var querierFactory = scope.ServiceProvider.GetRequiredService<IDataSourceQuerierFactory>();

        // Load all registered datasources (not Error status for > maxFailures — they still get checked)
        var dataSources = await dbContext.DataSourceRegistrations
            .AsTracking()
            .ToListAsync(ct);

        if (dataSources.Count == 0)
            return;

        _logger.LogDebug("Running health checks for {Count} datasources", dataSources.Count);

        var tasks = dataSources.Select(ds => CheckSingleDataSourceAsync(ds, querierFactory, ct));
        await Task.WhenAll(tasks);

        // Save all health check updates in one batch
        await dbContext.SaveChangesAsync(ct);
    }

    private async Task CheckSingleDataSourceAsync(
        DataSourceRegistration ds,
        IDataSourceQuerierFactory querierFactory,
        CancellationToken ct)
    {
        try
        {
            var querier = querierFactory.GetQuerier(ds.Product);
            var healthResult = await querier.HealthCheckAsync(ds, ct);

            if (healthResult.IsHealthy)
            {
                // Reset failure counter on success
                _failureCounters.Remove(ds.Id);

                // Update health and transition to Connected if was Error/Registered/Disconnected
                ds.UpdateHealthCheck(healthResult);

                _logger.LogDebug(
                    "Health check OK for '{Name}' ({Product}) — {ResponseTime}ms, version: {Version}",
                    ds.Name, ds.Product, healthResult.ResponseTimeMs, healthResult.Version);
            }
            else
            {
                // Track failure
                _failureCounters.TryGetValue(ds.Id, out var count);
                count++;
                _failureCounters[ds.Id] = count;

                if (count >= _maxConsecutiveFailures)
                {
                    // Transition to Error after N consecutive failures
                    ds.MarkError(healthResult.ErrorMessage ?? "Health check failed");
                    _logger.LogWarning(
                        "DataSource '{Name}' ({Product}) marked as Error after {Count} consecutive failures: {Error}",
                        ds.Name, ds.Product, count, healthResult.ErrorMessage);
                }
                else
                {
                    // Just update health, keep current status
                    ds.UpdateHealthCheck(healthResult);
                    _logger.LogDebug(
                        "Health check FAILED for '{Name}' ({Product}) — failure {Count}/{Max}: {Error}",
                        ds.Name, ds.Product, count, _maxConsecutiveFailures, healthResult.ErrorMessage);
                }
            }
        }
        catch (NotSupportedException)
        {
            // No querier for this product — skip silently
            _logger.LogDebug("No querier registered for DataSource '{Name}' ({Product}), skipping health check", ds.Name, ds.Product);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health check exception for DataSource '{Name}' ({Product})", ds.Name, ds.Product);

            _failureCounters.TryGetValue(ds.Id, out var count);
            count++;
            _failureCounters[ds.Id] = count;

            if (count >= _maxConsecutiveFailures)
            {
                ds.MarkError(ex.Message);
            }
        }
    }
}
