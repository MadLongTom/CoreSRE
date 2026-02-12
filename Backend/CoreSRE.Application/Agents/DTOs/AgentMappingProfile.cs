using AutoMapper;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.ValueObjects;

namespace CoreSRE.Application.Agents.DTOs;

/// <summary>
/// AutoMapper 映射配置：AgentRegistration 实体 ↔ DTOs
/// </summary>
public class AgentMappingProfile : Profile
{
    public AgentMappingProfile()
    {
        // Entity → Detail DTO
        CreateMap<AgentRegistration, AgentRegistrationDto>()
            .ForMember(d => d.AgentType, opt => opt.MapFrom(s => s.AgentType.ToString()))
            .ForMember(d => d.Status, opt => opt.MapFrom(s => s.Status.ToString()));

        // Entity → Summary DTO
        CreateMap<AgentRegistration, AgentSummaryDto>()
            .ForMember(d => d.AgentType, opt => opt.MapFrom(s => s.AgentType.ToString()))
            .ForMember(d => d.Status, opt => opt.MapFrom(s => s.Status.ToString()));

        // Value Objects → DTOs
        CreateMap<AgentCardVO, AgentCardDto>();
        CreateMap<AgentSkillVO, AgentSkillDto>();
        CreateMap<AgentInterfaceVO, AgentInterfaceDto>();
        CreateMap<SecuritySchemeVO, SecuritySchemeDto>();
        CreateMap<LlmConfigVO, LlmConfigDto>();

        // DTOs → Value Objects (for command handling)
        CreateMap<AgentCardDto, AgentCardVO>();
        CreateMap<AgentSkillDto, AgentSkillVO>();
        CreateMap<AgentInterfaceDto, AgentInterfaceVO>();
        CreateMap<SecuritySchemeDto, SecuritySchemeVO>();
        CreateMap<LlmConfigDto, LlmConfigVO>()
            .ForSourceMember(s => s.ProviderName, opt => opt.DoNotValidate())
            .ForSourceMember(s => s.EmbeddingProviderName, opt => opt.DoNotValidate());
    }
}
