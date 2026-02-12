namespace CoreSRE.Infrastructure.Services.Sandbox.Kubernetes;

/// <summary>
/// 沙盒执行结果。
/// </summary>
internal sealed record SandboxExecResult(int ExitCode, string Stdout, string Stderr);

/// <summary>
/// 沙盒容器的抽象接口。
/// 实现：<see cref="KubernetesSandboxBox"/> — 通过 K8s API 管理 Pod。
/// </summary>
internal interface ISandboxBox : IAsyncDisposable
{
    /// <summary>OCI 镜像名</summary>
    string Image { get; }

    /// <summary>
    /// 在容器内执行命令并等待完成。返回 exit code + stdout + stderr。
    /// </summary>
    Task<SandboxExecResult> ExecAsync(string command, params string[] args);
}
