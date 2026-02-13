using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Infrastructure.Services.Sandbox.Kubernetes;
using SandboxExecResult = CoreSRE.Application.Interfaces.SandboxExecResult;
using k8s;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services.Sandbox;

/// <summary>
/// 持久化沙箱管理器 — 管理 SandboxInstance 对应的 K8s Pod 全生命周期。
/// 停止时将 /workspace 持久化到 S3；启动时从 S3 恢复。
/// </summary>
public sealed class PersistentSandboxManager : IPersistentSandboxManager
{
    private readonly k8s.Kubernetes _k8sClient;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<PersistentSandboxManager> _logger;
    private readonly ILoggerFactory _loggerFactory;

    private const string Bucket = "coresre-sandboxes";

    public PersistentSandboxManager(
        k8s.Kubernetes k8sClient,
        IFileStorageService fileStorage,
        ILogger<PersistentSandboxManager> logger,
        ILoggerFactory loggerFactory)
    {
        _k8sClient = k8sClient;
        _fileStorage = fileStorage;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public async Task<SandboxInstance> CreateAsync(SandboxInstance sandbox, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating persistent sandbox Pod: {Name}, Image={Image}",
            sandbox.Name, sandbox.Image);

        var box = await KubernetesSandboxBox.CreateAsync(
            _k8sClient,
            sandbox.K8sNamespace,
            sandbox.Image,
            sandbox.CpuCores * 1000,
            sandbox.MemoryMib,
            _loggerFactory.CreateLogger<KubernetesSandboxBox>(),
            ct);

        sandbox.MarkRunning(box.PodName);

        _logger.LogInformation("Persistent sandbox Pod ready: {PodName}", box.PodName);
        return sandbox;
    }

    public async Task StartAsync(SandboxInstance sandbox, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting persistent sandbox: {Name}", sandbox.Name);

        var box = await KubernetesSandboxBox.CreateAsync(
            _k8sClient,
            sandbox.K8sNamespace,
            sandbox.Image,
            sandbox.CpuCores * 1000,
            sandbox.MemoryMib,
            _loggerFactory.CreateLogger<KubernetesSandboxBox>(),
            ct);

        sandbox.MarkRunning(box.PodName);

        // Restore /workspace from S3
        if (sandbox.PersistWorkspace)
        {
            await RestoreWorkspaceAsync(sandbox, box, ct);
        }

        _logger.LogInformation("Persistent sandbox started: {PodName}", box.PodName);
    }

