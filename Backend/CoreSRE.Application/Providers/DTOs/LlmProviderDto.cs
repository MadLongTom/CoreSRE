namespace CoreSRE.Application.Providers.DTOs;

/// <summary>
/// LLM Provider 完整详情 DTO（API Key 已掩码）
/// </summary>
public class LlmProviderDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string MaskedApiKey { get; set; } = string.Empty;
    public List<string> DiscoveredModels { get; set; } = [];
    public DateTime? ModelsRefreshedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
