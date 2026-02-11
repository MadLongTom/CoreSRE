namespace CoreSRE.Domain.Enums;

/// <summary>节点执行状态</summary>
public enum NodeExecutionStatus
{
    /// <summary>等待执行（初始状态）</summary>
    Pending,

    /// <summary>执行中</summary>
    Running,

    /// <summary>成功完成</summary>
    Completed,

    /// <summary>执行失败</summary>
    Failed,

    /// <summary>被条件分支跳过</summary>
    Skipped
}
