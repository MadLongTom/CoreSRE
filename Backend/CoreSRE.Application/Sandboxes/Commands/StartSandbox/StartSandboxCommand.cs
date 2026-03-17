using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Sandboxes.DTOs;
using MediatR;

namespace CoreSRE.Application.Sandboxes.Commands.StartSandbox;

/// <summary>启动已停止的沙箱</summary>
public record StartSandboxCommand(Guid Id) : IRequest<Result<SandboxInstanceDto>>;
