using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services.Sandbox.Kubernetes;

/// <summary>
/// 基于 Kubernetes Pod 的沙盒容器。
/// 每个实例对应一个 K8s Pod，提供命令执行能力。
/// 创建后 Pod 持续运行（sleep infinity），通过 exec API 执行命令。
/// Dispose 时自动删除 Pod。
/// </summary>
internal sealed partial class KubernetesSandboxBox : ISandboxBox
{
    private readonly k8s.Kubernetes _client;
    private readonly string _namespace;
    private readonly string _podName;
    private readonly ILogger _logger;
    private bool _disposed;

    /// <summary>单次 exec 超时（秒）</summary>
    private const int ExecTimeoutSeconds = 120;

    public string Image { get; }

    [GeneratedRegex(@"""ExitCode""\s*,\s*""message""\s*:\s*""(\d+)""")]
    private static partial Regex ExitCodeRegex();

    private KubernetesSandboxBox(
        k8s.Kubernetes client, string ns, string podName, string image, ILogger logger)
    {
        _client = client;
        _namespace = ns;
        _podName = podName;
        Image = image;
        _logger = logger;
    }

    /// <summary>
    /// 创建并启动一个 K8s Pod 作为沙盒容器。
    /// </summary>
    public static async Task<KubernetesSandboxBox> CreateAsync(
        k8s.Kubernetes client,
        string ns,
        string image,
        int cpuMillicores,
        int memoryMib,
        ILogger logger,
        CancellationToken ct = default)
    {
        var podName = $"sandbox-{Guid.NewGuid():N}"[..32]; // K8s name max 63 chars

        var pod = new V1Pod
        {
            Metadata = new V1ObjectMeta
            {
                Name = podName,
                NamespaceProperty = ns,
                Labels = new Dictionary<string, string>
                {
                    ["app.kubernetes.io/managed-by"] = "coresre",
                    ["coresre/component"] = "sandbox",
                },
            },
            Spec = new V1PodSpec
            {
                RestartPolicy = "Never",
                AutomountServiceAccountToken = false,
                Containers =
                [
                    new V1Container
                    {
                        Name = "sandbox",
                        Image = image,
                        Command = ["/bin/sh", "-c", "sleep infinity"],
                        Resources = BuildResources(cpuMillicores, memoryMib),
                        SecurityContext = new V1SecurityContext
                        {
                            // 使用非特权模式运行
                            Privileged = false,
                            AllowPrivilegeEscalation = false,
                        },
                    },
                ],
            },
        };

        logger.LogInformation(
            "Creating sandbox Pod {PodName} in namespace {Namespace} (image={Image})",
            podName, ns, image);

        await client.CoreV1.CreateNamespacedPodAsync(pod, ns, cancellationToken: ct);

        // 等待 Pod Running
        await WaitForPodRunningAsync(client, ns, podName, logger, ct);

        logger.LogInformation("Sandbox Pod {PodName} is running", podName);

        return new KubernetesSandboxBox(client, ns, podName, image, logger);
    }

    public async Task<SandboxExecResult> ExecAsync(string command, params string[] args)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 直接将 command + args 构建为 K8s exec 命令数组。
        // 调用方负责正确拆分命令，例如：
        //   ExecAsync("sh", "-c", "echo hello")  → ["sh", "-c", "echo hello"]
        //   ExecAsync("mkdir", "-p", "/dir")      → ["mkdir", "-p", "/dir"]
        // 【注意】不在此处额外包装 sh -c，否则会双重包装导致命令解析错误。
        var execCommand = new string[1 + args.Length];
        execCommand[0] = command;
        args.CopyTo(execCommand, 1);

        var cmdDisplay = string.Join(' ', execCommand);
        if (cmdDisplay.Length > 200) cmdDisplay = cmdDisplay[..200] + "...";

        _logger.LogInformation(
            "[ExecAsync] START pod={PodName}, command={Command}",
            _podName, cmdDisplay);