    public async Task StopAsync(SandboxInstance sandbox, CancellationToken ct = default)
    {
        if (sandbox.Status != SandboxStatus.Running || string.IsNullOrEmpty(sandbox.PodName))
        {
            _logger.LogWarning("Cannot stop sandbox {Name}: status={Status}, pod={PodName}",
                sandbox.Name, sandbox.Status, sandbox.PodName);
            return;
        }

        _logger.LogInformation("Stopping persistent sandbox: {Name}, Pod={PodName}",
            sandbox.Name, sandbox.PodName);

        // Persist /workspace to S3
        if (sandbox.PersistWorkspace)
        {
            await PersistWorkspaceAsync(sandbox, ct);
        }

        // Delete Pod
        try
        {
            await _k8sClient.CoreV1.DeleteNamespacedPodAsync(
                sandbox.PodName, sandbox.K8sNamespace,
                gracePeriodSeconds: 5,
                propagationPolicy: "Foreground",
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete Pod {PodName} during stop", sandbox.PodName);
        }

        sandbox.MarkStopped();
        _logger.LogInformation("Persistent sandbox stopped: {Name}", sandbox.Name);
    }

    public async Task TerminateAsync(SandboxInstance sandbox, CancellationToken ct = default)
    {
        _logger.LogInformation("Terminating persistent sandbox: {Name}", sandbox.Name);

        // Delete Pod if running
        if (!string.IsNullOrEmpty(sandbox.PodName))
        {
            try
            {
                await _k8sClient.CoreV1.DeleteNamespacedPodAsync(
                    sandbox.PodName, sandbox.K8sNamespace,
                    gracePeriodSeconds: 0,
                    propagationPolicy: "Foreground",
                    cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete Pod {PodName} during terminate", sandbox.PodName);
            }
        }

        // Clean up S3 workspace files
        try
        {
            await _fileStorage.DeletePrefixAsync(Bucket, $"{sandbox.Id}/", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean S3 workspace for sandbox {Id}", sandbox.Id);
        }

        sandbox.MarkTerminated();
        _logger.LogInformation("Persistent sandbox terminated: {Name}", sandbox.Name);
    }

    public async Task<SandboxExecResult> ExecAsync(
        SandboxInstance sandbox, string command, string[] args, CancellationToken ct = default)
    {
        if (sandbox.Status != SandboxStatus.Running || string.IsNullOrEmpty(sandbox.PodName))
            throw new InvalidOperationException($"Sandbox '{sandbox.Name}' is not running.");

        sandbox.Touch();

        var box = KubernetesSandboxBox.Attach(_k8sClient, sandbox.K8sNamespace, sandbox.PodName,
            sandbox.Image, _loggerFactory.CreateLogger<KubernetesSandboxBox>());

        var result = await box.ExecAsync(command, args);
        return new SandboxExecResult(result.ExitCode, result.Stdout, result.Stderr);
    }

    /// <summary>将 /workspace 目录打包上传到 S3</summary>
    private async Task PersistWorkspaceAsync(SandboxInstance sandbox, CancellationToken ct)
    {
        try
        {
            var box = KubernetesSandboxBox.Attach(_k8sClient, sandbox.K8sNamespace, sandbox.PodName!,
                sandbox.Image, _loggerFactory.CreateLogger<KubernetesSandboxBox>());

            // tar czf - -C /workspace . — to stdout
            var result = await box.ExecAsync("tar", "czf", "-", "-C", "/workspace", ".");
            if (result.ExitCode != 0)
            {
                _logger.LogWarning("Workspace tar failed (exit={ExitCode}): {Stderr}",
                    result.ExitCode, result.Stderr);
                return;
            }

            var key = $"{sandbox.Id}/workspace.tar.gz";
            using var stream = new MemoryStream(System.Text.Encoding.Latin1.GetBytes(result.Stdout));
            await _fileStorage.UploadAsync(Bucket, key, stream, "application/gzip", ct);

            _logger.LogInformation("Persisted workspace for sandbox {Name} ({Size} bytes)",
                sandbox.Name, stream.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist workspace for sandbox {Name}", sandbox.Name);
        }
    }

    /// <summary>从 S3 下载打包文件并解压到 /workspace</summary>
    private async Task RestoreWorkspaceAsync(SandboxInstance sandbox, KubernetesSandboxBox box, CancellationToken ct)
    {
        try
        {
            var key = $"{sandbox.Id}/workspace.tar.gz";
            var exists = await _fileStorage.ExistsAsync(Bucket, key, ct);
            if (!exists)
            {
                _logger.LogDebug("No workspace archive found for sandbox {Name}", sandbox.Name);
                return;
            }

            // 先确保 /workspace 目录存在
            await box.ExecAsync("mkdir", "-p", "/workspace");

            // 下载到容器内
            using var stream = await _fileStorage.DownloadAsync(Bucket, key, ct);
            var bytes = new byte[stream.Length];
            await stream.ReadExactlyAsync(bytes, ct);

            // base64 编码传输避免二进制转义问题
            var base64 = Convert.ToBase64String(bytes);
            await box.ExecAsync("sh", "-c",
                $"echo '{base64}' | base64 -d | tar xzf - -C /workspace");

            _logger.LogInformation("Restored workspace for sandbox {Name} ({Size} bytes)",
                sandbox.Name, bytes.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore workspace for sandbox {Name}", sandbox.Name);
        }
    }
}
