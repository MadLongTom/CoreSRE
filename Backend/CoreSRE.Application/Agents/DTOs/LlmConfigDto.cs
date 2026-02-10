namespace CoreSRE.Application.Agents.DTOs;

/// <summary>
/// ChatClient Agent 的 LLM 配置 DTO
/// </summary>
public class LlmConfigDto
{
    public string ModelId { get; set; } = string.Empty;
    public string? Instructions { get; set; }
    public List<Guid> ToolRefs { get; set; } = [];
}
