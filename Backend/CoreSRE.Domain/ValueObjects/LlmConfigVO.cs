namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// ChatClient Agent 的 LLM 配置。存储为 PostgreSQL JSONB 列。
/// </summary>
public sealed record LlmConfigVO
{
    /// <summary>LLM 模型标识符</summary>
    public string ModelId { get; init; } = string.Empty;

    /// <summary>系统指令</summary>
    public string? Instructions { get; init; }

    /// <summary>工具引用列表（M2 模块 ID，可为空）</summary>
    public List<Guid> ToolRefs { get; init; } = [];
}
