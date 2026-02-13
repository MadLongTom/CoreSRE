using CoreSRE.Domain.ValueObjects;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// 执行栈条目 — 表示一个待执行的节点及其输入数据。
/// </summary>
internal sealed record NodeExecutionTask
{
    /// <summary>待执行的节点</summary>
    public WorkflowNodeVO Node { get; init; } = null!;

    /// <summary>节点的结构化输入数据</summary>
    public NodeInputData InputData { get; init; } = NodeInputData.Empty;

    /// <summary>执行次数索引（用于循环场景，当前默认 0）</summary>
    public int RunIndex { get; init; } = 0;

    /// <summary>触发该执行的数据源信息（可选）</summary>
    public ItemSourceVO? TriggerSource { get; init; }
}
