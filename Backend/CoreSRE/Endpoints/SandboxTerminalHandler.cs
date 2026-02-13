using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CoreSRE.Domain.Interfaces;
using k8s;

namespace CoreSRE.Endpoints;

/// <summary>
/// 沙箱 Web Terminal — 通过 WebSocket 双向中继实现浏览器内交互式终端。
///
/// 协议：
///   Client → Server:
///     - Text  帧 = stdin 数据（UTF-8）
///     - Binary 帧 = 控制消息：[0x01, cols_hi, cols_lo, rows_hi, rows_lo] = resize
///   Server → Client:
///     - Binary 帧 = stdout 数据
///
/// K8s 侧使用 exec API（stdin=true, tty=true）打开交互式 shell，
/// 通过 StreamDemuxer 进行多路复用读写。
/// </summary>
public static class SandboxTerminalHandler
{
    /// <summary>最大终端会话时长</summary>
    private static readonly TimeSpan MaxSessionDuration = TimeSpan.FromHours(4);

    /// <summary>WebSocket keepalive 间隔</summary>
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(30);

    public static async Task HandleAsync(
        HttpContext context,
        Guid id,
        ISandboxInstanceRepository repository,
        k8s.Kubernetes k8sClient,
        ILogger<Program> logger)
    {
        // ── 1. 验证 WebSocket 请求 ──
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket connection required.");
            return;
        }

        // ── 2. 查询沙箱并验证状态 ──
        var sandbox = await repository.GetByIdAsync(id, context.RequestAborted);
        if (sandbox is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("Sandbox not found.");
            return;
        }

        if (sandbox.Status != Domain.Enums.SandboxStatus.Running || string.IsNullOrEmpty(sandbox.PodName))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync($"Sandbox is not Running (current: {sandbox.Status}).");
            return;
        }

        // ── 3. 接受 WebSocket 连接 ──
        using var clientSocket = await context.WebSockets.AcceptWebSocketAsync();
        logger.LogInformation(
            "Terminal WebSocket connected: sandbox={Name}, pod={PodName}",
            sandbox.Name, sandbox.PodName);

        try
        {
            await RelayAsync(clientSocket, k8sClient, sandbox.K8sNamespace, sandbox.PodName, logger, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Terminal session cancelled: sandbox={Name}", sandbox.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Terminal session error: sandbox={Name}", sandbox.Name);
        }
        finally
        {
            logger.LogInformation("Terminal WebSocket disconnected: sandbox={Name}", sandbox.Name);
        }
    }

    /// <summary>
    /// 双向中继：浏览器 WebSocket ↔ K8s Pod exec WebSocket
    /// </summary>
    private static async Task RelayAsync(
        WebSocket clientSocket,
        k8s.Kubernetes k8sClient,
        string k8sNamespace,
        string podName,
        ILogger logger,
        CancellationToken ct)
    {
        // ── 打开 K8s exec WebSocket（交互式 shell + TTY） ──
        var k8sWebSocket = await k8sClient.WebSocketNamespacedPodExecAsync(
            name: podName,
            @namespace: k8sNamespace,
            command: ["/bin/sh"],
            container: "sandbox",
            stderr: true,
            stdin: true,
            stdout: true,
            tty: true);

        using var demux = new StreamDemuxer(k8sWebSocket);

        // K8s exec 完成时触发（shell 退出或 Pod 被删除）
        var connectionClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        demux.ConnectionClosed += (_, _) => connectionClosed.TrySetResult();

        demux.Start();
        logger.LogDebug("K8s exec demuxer started for pod={PodName}", podName);

        // 获取 stdin(写通道0) / stdout(读通道1) 流
        var stdinStream = demux.GetStream((byte?)null, (byte?)0);
        var stdoutStream = demux.GetStream((byte?)1, (byte?)null);

        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        sessionCts.CancelAfter(MaxSessionDuration);

        // ── 任务 1：K8s stdout → 客户端 WebSocket ──
        // MuxedStream.Read 是阻塞调用，必须在后台线程运行
        var k8sToClient = Task.Run(async () =>
        {
            var buffer = new byte[4096];
            try
            {
                while (!sessionCts.IsCancellationRequested)
                {
                    // 阻塞读取 K8s stdout
                    var read = stdoutStream.Read(buffer, 0, buffer.Length);
                    if (read <= 0) break;

                    if (clientSocket.State == WebSocketState.Open)
                    {
                        await clientSocket.SendAsync(
                            new ArraySegment<byte>(buffer, 0, read),
                            WebSocketMessageType.Binary,
                            endOfMessage: true,
                            sessionCts.Token);
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch (ObjectDisposedException) { /* stream disposed to unblock */ }
            catch (WebSocketException) { /* client disconnected */ }
            catch (OperationCanceledException) { /* session ended */ }
        }, sessionCts.Token);

        // ── 任务 2：客户端 WebSocket → K8s stdin ──
        var clientToK8s = Task.Run(async () =>
        {
            var buffer = new byte[4096];
            try
            {
                while (!sessionCts.IsCancellationRequested && clientSocket.State == WebSocketState.Open)
                {
                    var result = await clientSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), sessionCts.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    if (result.MessageType == WebSocketMessageType.Text && result.Count > 0)
                    {
                        // Text 帧 = stdin 数据
                        stdinStream.Write(buffer, 0, result.Count);
                        stdinStream.Flush();
                    }
                    else if (result.MessageType == WebSocketMessageType.Binary && result.Count >= 5 && buffer[0] == 0x01)
                    {
                        // Binary 帧, 类型 0x01 = resize
                        var cols = (buffer[1] << 8) | buffer[2];
                        var rows = (buffer[3] << 8) | buffer[4];

                        if (cols is > 0 and <= 500 && rows is > 0 and <= 200)
                        {
                            try
                            {
                                // K8s resize channel (channel 4): JSON {"Width":cols,"Height":rows}
                                var resizeJson = JsonSerializer.Serialize(new { Width = cols, Height = rows });
                                var resizeBytes = Encoding.UTF8.GetBytes(resizeJson);
                                var resizeStream = demux.GetStream((byte?)null, (byte?)4);
                                resizeStream.Write(resizeBytes, 0, resizeBytes.Length);
                                resizeStream.Flush();

                                logger.LogDebug("Terminal resized: {Cols}x{Rows} for pod={PodName}",
                                    cols, rows, podName);
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex, "Failed to send resize for pod={PodName}", podName);
                            }
                        }
                    }
                }
            }
            catch (WebSocketException) { /* client disconnected */ }
            catch (OperationCanceledException) { /* session ended */ }
        }, sessionCts.Token);

        // ── 等待任一方断开或 K8s 连接关闭 ──
        await Task.WhenAny(clientToK8s, k8sToClient, connectionClosed.Task);

        // 触发清理
        await sessionCts.CancelAsync();

        // Dispose stdout stream → 解除 MuxedStream.Read 阻塞
        stdoutStream.Dispose();

        // 等待任务结束（有超时保护）
        try
        {
            await Task.WhenAll(clientToK8s, k8sToClient).WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch { /* ignore timeout */ }

        // 优雅关闭客户端 WebSocket
        if (clientSocket.State == WebSocketState.Open)
        {
            try
            {
                await clientSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Terminal session ended",
                    CancellationToken.None);
            }
            catch { /* best effort */ }
        }
    }
}
