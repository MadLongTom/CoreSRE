using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Skills.Commands.PublishSop;

/// <summary>发布 SOP（从 Reviewed 状态变为 Active）</summary>
public record PublishSopCommand(Guid SkillId, Guid? AlertRuleId = null)
    : IRequest<Result<bool>>;
