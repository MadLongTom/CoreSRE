using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Sandboxes.DTOs;
using MediatR;

namespace CoreSRE.Application.Sandboxes.Commands.UpdateSandbox;

/// <summary>更新沙箱配置（仅 Stopped 状态）</summary>
public record UpdateSandboxCommand : IRequest<Result<SandboxInstanceDto>>
{
    public Guid Id { get; init; }
    public string? Name { get; init; }
    public string? Image { get; init; }
    public int? CpuCores { get; init; }
    public int? MemoryMib { get; init; }
    public int? AutoStopMinutes { get; init; }
    public bool? PersistWorkspace { get; init; }
    public Guid? AgentId { get; init; }
}
