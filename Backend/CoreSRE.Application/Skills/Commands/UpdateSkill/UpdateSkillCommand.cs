using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Skills.DTOs;
using MediatR;

namespace CoreSRE.Application.Skills.Commands.UpdateSkill;

/// <summary>更新技能</summary>
public record UpdateSkillCommand : IRequest<Result<SkillRegistrationDto>>
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public List<Guid> RequiresTools { get; init; } = [];
}
