using AutoMapper;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;

namespace CoreSRE.Application.AlertRules.DTOs;

public class AlertRuleMappingProfile : Profile
{
    public AlertRuleMappingProfile()
    {
        CreateMap<AlertRule, AlertRuleDto>()
            .ForMember(d => d.Status, o => o.MapFrom(s => s.Status.ToString()))
            .ForMember(d => d.Severity, o => o.MapFrom(s => s.Severity.ToString()));

        CreateMap<AlertMatcherVO, AlertMatcherDto>()
            .ForMember(d => d.Operator, o => o.MapFrom(s => s.Operator.ToString()));

        CreateMap<AlertMatcherDto, AlertMatcherVO>()
            .ForMember(d => d.Operator, o => o.MapFrom(s => Enum.Parse<MatchOp>(s.Operator, true)));
    }
}
