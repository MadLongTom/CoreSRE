using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services.Sandbox;

/// <summary>
/// 后台服务 — 定期检查并自动停止超时无活动的持久化沙箱。
/// </summary>
public sealed class SandboxAutoStopService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SandboxAutoStopService> _logger;

    /// <summary>检查间隔</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    public SandboxAutoStopService(
        IServiceScopeFactory scopeFactory,
        ILogger<SandboxAutoStopService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[SandboxAutoStop] Service started, check interval: {Interval}", CheckInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
                await CheckAndStopInactiveSandboxesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SandboxAutoStop] Error during check cycle (will retry)");
            }
        }

        _logger.LogInformation("[SandboxAutoStop] Service stopped");
    }

    private async Task CheckAndStopInactiveSandboxesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISandboxInstanceRepository>();
        var manager = scope.ServiceProvider.GetRequiredService<IPersistentSandboxManager>();

        var runningSandboxes = await repo.GetRunningWithAutoStopAsync(ct);
        var stopCount = 0;

        foreach (var sandbox in runningSandboxes)
        {
            if (sandbox.IsInactive)
            {
                _logger.LogInformation(
                    "[SandboxAutoStop] Stopping inactive sandbox: {Name} (last activity: {LastActivity})",
                    sandbox.Name, sandbox.LastActivityAt);

                try
                {
                    await manager.StopAsync(sandbox, ct);
                    await repo.UpdateAsync(sandbox, ct);
                    stopCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[SandboxAutoStop] Failed to stop sandbox {Name}", sandbox.Name);
                }
            }
        }

        if (stopCount > 0)
        {
            _logger.LogInformation("[SandboxAutoStop] Stopped {Count} inactive sandboxes", stopCount);
        }
    }
}
