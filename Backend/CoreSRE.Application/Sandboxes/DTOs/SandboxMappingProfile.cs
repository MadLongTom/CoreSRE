using AutoMapper;
using CoreSRE.Domain.Entities;

namespace CoreSRE.Application.Sandboxes.DTOs;

/// <summary>
/// AutoMapper 映射配置：SandboxInstance ↔ SandboxInstanceDto
/// </summary>
public class SandboxMappingProfile : Profile
{
    public SandboxMappingProfile()
    {
        CreateMap<SandboxInstance, SandboxInstanceDto>()
            .ForMember(d => d.Status, opt => opt.MapFrom(s => s.Status.ToString()));
    }
}
