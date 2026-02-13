using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Skills.DTOs;
using MediatR;

namespace CoreSRE.Application.Skills.Queries.GetSkillById;

/// <summary>获取技能详情</summary>
public record GetSkillByIdQuery(Guid Id) : IRequest<Result<SkillRegistrationDto>>;
