namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 工具的语义注解。嵌套在 ToolSchemaVO 或 McpToolItem 中。
/// </summary>
public sealed record ToolAnnotationsVO
{
    /// <summary>是否只读（不修改外部状态）</summary>
    public bool ReadOnly { get; init; }

    /// <summary>是否具有破坏性（需人工审批，SPEC-051）</summary>
    public bool Destructive { get; init; }

    /// <summary>是否幂等</summary>
    public bool Idempotent { get; init; }

    /// <summary>是否可能与外部不可控系统交互</summary>
    public bool OpenWorldHint { get; init; }
}
