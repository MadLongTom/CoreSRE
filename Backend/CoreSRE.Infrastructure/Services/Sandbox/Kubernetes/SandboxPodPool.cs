using System.Collections.Concurrent;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services.Sandbox.Kubernetes;

/// <summary>
/// 全局 Pod 池（Singleton），管理所有沙盒 Pod 的生命周期。
/// 实现 IHostedService 以在应用启动时清理孤儿 Pod、关闭时删除所有活跃 Pod。
/// </summary>
public sealed class SandboxPodPool : IHostedService, IDisposable
{
    private readonly k8s.Kubernetes _client;
    private readonly ILogger<SandboxPodPool> _logger;

    /// <summary>
    /// 活跃的 Pod 实例池。Key = "{agentId:N}/{conversationId}"。
    /// </summary>
    internal readonly ConcurrentDictionary<string, ISandboxBox> Boxes = new();

    /// <summary>用于标识由 CoreSRE 创建的沙盒 Pod 的标签</summary>
    private const string ManagedByLabel = "app.kubernetes.io/managed-by";
    private const string ManagedByValue = "coresre";
    private const string ComponentLabel = "coresre/component";
    private const string ComponentValue = "sandbox";

    public SandboxPodPool(k8s.Kubernetes client, ILogger<SandboxPodPool> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// 应用启动时清理上次残留的孤儿 Pod。
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[SandboxPodPool] Starting — cleaning up orphan pods...");
        await CleanupOrphanPodsAsync(cancellationToken);
    }

    /// <summary>
    /// 应用关闭时删除所有活跃的沙盒 Pod。
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[SandboxPodPool] Stopping — disposing {Count} active sandbox pods...",
            Boxes.Count);

        var disposeTasks = new List<Task>();

        foreach (var kv in Boxes)
        {
            disposeTasks.Add(DisposeBoxSafeAsync(kv.Key, kv.Value));
        }

        // 等待所有 Pod 删除完成，最多 15 秒
        if (disposeTasks.Count > 0)
        {
            try
            {
                await Task.WhenAll(disposeTasks)
                    .WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("[SandboxPodPool] Shutdown timeout — some pods may not be cleaned up");
            }
        }

        Boxes.Clear();
        _logger.LogInformation("[SandboxPodPool] All sandbox pods disposed");
    }

    /// <summary>
    /// 清理上次运行残留的 Pod（通过标签选择器匹配）。
    /// </summary>
    private async Task CleanupOrphanPodsAsync(CancellationToken ct)
    {
        try
        {
            // 查找所有带 coresre 标签的 sandbox Pod（跨所有 namespace）
            var labelSelector = $"{ManagedByLabel}={ManagedByValue},{ComponentLabel}={ComponentValue}";

            // 列出所有 namespace 中的孤儿 Pod
            var podList = await _client.CoreV1.ListPodForAllNamespacesAsync(
                labelSelector: labelSelector,
                cancellationToken: ct);

            if (podList.Items.Count == 0)
            {
                _logger.LogInformation("[SandboxPodPool] No orphan pods found");
                return;
            }

            _logger.LogWarning(
                "[SandboxPodPool] Found {Count} orphan sandbox pods, deleting...",
                podList.Items.Count);

            var deleteTasks = podList.Items.Select(pod =>
                DeletePodSafeAsync(
                    pod.Metadata.Name,
                    pod.Metadata.NamespaceProperty,
                    ct));

            await Task.WhenAll(deleteTasks);

            _logger.LogInformation("[SandboxPodPool] Orphan pods cleanup complete");
        }
        catch (Exception ex)
        {
            // 启动时孤儿清理失败不应阻止应用启动
            _logger.LogWarning(ex, "[SandboxPodPool] Failed to clean up orphan pods (non-fatal)");
        }
    }

    private async Task DeletePodSafeAsync(string podName, string ns, CancellationToken ct)
    {
        try
        {
            await _client.CoreV1.DeleteNamespacedPodAsync(
                podName, ns,
                gracePeriodSeconds: 0,
                propagationPolicy: "Foreground",
                cancellationToken: ct);

            _logger.LogInformation(
                "[SandboxPodPool] Deleted orphan pod {PodName} in {Namespace}",
                podName, ns);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[SandboxPodPool] Failed to delete orphan pod {PodName} in {Namespace}",
                podName, ns);
        }
    }

    private async Task DisposeBoxSafeAsync(string key, ISandboxBox box)
    {
        try
        {
            await box.DisposeAsync();
            _logger.LogInformation("[SandboxPodPool] Disposed sandbox pod: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SandboxPodPool] Failed to dispose sandbox pod: {Key}", key);
        }
    }

    public void Dispose()
    {
        // StopAsync 已经处理了清理，这里是后备
        foreach (var kv in Boxes)
        {
            try { kv.Value.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch { /* best effort */ }
        }
        Boxes.Clear();
    }
}
