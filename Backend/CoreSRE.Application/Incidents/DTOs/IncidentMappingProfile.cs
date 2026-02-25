using AutoMapper;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.ValueObjects;

namespace CoreSRE.Application.Incidents.DTOs;

public class IncidentMappingProfile : Profile
{
    public IncidentMappingProfile()
    {
        CreateMap<Incident, IncidentSummaryDto>()
            .ForMember(d => d.Status, opt => opt.MapFrom(s => s.Status.ToString()))
            .ForMember(d => d.Severity, opt => opt.MapFrom(s => s.Severity.ToString()))
            .ForMember(d => d.Route, opt => opt.MapFrom(s => s.Route.ToString()));

        CreateMap<Incident, IncidentDetailDto>()
            .ForMember(d => d.Status, opt => opt.MapFrom(s => s.Status.ToString()))
            .ForMember(d => d.Severity, opt => opt.MapFrom(s => s.Severity.ToString()))
            .ForMember(d => d.Route, opt => opt.MapFrom(s => s.Route.ToString()))
            .ForMember(d => d.GeneratedSopId,
                opt => opt.MapFrom(s => s.GeneratedSopId.HasValue ? s.GeneratedSopId.Value.ToString() : null));

        CreateMap<IncidentTimelineVO, IncidentTimelineItemDto>()
            .ForMember(d => d.EventType, opt => opt.MapFrom(s => s.EventType.ToString()))
            .ForMember(d => d.ActorAgentId,
                opt => opt.MapFrom(s => s.ActorAgentId.HasValue ? s.ActorAgentId.Value.ToString() : null));
    }
}
