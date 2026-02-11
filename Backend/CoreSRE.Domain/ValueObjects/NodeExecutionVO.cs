using CoreSRE.Domain.Enums;

namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 单个节点的执行记录，嵌套在 WorkflowExecution 中。
/// </summary>
public sealed record NodeExecutionVO
{
    /// <summary>对应 DAG 图中的节点 ID</summary>
    public string NodeId { get; init; } = string.Empty;

    /// <summary>节点执行状态</summary>
    public NodeExecutionStatus Status { get; init; }

    /// <summary>节点输入数据（JSON 字符串）</summary>
    public string? Input { get; init; }

    /// <summary>节点输出数据（JSON 字符串）</summary>
    public string? Output { get; init; }

    /// <summary>失败时的错误信息</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>节点开始执行时间</summary>
    public DateTime? StartedAt { get; init; }

    /// <summary>节点执行完成时间</summary>
    public DateTime? CompletedAt { get; init; }
}
