using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Sandboxes.DTOs;
using CoreSRE.Domain.Enums;
using MediatR;

namespace CoreSRE.Application.Sandboxes.Queries.GetSandboxes;

/// <summary>查询沙箱列表</summary>
public record GetSandboxesQuery(
    SandboxStatus? Status = null,
    Guid? AgentId = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20) : IRequest<Result<PagedResult<SandboxInstanceDto>>>;
