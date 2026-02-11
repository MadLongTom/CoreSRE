using System.Text.Json;
using AutoMapper;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.ValueObjects;

namespace CoreSRE.Application.Tools.DTOs;

/// <summary>
/// AutoMapper 映射配置：ToolRegistration / McpToolItem 实体 ↔ DTOs
/// </summary>
public class ToolMappingProfile : Profile
{
    public ToolMappingProfile()
    {
        // ToolRegistration → ToolRegistrationDto
        CreateMap<ToolRegistration, ToolRegistrationDto>()
            .ForMember(d => d.ToolType, opt => opt.MapFrom(s => s.ToolType.ToString()))
            .ForMember(d => d.Status, opt => opt.MapFrom(s => s.Status.ToString()))
            .ForMember(d => d.McpToolCount, opt => opt.MapFrom(s => s.McpToolItems.Count));

        // ConnectionConfigVO → ConnectionConfigDto
        CreateMap<ConnectionConfigVO, ConnectionConfigDto>()
            .ForMember(d => d.TransportType, opt => opt.MapFrom(s => s.TransportType.ToString()));

        // AuthConfigVO → AuthConfigDto (credential masking handled by custom resolver)
        CreateMap<AuthConfigVO, AuthConfigDto>()
            .ForMember(d => d.AuthType, opt => opt.MapFrom(s => s.AuthType.ToString()))
            .ForMember(d => d.HasCredential, opt => opt.MapFrom(s => !string.IsNullOrEmpty(s.EncryptedCredential)))
            .ForMember(d => d.MaskedCredential, opt => opt.MapFrom<MaskedCredentialResolver>())
            .ForMember(d => d.HasClientSecret, opt => opt.MapFrom(s => !string.IsNullOrEmpty(s.EncryptedClientSecret)));

        // ToolSchemaVO → ToolSchemaDto (string? → JsonElement? deserialization)
        CreateMap<ToolSchemaVO, ToolSchemaDto>()
            .ForMember(d => d.InputSchema, opt => opt.MapFrom(s =>
                s.InputSchema != null ? JsonSerializer.Deserialize<JsonElement>(s.InputSchema) : (JsonElement?)null))
            .ForMember(d => d.OutputSchema, opt => opt.MapFrom(s =>
                s.OutputSchema != null ? JsonSerializer.Deserialize<JsonElement>(s.OutputSchema) : (JsonElement?)null));

        // ToolAnnotationsVO → ToolAnnotationsDto
        CreateMap<ToolAnnotationsVO, ToolAnnotationsDto>();

        // McpToolItem → McpToolItemDto
        CreateMap<McpToolItem, McpToolItemDto>();
    }
}

/// <summary>
/// AutoMapper 自定义解析器：将加密凭据遮盖处理
/// </summary>
public class MaskedCredentialResolver : IValueResolver<AuthConfigVO, AuthConfigDto, string?>
{
    private readonly ICredentialEncryptionService _encryptionService;

    public MaskedCredentialResolver(ICredentialEncryptionService encryptionService)
    {
        _encryptionService = encryptionService;
    }

    public string? Resolve(AuthConfigVO source, AuthConfigDto destination, string? destMember, ResolutionContext context)
    {
        if (string.IsNullOrEmpty(source.EncryptedCredential))
            return null;

        return _encryptionService.Mask(source.EncryptedCredential);
    }
}