        var sw = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(ExecTimeoutSeconds));

        try
        {
            // ──────────────────────────────────────────────────────────────
            // 使用底层 WebSocket exec API + StreamDemuxer。
            //
            // 【不能用 NamespacedPodExecAsync 高级 API】
            // 原因：该 API 的回调中对 MuxedStream 的 CopyToAsync 会永久阻塞。
            // MuxedStream.Read 内部调用 ByteBuffer.Wait()，只有 ByteBuffer.Dispose()
            // 才会解除阻塞，但 NamespacedPodExecAsync 在回调返回后才会 dispose —
            // 形成死锁。
            //
            // 正确做法：
            // 1. 注册 StreamDemuxer.ConnectionClosed 事件
            // 2. 在后台线程中阻塞读取 MuxedStream
            // 3. 命令结束 → 服务端关闭 WebSocket → ConnectionClosed 触发
            // 4. 手动 Dispose stream → 解除 ByteBuffer.Read 阻塞
            // 5. 收集输出数据
            // ──────────────────────────────────────────────────────────────

            _logger.LogDebug("[ExecAsync] Opening WebSocket for pod={PodName}...", _podName);

            var webSocket = await _client.WebSocketNamespacedPodExecAsync(
                name: _podName,
                @namespace: _namespace,
                command: execCommand,
                container: "sandbox",
                stderr: true,
                stdin: false,
                stdout: true,
                tty: false);

            using var demux = new StreamDemuxer(webSocket);

            // 命令完成 → K8s 关闭 WebSocket → demuxer 触发此事件
            var connectionClosed = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            demux.ConnectionClosed += (_, _) => connectionClosed.TrySetResult();

            demux.Start();

            _logger.LogDebug("[ExecAsync] Demuxer started, reading streams...");

            // 获取 stdout(1) / stderr(2) 输出流
            var stdoutStream = demux.GetStream(1, null);
            var stderrStream = demux.GetStream(2, null);

            // MuxedStream.Read 是阻塞调用，必须在后台线程
            var stdoutTask = Task.Run(() => ReadToEnd(stdoutStream), cts.Token);
            var stderrTask = Task.Run(() => ReadToEnd(stderrStream), cts.Token);

            // 等待命令执行完成（服务端关闭 WebSocket）
            await connectionClosed.Task.WaitAsync(cts.Token);

            _logger.LogDebug("[ExecAsync] Connection closed, draining buffers...");

            // 给 ByteBuffer 一点时间排空剩余数据
            await Task.Delay(200, CancellationToken.None);

            // Dispose stream → 设置 ByteBuffer.disposed=true，解除 Read 阻塞
            stdoutStream.Dispose();
            stderrStream.Dispose();

            // 收集输出（Read 已被 Dispose 解除阻塞，应很快返回）
            var stdoutStr = await stdoutTask.WaitAsync(TimeSpan.FromSeconds(5));
            var stderrStr = await stderrTask.WaitAsync(TimeSpan.FromSeconds(5));

            // 从 status channel (channel 3) 读取退出码
            var exitCode = ReadExitCode(demux);

            sw.Stop();
            _logger.LogInformation(
                "[ExecAsync] DONE pod={PodName}, exit={ExitCode}, duration={Duration}ms, " +
                "stdout={StdoutLen}b, stderr={StderrLen}b",
                _podName, exitCode, sw.ElapsedMilliseconds,
                stdoutStr.Length, stderrStr.Length);

            return new SandboxExecResult(exitCode, stdoutStr, stderrStr);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.LogWarning(
                "[ExecAsync] TIMEOUT pod={PodName}, duration={Duration}ms: {Command}",
                _podName, sw.ElapsedMilliseconds, cmdDisplay);
            return new SandboxExecResult(-1, "", $"Exec timeout after {ExecTimeoutSeconds}s");
        }
        catch (KubernetesException kex)
        {
            sw.Stop();
            _logger.LogError(kex,
                "[ExecAsync] K8s error pod={PodName}, duration={Duration}ms, status={Status}: {Command}",
                _podName, sw.ElapsedMilliseconds, kex.Status?.Reason, cmdDisplay);
            return new SandboxExecResult(-1, "", $"K8s error: {kex.Status?.Message ?? kex.Message}");
        }
        catch (Exception ex) when (ex is not ObjectDisposedException)
        {
            sw.Stop();
            _logger.LogError(ex,
                "[ExecAsync] FAILED pod={PodName}, duration={Duration}ms: {Command}",
                _podName, sw.ElapsedMilliseconds, cmdDisplay);
            return new SandboxExecResult(-1, "", $"Exec error: {ex.Message}");
        }
    }

    /// <summary>
    /// 从 MuxedStream 阻塞读取全部数据。
    /// 当 stream 被 Dispose 后，ByteBuffer.Read 返回 0 或抛出 ObjectDisposedException，
    /// 循环退出并返回已读取的数据。
    /// </summary>
    private static string ReadToEnd(Stream stream)
    {
        var sb = new StringBuilder();
        var buf = new byte[8192];
        try
        {
            int read;
            while ((read = stream.Read(buf, 0, buf.Length)) > 0)
            {
                sb.Append(Encoding.UTF8.GetString(buf, 0, read));
            }
        }
        catch (ObjectDisposedException)
        {
            // 预期：stream 被 Dispose 以解除 ByteBuffer.Read 阻塞
        }
        return sb.ToString();
    }

    /// <summary>从 demux status channel (channel 3) 读取退出码</summary>
    private static int ReadExitCode(StreamDemuxer demux)
    {
        Stream? statusStream = null;
        try
        {
            statusStream = demux.GetStream(3, null);
            var buf = new byte[4096];

            // 退出码在 WebSocket 关闭前已写入 ByteBuffer，应立即可读
            // 使用 Task.Run + Wait 添加超时保护，避免万一阻塞
            var readTask = Task.Run(() => statusStream.Read(buf, 0, buf.Length));
            if (!readTask.Wait(TimeSpan.FromSeconds(2)))
                return 0;

            var read = readTask.Result;
            if (read <= 0) return 0;

            var json = Encoding.UTF8.GetString(buf, 0, read);
            if (json.Contains("\"Success\"")) return 0;

            var match = ExitCodeRegex().Match(json);
            return match.Success && int.TryParse(match.Groups[1].Value, out var code) ? code : 1;
        }
        catch { return 0; }
        finally { statusStream?.Dispose(); }
    }

    /// <summary>等待 Pod 进入 Running 状态</summary>
    private static async Task WaitForPodRunningAsync(
        k8s.Kubernetes client, string ns, string podName, ILogger logger,
        CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(120));

        while (!cts.Token.IsCancellationRequested)
        {
            var pod = await client.CoreV1.ReadNamespacedPodAsync(podName, ns, cancellationToken: cts.Token);

            switch (pod.Status?.Phase)
            {
                case "Running":
                    return;
                case "Failed" or "Succeeded":
                    throw new InvalidOperationException(
                        $"Pod {podName} entered terminal phase: {pod.Status.Phase}. " +
                        $"Reason: {pod.Status.Reason ?? "unknown"}");
            }

            // 检查容器状态
            var containerStatus = pod.Status?.ContainerStatuses?.FirstOrDefault();
            if (containerStatus?.State?.Waiting?.Reason is "ImagePullBackOff" or "ErrImagePull" or "CrashLoopBackOff")
            {
                throw new InvalidOperationException(
                    $"Pod {podName} container error: {containerStatus.State.Waiting.Reason} — " +
                    $"{containerStatus.State.Waiting.Message}");
            }

            await Task.Delay(500, cts.Token);
        }

        throw new TimeoutException($"Pod {podName} did not reach Running state within 120s");
    }

    /// <summary>构建资源限制</summary>
    private static V1ResourceRequirements? BuildResources(int cpuMillicores, int memoryMib)
    {
        if (cpuMillicores <= 0 && memoryMib <= 0)
            return null;

        var limits = new Dictionary<string, ResourceQuantity>();
        var requests = new Dictionary<string, ResourceQuantity>();

        if (cpuMillicores > 0)
        {
            limits["cpu"] = new ResourceQuantity($"{cpuMillicores}m");
            // Request = 50% of limit to allow burst
            requests["cpu"] = new ResourceQuantity($"{Math.Max(cpuMillicores / 2, 100)}m");
        }
        if (memoryMib > 0)
        {
            limits["memory"] = new ResourceQuantity($"{memoryMib}Mi");
            requests["memory"] = new ResourceQuantity($"{memoryMib}Mi");
        }

        return new V1ResourceRequirements { Limits = limits, Requests = requests };
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _logger.LogInformation("Deleting sandbox Pod {PodName}", _podName);
            await _client.CoreV1.DeleteNamespacedPodAsync(
                _podName, _namespace,
                gracePeriodSeconds: 0,
                propagationPolicy: "Foreground");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete sandbox Pod {PodName}", _podName);
        }
    }
}
