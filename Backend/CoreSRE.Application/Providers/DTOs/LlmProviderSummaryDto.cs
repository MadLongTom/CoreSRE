namespace CoreSRE.Application.Providers.DTOs;

/// <summary>
/// LLM Provider 列表摘要 DTO
/// </summary>
public class LlmProviderSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public int ModelCount { get; set; }
    public DateTime CreatedAt { get; set; }
}
