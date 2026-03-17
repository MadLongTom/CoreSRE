using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Sandboxes.Commands.DeleteSandbox;

/// <summary>终止并删除沙箱</summary>
public record DeleteSandboxCommand(Guid Id) : IRequest<Result<bool>>;
