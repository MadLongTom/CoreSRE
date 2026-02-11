namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// ChatClient Agent 的 LLM 配置。存储为 PostgreSQL JSONB 列。
/// 字段与 Microsoft.Extensions.AI.ChatOptions 的可配置项一一对应。
/// </summary>
public sealed record LlmConfigVO
{
    /// <summary>关联的 LLM Provider ID（nullable，向后兼容）</summary>
    public Guid? ProviderId { get; init; }

    /// <summary>LLM 模型标识符</summary>
    public string ModelId { get; init; } = string.Empty;

    /// <summary>系统指令</summary>
    public string? Instructions { get; init; }

    /// <summary>工具引用列表（M2 模块 ID，可为空）</summary>
    public List<Guid> ToolRefs { get; init; } = [];

    // ── ChatOptions 扩展配置 ──────────────────────────────────────────────

    /// <summary>生成温度（0‒2），越低越确定</summary>
    public float? Temperature { get; init; }

    /// <summary>最大输出 token 数</summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>核采样概率（0‒1）</summary>
    public float? TopP { get; init; }

    /// <summary>Top-K 采样，限制候选 token 数</summary>
    public int? TopK { get; init; }

    /// <summary>重复频率惩罚</summary>
    public float? FrequencyPenalty { get; init; }

    /// <summary>存在性惩罚</summary>
    public float? PresencePenalty { get; init; }

    /// <summary>随机种子，用于可复现生成</summary>
    public long? Seed { get; init; }

    /// <summary>停止序列列表</summary>
    public List<string>? StopSequences { get; init; }

    /// <summary>响应格式：Text / Json</summary>
    public string? ResponseFormat { get; init; }

    /// <summary>当 ResponseFormat 为 Json 时的 JSON Schema（可选）</summary>
    public string? ResponseFormatSchema { get; init; }

    /// <summary>工具模式：Auto / Required / None</summary>
    public string? ToolMode { get; init; }

    /// <summary>是否允许单次响应包含多个工具调用</summary>
    public bool? AllowMultipleToolCalls { get; init; }
}
