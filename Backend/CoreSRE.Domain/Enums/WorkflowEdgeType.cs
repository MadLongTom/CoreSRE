namespace CoreSRE.Domain.Enums;

/// <summary>工作流边类型</summary>
public enum WorkflowEdgeType
{
    /// <summary>无条件执行边</summary>
    Normal,
    /// <summary>条件执行边（需条件表达式）</summary>
    Conditional
}
