using System.Text.Json;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using k8s;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services.Sandbox.Kubernetes;

// =============================================================================
// Kubernetes 沙盒工具提供者
//
// 在 Kubernetes Pod（OCI 容器 + namespace/cgroup 隔离）中执行所有操作。
// 配置（镜像、CPU、内存、沙盒类型、K8s namespace）来自 Agent 的 LlmConfigVO。
// 使用 KubernetesClient NuGet 包通过 K8s API 管理 Pod 生命周期。
//
// Docker Desktop 集成的 K8s 环境开箱即用：
//   kubectl config use-context docker-desktop
// =============================================================================

/// <summary>
/// 基于 Kubernetes Pod 的沙盒工具提供者。
/// Agent 的所有命令/代码执行都在隔离的 K8s 容器中进行。
/// Pod 池由 SandboxPodPool（Singleton + IHostedService）统一管理生命周期。
/// </summary>
public sealed class KubernetesSandboxToolProvider : ISandboxToolProvider
{
    private readonly ILogger<KubernetesSandboxToolProvider> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly k8s.Kubernetes _k8sClient;
    private readonly SandboxPodPool _podPool;
    private readonly ISandboxInstanceRepository _sandboxRepo;

    public KubernetesSandboxToolProvider(
        ILogger<KubernetesSandboxToolProvider> logger,
        ILoggerFactory loggerFactory,
        k8s.Kubernetes k8sClient,
        SandboxPodPool podPool,
        ISandboxInstanceRepository sandboxRepo)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _k8sClient = k8sClient;
        _podPool = podPool;
        _sandboxRepo = sandboxRepo;
    }

    public IReadOnlyList<AIFunction> CreateSandboxTools(
        Guid agentId, string conversationId, LlmConfigVO llmConfig)
    {
        var boxType = Enum.TryParse<SandboxType>(llmConfig.SandboxType, ignoreCase: true, out var parsed)
            ? parsed
            : SandboxType.CodeBox;

        // 从 Agent 配置中读取容器参数
        var image = !string.IsNullOrWhiteSpace(llmConfig.SandboxImage)
            ? llmConfig.SandboxImage
            : ResolveDefaultImage(boxType);
        var cpuMillicores = (llmConfig.SandboxCpus ?? 1) * 1000; // CPU 核数 → 毫核
        var memoryMib = llmConfig.SandboxMemoryMib ?? 512;
        var ns = !string.IsNullOrWhiteSpace(llmConfig.SandboxK8sNamespace)
            ? llmConfig.SandboxK8sNamespace
            : "coresre-sandbox";

        // 判断沙箱模式：Persistent 使用已有 Pod，Ephemeral 创建临时 Pod
        var sandboxMode = llmConfig.SandboxMode;
        ISandboxBox box;

        if (string.Equals(sandboxMode, "Persistent", StringComparison.OrdinalIgnoreCase)
            && llmConfig.SandboxInstanceId is not null)
        {
            // Persistent 模式 — 使用已存在的持久化沙箱 Pod
            var sandboxInstance = _sandboxRepo.GetByIdAsync(llmConfig.SandboxInstanceId.Value)
                .GetAwaiter().GetResult();

            if (sandboxInstance is null || sandboxInstance.Status != Domain.Enums.SandboxStatus.Running
                || string.IsNullOrEmpty(sandboxInstance.PodName))
            {
                _logger.LogWarning(
                    "Persistent sandbox {SandboxId} not available, falling back to ephemeral Pod",
                    llmConfig.SandboxInstanceId);
                box = CreateEphemeralBox(agentId, conversationId, image, cpuMillicores, memoryMib, ns, boxType);
            }
            else
            {
                _logger.LogInformation(
                    "Using persistent sandbox Pod={PodName} for Agent={AgentId}",
                    sandboxInstance.PodName, agentId);

                sandboxInstance.Touch();
                _sandboxRepo.UpdateAsync(sandboxInstance).GetAwaiter().GetResult();

                box = KubernetesSandboxBox.Attach(
                    _k8sClient, sandboxInstance.K8sNamespace, sandboxInstance.PodName,
                    sandboxInstance.Image, _loggerFactory.CreateLogger<KubernetesSandboxBox>());
            }
        }
        else
        {
            // Ephemeral 模式 — 每对话创建/销毁 Pod（原有行为）
            box = CreateEphemeralBox(agentId, conversationId, image, cpuMillicores, memoryMib, ns, boxType);
        }

        var toolLogger = _loggerFactory.CreateLogger("CoreSRE.Sandbox.K8s");

        // ── 基础工具（所有 BoxType 共享） ──
        var tools = new List<AIFunction>
        {
            new SandboxExecAIFunction(box, toolLogger),
            new SandboxReadFileAIFunction(box, toolLogger),
            new SandboxWriteFileAIFunction(box, toolLogger),
            new SandboxListDirectoryAIFunction(box, toolLogger),
        };

        // ── CodeBox+ 追加代码执行和包安装 ──
        if (boxType >= SandboxType.CodeBox)
        {
            tools.Add(new SandboxRunCodeAIFunction(box, toolLogger));
            tools.Add(new SandboxInstallPackageAIFunction(box, toolLogger));
        }

        _logger.LogInformation(
            "K8s {BoxType} sandbox: {ToolCount} tools for Agent={AgentId}",
            boxType, tools.Count, agentId);

        return tools;
    }

    /// <summary>根据 BoxType 选择默认 OCI 镜像</summary>
    private static string ResolveDefaultImage(SandboxType boxType) => boxType switch
    {
        SandboxType.SimpleBox => "alpine:latest",
        SandboxType.CodeBox => "python:3.12-slim",
        SandboxType.InteractiveBox => "python:3.12-slim",
        SandboxType.BrowserBox => "mcr.microsoft.com/playwright:v1.52.0-jammy",
        SandboxType.ComputerBox => "python:3.12-slim",
        _ => "python:3.12-slim"
    };

    /// <summary>创建临时沙箱 Pod（原有 Ephemeral 行为）</summary>
    private ISandboxBox CreateEphemeralBox(
        Guid agentId, string conversationId, string image,
        int cpuMillicores, int memoryMib, string ns, SandboxType boxType)
    {
        var key = $"{agentId:N}/{conversationId}";
        return _podPool.Boxes.GetOrAdd(key, _ =>
        {
            _logger.LogInformation(
                "Creating ephemeral K8s sandbox Pod for Agent={AgentId}, Conversation={ConversationId}, " +
                "Image={Image}, Cpus={Cpus}m, Memory={Memory}MiB, Namespace={Namespace}, Type={BoxType}",
                agentId, conversationId, image, cpuMillicores, memoryMib, ns, boxType);

            return KubernetesSandboxBox.CreateAsync(
                _k8sClient, ns, image, cpuMillicores, memoryMib,
                _loggerFactory.CreateLogger<KubernetesSandboxBox>())
                .GetAwaiter().GetResult();
        });
    }
}

