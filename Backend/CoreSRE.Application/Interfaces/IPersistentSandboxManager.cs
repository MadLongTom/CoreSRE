using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;

namespace CoreSRE.Application.Interfaces;

/// <summary>
/// 持久化沙箱生命周期管理接口。
/// 管理 SandboxInstance 对应的 K8s Pod 创建/启动(含 S3 恢复)/停止(含 S3 持久化)/删除。
/// </summary>
public interface IPersistentSandboxManager
{
    /// <summary>创建沙箱 — 创建 K8s Pod 并等待 Running</summary>
    Task<SandboxInstance> CreateAsync(SandboxInstance sandbox, CancellationToken ct = default);

    /// <summary>启动已停止的沙箱 — 新建 Pod + 从 S3 恢复 /workspace</summary>
    Task StartAsync(SandboxInstance sandbox, CancellationToken ct = default);

    /// <summary>停止运行中的沙箱 — /workspace 同步到 S3 + 删除 Pod</summary>
    Task StopAsync(SandboxInstance sandbox, CancellationToken ct = default);

    /// <summary>终止并删除沙箱 — 删除 Pod + 清理 S3 文件</summary>
    Task TerminateAsync(SandboxInstance sandbox, CancellationToken ct = default);

    /// <summary>在运行中的沙箱内执行命令</summary>
    Task<SandboxExecResult> ExecAsync(SandboxInstance sandbox, string command, string[] args, CancellationToken ct = default);
}

/// <summary>沙箱命令执行结果</summary>
public record SandboxExecResult(int ExitCode, string Stdout, string Stderr);
