namespace CoreSRE.Application.Sandboxes;

/// <summary>
/// 沙箱操作异常 — 封装 K8s Pod 创建/启动/停止过程中的基础设施错误，
/// 提供用户可理解的错误信息和排查建议。
/// </summary>
public class SandboxOperationException : Exception
{
    /// <summary>操作类型（Create / Start / Stop / Exec）</summary>
    public string Operation { get; }

    /// <summary>沙箱名称</summary>
    public string SandboxName { get; }

    /// <summary>用户可读的排查建议列表</summary>
    public IReadOnlyList<string> Hints { get; }

    public SandboxOperationException(
        string operation,
        string sandboxName,
        string message,
        IReadOnlyList<string>? hints = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Operation = operation;
        SandboxName = sandboxName;
        Hints = hints ?? [];
    }

    /// <summary>
    /// 从基础设施异常推断用户友好的错误信息和排查建议。
    /// </summary>
    public static SandboxOperationException FromInfrastructureError(
        string operation, string sandboxName, string image, Exception ex)
    {
        var msg = ex.Message;
        var hints = new List<string>();

        // ── Image pull errors ──
        if (msg.Contains("ErrImagePull", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("ImagePullBackOff", StringComparison.OrdinalIgnoreCase))
        {
            hints.Add($"镜像 \"{image}\" 无法拉取。请检查：");
            hints.Add("  1. 镜像名称是否正确（例如应使用 ubuntu:22.04 而非 ubuntu-22.04）");
            hints.Add("  2. 如果是私有镜像，请确认 K8s 集群已配置 imagePullSecret");
            hints.Add("  3. 如果使用本地镜像，请将 imagePullPolicy 设置为 IfNotPresent 或 Never");
            hints.Add("  4. 检查网络是否能访问容器注册表（docker.io / ghcr.io 等）");

            return new SandboxOperationException(
                operation, sandboxName,
                $"镜像拉取失败：{image}",
                hints, ex);
        }

        // ── CrashLoopBackOff ──
        if (msg.Contains("CrashLoopBackOff", StringComparison.OrdinalIgnoreCase))
        {
            hints.Add($"容器启动后立即退出（CrashLoopBackOff）。请检查：");
            hints.Add("  1. 镜像是否支持 sleep infinity 命令（某些极简镜像可能缺少 /bin/sh）");
            hints.Add("  2. 尝试使用标准镜像如 ubuntu:22.04 或 python:3.12-slim");
            hints.Add("  3. 资源限制（CPU/内存）是否过低导致 OOM");

            return new SandboxOperationException(
                operation, sandboxName,
                $"容器反复崩溃：{image}",
                hints, ex);
        }

        // ── Timeout ──
        if (ex is TimeoutException)
        {
            hints.Add("Pod 在 120 秒内未进入 Running 状态。请检查：");
            hints.Add("  1. K8s 集群是否有足够的资源（CPU/内存）调度 Pod");
            hints.Add("  2. 镜像是否过大导致拉取超时");
            hints.Add("  3. 使用 kubectl get pods -n <namespace> 查看 Pod 事件");

            return new SandboxOperationException(
                operation, sandboxName,
                "沙箱启动超时",
                hints, ex);
        }

        // ── K8s API errors ──
        if (msg.Contains("Forbidden", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
        {
            hints.Add("K8s API 权限不足。请检查：");
            hints.Add("  1. ServiceAccount 是否有权创建/读取/删除 Pod");
            hints.Add("  2. RBAC Role/ClusterRole 配置是否正确");
            hints.Add("  3. kubeconfig 凭证是否过期");

            return new SandboxOperationException(
                operation, sandboxName,
                "Kubernetes 权限不足",
                hints, ex);
        }

        if (msg.Contains("not found", StringComparison.OrdinalIgnoreCase) &&
            msg.Contains("namespace", StringComparison.OrdinalIgnoreCase))
        {
            hints.Add($"K8s 命名空间不存在。请先创建命名空间或使用已有的命名空间。");

            return new SandboxOperationException(
                operation, sandboxName,
                "Kubernetes 命名空间不存在",
                hints, ex);
        }

        // ── Fallback: unknown infrastructure error ──
        hints.Add("请查看后端日志获取详细错误信息");
        hints.Add("使用 kubectl describe pod <pod-name> 查看 Pod 事件");

        return new SandboxOperationException(
            operation, sandboxName,
            $"沙箱操作失败：{msg}",
            hints, ex);
    }
}