// =============================================================================
// Sandbox AIFunction 工具 — 所有命令通过 K8s exec API 在 Pod 容器内执行
// =============================================================================

/// <summary>在容器中执行 shell 命令</summary>
internal sealed class SandboxExecAIFunction : AIFunction
{
    private readonly ISandboxBox _box;
    private readonly ILogger _logger;

    private static readonly JsonElement _schema = JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "command": {
                "type": "string",
                "description": "要执行的 shell 命令"
            },
            "working_directory": {
                "type": "string",
                "description": "工作目录（容器内路径，默认 /workspace）"
            }
        },
        "required": ["command"]
    }
    """).RootElement.Clone();

    public SandboxExecAIFunction(ISandboxBox box, ILogger logger)
    { _box = box; _logger = logger; }

    public override string Name => "run_command";
    public override string Description =>
        "在隔离的 Kubernetes 容器中执行 shell 命令。每个对话拥有独立的 Pod，" +
        "提供容器级别的安全隔离（独立文件系统、网络栈、资源限制）。";
    public override JsonElement JsonSchema => _schema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var command = ArgHelper.GetString(arguments.TryGetValue("command", out var cObj) ? cObj : null);
        if (string.IsNullOrWhiteSpace(command))
            return JsonSerializer.Serialize(new { error = "参数 'command' 不能为空" });

        var workDir = ArgHelper.GetString(arguments.TryGetValue("working_directory", out var wObj) ? wObj : null);

        try
        {
            var fullCommand = workDir is not null
                ? $"cd {workDir} && {command}"
                : command;

            var result = await _box.ExecAsync("sh", "-c", fullCommand);

            _logger.LogInformation(
                "K8s run_command: exit={ExitCode}, cmd={Command}",
                result.ExitCode, command.Length > 100 ? command[..100] + "..." : command);

            return JsonSerializer.Serialize(new
            {
                exitCode = result.ExitCode,
                stdout = Truncate(result.Stdout),
                stderr = Truncate(result.Stderr),
                isolated = true,
                runtime = "kubernetes-pod"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "K8s run_command failed");
            return JsonSerializer.Serialize(new { error = $"容器执行失败: {ex.Message}" });
        }
    }

    private static string Truncate(string s, int max = 50_000) =>
        s.Length <= max ? s : s[..max] + $"\n\n... [截断，共 {s.Length} 字符]";
}

/// <summary>在容器中读取文件内容</summary>
internal sealed class SandboxReadFileAIFunction : AIFunction
{
    private readonly ISandboxBox _box;
    private readonly ILogger _logger;

    private static readonly JsonElement _schema = JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "path": {
                "type": "string",
                "description": "文件路径（容器内绝对路径或相对于 /workspace 的路径）"
            },
            "max_lines": {
                "type": "integer",
                "description": "最大读取行数（可选，默认全部）"
            }
        },
        "required": ["path"]
    }
    """).RootElement.Clone();

    public SandboxReadFileAIFunction(ISandboxBox box, ILogger logger)
    { _box = box; _logger = logger; }

    public override string Name => "read_file";
    public override string Description =>
        "读取容器内的文件内容。支持文本文件和二进制文件（base64）。";
    public override JsonElement JsonSchema => _schema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var path = ArgHelper.GetString(arguments.TryGetValue("path", out var pObj) ? pObj : null);
        if (string.IsNullOrWhiteSpace(path))
            return JsonSerializer.Serialize(new { error = "参数 'path' 不能为空" });

        var maxLines = ArgHelper.GetInt(arguments.TryGetValue("max_lines", out var mlObj) ? mlObj : null);

        try
        {
            var cmd = maxLines.HasValue
                ? $"head -n {maxLines.Value} '{path}'"
                : $"cat '{path}'";

            var result = await _box.ExecAsync("sh", "-c", cmd);

            if (result.ExitCode != 0)
                return JsonSerializer.Serialize(new
                {
                    error = $"读取失败: {result.Stderr.Trim()}",
                    exitCode = result.ExitCode
                });

            return JsonSerializer.Serialize(new
            {
                path,
                content = result.Stdout.Length > 1_048_576
                    ? result.Stdout[..1_048_576] + "\n... [截断]"
                    : result.Stdout,
                exitCode = result.ExitCode
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"容器读取失败: {ex.Message}" });
        }
    }
}

