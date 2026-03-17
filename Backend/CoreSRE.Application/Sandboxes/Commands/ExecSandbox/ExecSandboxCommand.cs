using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using MediatR;

namespace CoreSRE.Application.Sandboxes.Commands.ExecSandbox;

/// <summary>在沙箱内执行命令</summary>
public record ExecSandboxCommand : IRequest<Result<SandboxExecResult>>
{
    public Guid Id { get; init; }
    public string Command { get; init; } = string.Empty;
    public string[] Args { get; init; } = [];
}
