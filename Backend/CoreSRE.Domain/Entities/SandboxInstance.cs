using CoreSRE.Domain.Enums;

namespace CoreSRE.Domain.Entities;

/// <summary>
/// 持久化沙箱实例 — 独立于对话的有状态 K8s Pod。
/// 状态机: Creating → Running → Stopped ⇄ Running → Terminated
/// </summary>
public class SandboxInstance : BaseEntity
{
    /// <summary>沙箱名称</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>沙箱状态</summary>
    public SandboxStatus Status { get; private set; } = SandboxStatus.Creating;

    /// <summary>沙箱类型 (SimpleBox / CodeBox / InteractiveBox ...)</summary>
    public string SandboxType { get; private set; } = "SimpleBox";

    /// <summary>容器镜像</summary>
    public string Image { get; private set; } = "alpine:latest";

    /// <summary>CPU 核数</summary>
    public int CpuCores { get; private set; } = 1;

    /// <summary>内存 MiB</summary>
    public int MemoryMib { get; private set; } = 512;

    /// <summary>K8s 命名空间</summary>
    public string K8sNamespace { get; private set; } = "coresre-sandbox";

    /// <summary>无活动自动停止时间（分钟），0 表示不自动停止</summary>
    public int AutoStopMinutes { get; private set; } = 30;

    /// <summary>是否在停止时持久化 /workspace 到 S3</summary>
    public bool PersistWorkspace { get; private set; } = true;

    /// <summary>关联的 Agent ID（可选）</summary>
    public Guid? AgentId { get; private set; }

    /// <summary>最后活动时间</summary>
    public DateTime? LastActivityAt { get; private set; }

    /// <summary>K8s Pod 名称（Running 时有值）</summary>
    public string? PodName { get; private set; }

    private SandboxInstance() { }

    /// <summary>创建沙箱实例</summary>
    public static SandboxInstance Create(
        string name,
        string sandboxType = "SimpleBox",
        string? image = null,
        int cpuCores = 1,
        int memoryMib = 512,
        string k8sNamespace = "coresre-sandbox",
        int autoStopMinutes = 30,
        bool persistWorkspace = true,
        Guid? agentId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new SandboxInstance
        {
            Name = name.Trim(),
            Status = SandboxStatus.Creating,
            SandboxType = sandboxType,
            Image = image ?? ResolveDefaultImage(sandboxType),
            CpuCores = cpuCores,
            MemoryMib = memoryMib,
            K8sNamespace = k8sNamespace,
            AutoStopMinutes = autoStopMinutes,
            PersistWorkspace = persistWorkspace,
            AgentId = agentId,
        };
    }

    /// <summary>标记为 Running（Pod 就绪后调用）</summary>
    public void MarkRunning(string podName)
    {
        if (Status is not SandboxStatus.Creating and not SandboxStatus.Stopped)
            throw new InvalidOperationException($"Cannot start sandbox in {Status} state.");

        Status = SandboxStatus.Running;
        PodName = podName;
        LastActivityAt = DateTime.UtcNow;
    }

    /// <summary>标记为 Stopped</summary>
    public void MarkStopped()
    {
        if (Status != SandboxStatus.Running)
            throw new InvalidOperationException($"Cannot stop sandbox in {Status} state.");

        Status = SandboxStatus.Stopped;
        PodName = null;
    }

    /// <summary>标记为 Terminated（永久删除前）</summary>
    public void MarkTerminated()
    {
        Status = SandboxStatus.Terminated;
        PodName = null;
    }

    /// <summary>记录活动时间</summary>
    public void Touch() => LastActivityAt = DateTime.UtcNow;

    /// <summary>更新配置（仅 Stopped 状态可修改）</summary>
    public void UpdateConfig(
        string name,
        string? image = null,
        int? cpuCores = null,
        int? memoryMib = null,
        int? autoStopMinutes = null,
        bool? persistWorkspace = null,
        Guid? agentId = null)
    {
        if (Status != SandboxStatus.Stopped)
            throw new InvalidOperationException("Sandbox config can only be updated in Stopped state.");

        if (!string.IsNullOrWhiteSpace(name)) Name = name.Trim();
        if (image is not null) Image = image;
        if (cpuCores is not null) CpuCores = cpuCores.Value;
        if (memoryMib is not null) MemoryMib = memoryMib.Value;
        if (autoStopMinutes is not null) AutoStopMinutes = autoStopMinutes.Value;
        if (persistWorkspace is not null) PersistWorkspace = persistWorkspace.Value;
        AgentId = agentId;
    }

    /// <summary>是否超时无活动</summary>
    public bool IsInactive =>
        AutoStopMinutes > 0
        && Status == SandboxStatus.Running
        && LastActivityAt.HasValue
        && DateTime.UtcNow - LastActivityAt.Value > TimeSpan.FromMinutes(AutoStopMinutes);

    private static string ResolveDefaultImage(string sandboxType) => sandboxType switch
    {
        "SimpleBox" => "alpine:latest",
        "CodeBox" => "python:3.12-slim",
        "InteractiveBox" => "python:3.12-slim",
        "BrowserBox" => "mcr.microsoft.com/playwright:v1.52.0-jammy",
        _ => "python:3.12-slim"
    };
}
