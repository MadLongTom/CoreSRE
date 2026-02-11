using CoreSRE.Domain.Enums;

namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 工作流图中的边。描述节点间的执行流向。
/// </summary>
public sealed record WorkflowEdgeVO
{
    /// <summary>边 ID，图内唯一标识</summary>
    public string EdgeId { get; init; } = string.Empty;

    /// <summary>源节点 ID</summary>
    public string SourceNodeId { get; init; } = string.Empty;

    /// <summary>目标节点 ID</summary>
    public string TargetNodeId { get; init; } = string.Empty;

    /// <summary>边类型（Normal/Conditional）</summary>
    public WorkflowEdgeType EdgeType { get; init; }

    /// <summary>条件表达式（仅 Conditional 类型必填）</summary>
    public string? Condition { get; init; }
}
