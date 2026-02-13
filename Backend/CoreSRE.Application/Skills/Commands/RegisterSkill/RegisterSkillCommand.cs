using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Skills.DTOs;
using MediatR;

namespace CoreSRE.Application.Skills.Commands.RegisterSkill;

/// <summary>
/// 注册技能命令
/// </summary>
public record RegisterSkillCommand : IRequest<Result<SkillRegistrationDto>>
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string Scope { get; init; } = "User";
    public List<Guid> RequiresTools { get; init; } = [];
}
