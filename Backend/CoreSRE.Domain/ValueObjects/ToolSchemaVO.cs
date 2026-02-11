namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 工具的输入/输出 Schema 描述。存储为 PostgreSQL JSONB 列。
/// InputSchema / OutputSchema 以序列化 JSON 字符串形式保存，
/// 避免 EF Core ToJson() 无法处理 JsonElement 的问题。
/// </summary>
public sealed record ToolSchemaVO
{
    /// <summary>输入参数 JSON Schema (serialized JSON string)</summary>
    public string? InputSchema { get; init; }

    /// <summary>输出结果 JSON Schema (serialized JSON string)</summary>
    public string? OutputSchema { get; init; }

    /// <summary>工具注解</summary>
    public ToolAnnotationsVO? Annotations { get; init; }
}