/// <summary>在容器中写入文件</summary>
internal sealed class SandboxWriteFileAIFunction : AIFunction
{
    private readonly ISandboxBox _box;
    private readonly ILogger _logger;

    private static readonly JsonElement _schema = JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "path": {
                "type": "string",
                "description": "文件路径（容器内路径）"
            },
            "content": {
                "type": "string",
                "description": "文件内容"
            },
            "append": {
                "type": "boolean",
                "description": "是否追加而非覆盖（默认 false）"
            }
        },
        "required": ["path", "content"]
    }
    """).RootElement.Clone();

    public SandboxWriteFileAIFunction(ISandboxBox box, ILogger logger)
    { _box = box; _logger = logger; }

    public override string Name => "write_file";
    public override string Description =>
        "在容器中创建或写入文件。自动创建父目录。";
    public override JsonElement JsonSchema => _schema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var path = ArgHelper.GetString(arguments.TryGetValue("path", out var pObj) ? pObj : null);
        var content = ArgHelper.GetString(arguments.TryGetValue("content", out var cObj) ? cObj : null);
        var append = ArgHelper.GetBool(arguments.TryGetValue("append", out var aObj) ? aObj : null) ?? false;

        if (string.IsNullOrWhiteSpace(path))
            return JsonSerializer.Serialize(new { error = "参数 'path' 不能为空" });
        if (content is null)
            return JsonSerializer.Serialize(new { error = "参数 'content' 不能为空" });

        if (content.Length > 10_485_760)
            return JsonSerializer.Serialize(new { error = "内容超过最大写入限制 (10 MB)" });

        try
        {
            // 确保父目录存在
            var dir = path.Contains('/') ? path[..path.LastIndexOf('/')] : null;
            if (dir is not null)
                await _box.ExecAsync("mkdir", "-p", dir);

            // 通过 base64 编码传输内容，避免 shell 特殊字符问题
            var op = append ? ">>" : ">";
            var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content));
            var cmd = $"echo '{base64}' | base64 -d {op} '{path}'";
            var result = await _box.ExecAsync("sh", "-c", cmd);

            if (result.ExitCode != 0)
                return JsonSerializer.Serialize(new { error = result.Stderr.Trim() });

            return JsonSerializer.Serialize(new
            {
                success = true,
                path,
                bytes = content.Length,
                append
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"容器写入失败: {ex.Message}" });
        }
    }
}

/// <summary>在容器中列出目录内容</summary>
internal sealed class SandboxListDirectoryAIFunction : AIFunction
{
    private readonly ISandboxBox _box;
    private readonly ILogger _logger;

    private static readonly JsonElement _schema = JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "path": {
                "type": "string",
                "description": "目录路径（默认 /workspace）"
            },
            "recursive": {
                "type": "boolean",
                "description": "是否递归列出子目录（默认 false）"
            }
        }
    }
    """).RootElement.Clone();

    public SandboxListDirectoryAIFunction(ISandboxBox box, ILogger logger)
    { _box = box; _logger = logger; }

    public override string Name => "list_directory";
    public override string Description =>
        "列出容器中指定目录的文件和子目录。";
    public override JsonElement JsonSchema => _schema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var path = ArgHelper.GetString(arguments.TryGetValue("path", out var pObj) ? pObj : null) ?? "/workspace";
        var recursive = ArgHelper.GetBool(arguments.TryGetValue("recursive", out var rObj) ? rObj : null) ?? false;

        try
        {
            var cmd = recursive
                ? $"find '{path}' -maxdepth 3 -type f -o -type d | head -500"
                : $"ls -la '{path}'";

            var result = await _box.ExecAsync("sh", "-c", cmd);

            return JsonSerializer.Serialize(new
            {
                path,
                listing = result.Stdout,
                exitCode = result.ExitCode,
                error = result.ExitCode != 0 ? result.Stderr.Trim() : null
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"容器列目录失败: {ex.Message}" });
        }
    }
}

