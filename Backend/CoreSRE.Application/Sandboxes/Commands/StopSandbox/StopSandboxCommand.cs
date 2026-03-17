using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Sandboxes.DTOs;
using MediatR;

namespace CoreSRE.Application.Sandboxes.Commands.StopSandbox;

/// <summary>停止运行中的沙箱</summary>
public record StopSandboxCommand(Guid Id) : IRequest<Result<SandboxInstanceDto>>;
