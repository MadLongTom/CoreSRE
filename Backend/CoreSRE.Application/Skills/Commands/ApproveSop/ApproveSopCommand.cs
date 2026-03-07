using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Skills.Commands.ApproveSop;

/// <summary>审核通过 SOP</summary>
public record ApproveSopCommand(Guid SkillId, string ReviewedBy, string? Comment = null)
    : IRequest<Result<bool>>;
