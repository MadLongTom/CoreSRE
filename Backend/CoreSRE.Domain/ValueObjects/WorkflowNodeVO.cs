using CoreSRE.Domain.Enums;

namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 工作流图中的节点。描述一个执行单元。
/// </summary>
public sealed record WorkflowNodeVO
{
    /// <summary>节点 ID，图内唯一标识（用户指定，非数据库 GUID）</summary>
    public string NodeId { get; init; } = string.Empty;

    /// <summary>节点类型（Agent/Tool/Condition/FanOut/FanIn）</summary>
    public WorkflowNodeType NodeType { get; init; }

    /// <summary>引用 ID（Agent 或 Tool 的注册 ID，仅 Agent/Tool 类型必填）</summary>
    public Guid? ReferenceId { get; init; }

    /// <summary>显示名称</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>配置参数（JSON 格式字符串，可选）</summary>
    public string? Config { get; init; }
}
