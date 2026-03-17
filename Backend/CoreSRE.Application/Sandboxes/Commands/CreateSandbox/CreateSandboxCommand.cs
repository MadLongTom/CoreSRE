using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Sandboxes.DTOs;
using MediatR;

namespace CoreSRE.Application.Sandboxes.Commands.CreateSandbox;

/// <summary>
/// 创建沙箱命令
/// </summary>
public record CreateSandboxCommand : IRequest<Result<SandboxInstanceDto>>
{
    public string Name { get; init; } = string.Empty;
    public string SandboxType { get; init; } = "SimpleBox";
    public string? Image { get; init; }
    public int CpuCores { get; init; } = 1;
    public int MemoryMib { get; init; } = 512;
    public string K8sNamespace { get; init; } = "coresre-sandbox";
    public int AutoStopMinutes { get; init; } = 30;
    public bool PersistWorkspace { get; init; } = true;
    public Guid? AgentId { get; init; }
}
