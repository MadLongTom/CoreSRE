using AutoMapper;
using CoreSRE.Domain.Entities;

namespace CoreSRE.Application.DataSources.DTOs;

/// <summary>
/// DataSourceRegistration 完整详情 DTO
/// </summary>
public class DataSourceRegistrationDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DataSourceConnectionDto ConnectionConfig { get; set; } = new();
    public QueryConfigDto? DefaultQueryConfig { get; set; }
    public DataSourceHealthDto? HealthCheck { get; set; }
    public DataSourceMetadataDto? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>连接配置 DTO（凭据以 hasCredential 标识替代实际值）</summary>
public class DataSourceConnectionDto
{
    public string BaseUrl { get; set; } = string.Empty;
    public string AuthType { get; set; } = "None";
    public bool HasCredential { get; set; }
    public string? MaskedCredential { get; set; }
    public string? AuthHeaderName { get; set; }
    public bool TlsSkipVerify { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public Dictionary<string, string>? CustomHeaders { get; set; }
    public string? Namespace { get; set; }
    public string? Organization { get; set; }
}

/// <summary>默认查询配置 DTO</summary>
public class QueryConfigDto
{
    public Dictionary<string, string>? DefaultLabels { get; set; }
    public string? DefaultNamespace { get; set; }
    public int? MaxResults { get; set; }
    public string? DefaultStep { get; set; }
    public string? DefaultIndex { get; set; }
}

/// <summary>健康检查状态 DTO</summary>
public class DataSourceHealthDto
{
    public DateTime? LastCheckAt { get; set; }
    public bool IsHealthy { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Version { get; set; }
    public int? ResponseTimeMs { get; set; }
}

/// <summary>元数据缓存 DTO</summary>
public class DataSourceMetadataDto
{
    public DateTime? DiscoveredAt { get; set; }
    public List<string>? Labels { get; set; }
    public List<string>? Indices { get; set; }
    public List<string>? Services { get; set; }
    public List<string>? Namespaces { get; set; }
    public List<string>? AvailableFunctions { get; set; }
}

/// <summary>
/// AutoMapper Profile: DataSourceRegistration → DataSourceRegistrationDto
/// </summary>
public class DataSourceMappingProfile : Profile
{
    public DataSourceMappingProfile()
    {
        CreateMap<DataSourceRegistration, DataSourceRegistrationDto>()
            .ForMember(d => d.Category, opt => opt.MapFrom(s => s.Category.ToString()))
            .ForMember(d => d.Product, opt => opt.MapFrom(s => s.Product.ToString()))
            .ForMember(d => d.Status, opt => opt.MapFrom(s => s.Status.ToString()));

        CreateMap<Domain.ValueObjects.DataSourceConnectionVO, DataSourceConnectionDto>()
            .ForMember(d => d.HasCredential, opt => opt.MapFrom(s => !string.IsNullOrEmpty(s.EncryptedCredential)))
            .ForMember(d => d.MaskedCredential, opt => opt.MapFrom(s =>
                string.IsNullOrEmpty(s.EncryptedCredential) ? null : "****"));

        CreateMap<Domain.ValueObjects.QueryConfigVO, QueryConfigDto>();
        CreateMap<Domain.ValueObjects.DataSourceHealthVO, DataSourceHealthDto>();
        CreateMap<Domain.ValueObjects.DataSourceMetadataVO, DataSourceMetadataDto>();
    }
}