/// <summary>在容器中执行代码片段</summary>
internal sealed class SandboxRunCodeAIFunction : AIFunction
{
    private readonly ISandboxBox _box;
    private readonly ILogger _logger;

    private static readonly JsonElement _schema = JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "code": {
                "type": "string",
                "description": "要执行的代码内容"
            },
            "language": {
                "type": "string",
                "enum": ["python", "javascript", "bash", "sh"],
                "description": "编程语言"
            }
        },
        "required": ["code", "language"]
    }
    """).RootElement.Clone();

    public SandboxRunCodeAIFunction(ISandboxBox box, ILogger logger)
    { _box = box; _logger = logger; }

    public override string Name => "execute_code";
    public override string Description =>
        "在隔离的 Kubernetes 容器中执行代码片段。支持 Python、JavaScript、Bash。" +
        "代码在独立容器中运行，提供资源隔离和安全边界。";
    public override JsonElement JsonSchema => _schema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var code = ArgHelper.GetString(arguments.TryGetValue("code", out var cObj) ? cObj : null);
        var language = ArgHelper.GetString(arguments.TryGetValue("language", out var lObj) ? lObj : null);

        if (string.IsNullOrWhiteSpace(code))
            return JsonSerializer.Serialize(new { error = "参数 'code' 不能为空" });
        if (string.IsNullOrWhiteSpace(language))
            return JsonSerializer.Serialize(new { error = "参数 'language' 不能为空" });

        try
        {
            // 先将代码写入临时文件
            var ext = language.ToLowerInvariant() switch
            {
                "python" => ".py",
                "javascript" or "js" => ".js",
                "bash" or "sh" => ".sh",
                _ => ".txt"
            };
            var tempFile = $"/tmp/code_{Guid.NewGuid():N}{ext}";
            var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(code));
            await _box.ExecAsync("sh", "-c", $"echo '{base64}' | base64 -d > '{tempFile}'");

            // 选择运行时
            var runner = language.ToLowerInvariant() switch
            {
                "python" => $"python3 '{tempFile}'",
                "javascript" or "js" => $"node '{tempFile}'",
                "bash" or "sh" => $"bash '{tempFile}'",
                _ => null
            };

            if (runner is null)
                return JsonSerializer.Serialize(new { error = $"不支持的语言: {language}" });

            var result = await _box.ExecAsync("sh", "-c", runner);

            // 清理临时文件
            _ = _box.ExecAsync("rm", "-f", tempFile);

            _logger.LogInformation(
                "K8s execute_code: lang={Language}, exit={ExitCode}",
                language, result.ExitCode);

            return JsonSerializer.Serialize(new
            {
                language,
                exitCode = result.ExitCode,
                stdout = Truncate(result.Stdout),
                stderr = Truncate(result.Stderr),
                isolated = true,
                runtime = "kubernetes-pod"
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"容器代码执行失败: {ex.Message}" });
        }
    }

    private static string Truncate(string s, int max = 50_000) =>
        s.Length <= max ? s : s[..max] + $"\n\n... [截断，共 {s.Length} 字符]";
}

/// <summary>在容器中安装软件包</summary>
internal sealed class SandboxInstallPackageAIFunction : AIFunction
{
    private readonly ISandboxBox _box;
    private readonly ILogger _logger;

    private static readonly JsonElement _schema = JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "package": {
                "type": "string",
                "description": "包名称（如 numpy, express）"
            },
            "manager": {
                "type": "string",
                "enum": ["pip", "npm", "apt", "apk"],
                "description": "包管理器"
            }
        },
        "required": ["package", "manager"]
    }
    """).RootElement.Clone();

    public SandboxInstallPackageAIFunction(ISandboxBox box, ILogger logger)
    { _box = box; _logger = logger; }

    public override string Name => "install_package";
    public override string Description =>
        "在容器中安装软件包。支持 pip (Python)、npm (Node.js)、apt/apk (Linux)。" +
        "安装的包仅在当前会话的容器中可用。";
    public override JsonElement JsonSchema => _schema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var package = ArgHelper.GetString(arguments.TryGetValue("package", out var pkgObj) ? pkgObj : null);
        var manager = ArgHelper.GetString(arguments.TryGetValue("manager", out var mgrObj) ? mgrObj : null)?.ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(package))
            return JsonSerializer.Serialize(new { error = "参数 'package' 不能为空" });
        if (string.IsNullOrWhiteSpace(manager))
            return JsonSerializer.Serialize(new { error = "参数 'manager' 不能为空" });

        var cmd = manager switch
        {
            "pip" => $"pip install {package}",
            "npm" => $"npm install -g {package}",
            "apt" => $"apt-get update -qq && apt-get install -y -qq {package}",
            "apk" => $"apk add --no-cache {package}",
            _ => null
        };

        if (cmd is null)
            return JsonSerializer.Serialize(new { error = $"不支持的包管理器: {manager}" });

        try
        {
            var result = await _box.ExecAsync("sh", "-c", cmd);

            _logger.LogInformation(
                "K8s install_package: manager={Manager}, package={Package}, exit={ExitCode}",
                manager, package, result.ExitCode);

            return JsonSerializer.Serialize(new
            {
                manager,
                package = package,
                exitCode = result.ExitCode,
                stdout = Truncate(result.Stdout, 20_000),
                stderr = Truncate(result.Stderr, 20_000)
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"容器安装失败: {ex.Message}" });
        }
    }

    private static string Truncate(string s, int max = 50_000) =>
        s.Length <= max ? s : s[..max] + $"\n\n... [截断，共 {s.Length} 字符]";
}
