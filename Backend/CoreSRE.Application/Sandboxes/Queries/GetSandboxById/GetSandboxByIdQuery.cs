using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Sandboxes.DTOs;
using MediatR;

namespace CoreSRE.Application.Sandboxes.Queries.GetSandboxById;

/// <summary>获取沙箱详情</summary>
public record GetSandboxByIdQuery(Guid Id) : IRequest<Result<SandboxInstanceDto>>;
