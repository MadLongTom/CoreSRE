using AutoMapper;
using CoreSRE.Domain.Entities;

namespace CoreSRE.Application.Providers.DTOs;

/// <summary>
/// AutoMapper 映射配置：LlmProvider 实体 → DTOs
/// </summary>
public class ProviderMappingProfile : Profile
{
    public ProviderMappingProfile()
    {
        // Entity → Detail DTO (MaskedApiKey handled manually in handlers)
        CreateMap<LlmProvider, LlmProviderDto>()
            .ForMember(d => d.MaskedApiKey, opt => opt.MapFrom(s => s.MaskApiKey()));

        // Entity → Summary DTO
        CreateMap<LlmProvider, LlmProviderSummaryDto>()
            .ForMember(d => d.ModelCount, opt => opt.MapFrom(s => s.DiscoveredModels.Count));
    }
}
