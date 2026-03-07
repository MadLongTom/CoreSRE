using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Skills.Commands.RejectSop;

/// <summary>驳回 SOP</summary>
public record RejectSopCommand(Guid SkillId, string ReviewedBy, string Reason)
    : IRequest<Result<bool>>;
