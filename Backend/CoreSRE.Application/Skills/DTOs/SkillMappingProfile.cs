using AutoMapper;
using CoreSRE.Domain.Entities;

namespace CoreSRE.Application.Skills.DTOs;

/// <summary>
/// AutoMapper 映射配置：SkillRegistration ↔ SkillRegistrationDto
/// </summary>
public class SkillMappingProfile : Profile
{
    public SkillMappingProfile()
    {
        CreateMap<SkillRegistration, SkillRegistrationDto>()
            .ForMember(d => d.Scope, opt => opt.MapFrom(s => s.Scope.ToString()))
            .ForMember(d => d.Status, opt => opt.MapFrom(s => s.Status.ToString()));
    }
}
