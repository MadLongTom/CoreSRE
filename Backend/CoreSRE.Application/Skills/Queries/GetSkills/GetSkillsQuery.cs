using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Skills.DTOs;
using CoreSRE.Domain.Enums;
using MediatR;

namespace CoreSRE.Application.Skills.Queries.GetSkills;

/// <summary>查询技能列表（支持分页、Scope/Status/Category 过滤、关键词搜索）</summary>
public record GetSkillsQuery(
    SkillScope? Scope = null,
    SkillStatus? Status = null,
    string? Category = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20) : IRequest<Result<PagedResult<SkillRegistrationDto>>>;
