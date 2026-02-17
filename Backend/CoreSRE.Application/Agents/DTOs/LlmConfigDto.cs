namespace CoreSRE.Application.Agents.DTOs;

/// <summary>
/// ChatClient Agent 的 LLM 配置 DTO
/// </summary>
public class LlmConfigDto
{
    public Guid? ProviderId { get; set; }
    public string? ProviderName { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public string? Instructions { get; set; }
    public List<Guid> ToolRefs { get; set; } = [];
    public List<DataSourceRefDto> DataSourceRefs { get; set; } = [];

    // ── ChatOptions 扩展配置 ──
    public float? Temperature { get; set; }
    public int? MaxOutputTokens { get; set; }
    public float? TopP { get; set; }
    public int? TopK { get; set; }
    public float? FrequencyPenalty { get; set; }
    public float? PresencePenalty { get; set; }
    public long? Seed { get; set; }
    public List<string>? StopSequences { get; set; }
    public string? ResponseFormat { get; set; }
    public string? ResponseFormatSchema { get; set; }
    public string? ToolMode { get; set; }
    public bool? AllowMultipleToolCalls { get; set; }

    // ── History & Memory 配置 ──
    public bool? EnableChatHistory { get; set; }
    public int? MaxHistoryMessages { get; set; }
    public bool? EnableSemanticMemory { get; set; }
    public Guid? EmbeddingProviderId { get; set; }
    public string? EmbeddingProviderName { get; set; }
    public string? EmbeddingModelId { get; set; }
    public int? EmbeddingDimensions { get; set; }
    public string? MemorySearchMode { get; set; }
    public int? MemoryMaxResults { get; set; }
    public double? MemoryMinRelevanceScore { get; set; }

    // ── Sandbox 配置（Kubernetes Pod 容器隔离）──
    public bool? EnableSandbox { get; set; }
    public string? SandboxType { get; set; }
    public string? SandboxImage { get; set; }
    public int? SandboxCpus { get; set; }
    public int? SandboxMemoryMib { get; set; }
    public string? SandboxK8sNamespace { get; set; }
    public string? SandboxMode { get; set; }
    public Guid? SandboxInstanceId { get; set; }

    // ── Skills 配置 ──
    public List<Guid> SkillRefs { get; set; } = [];
}
