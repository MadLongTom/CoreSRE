namespace CoreSRE.Domain.Enums;

/// <summary>工作流执行状态</summary>
public enum ExecutionStatus
{
    /// <summary>等待启动（初始状态）</summary>
    Pending,

    /// <summary>执行中</summary>
    Running,

    /// <summary>成功完成</summary>
    Completed,

    /// <summary>执行失败</summary>
    Failed,

    /// <summary>已取消（预留给 SPEC-024）</summary>
    Canceled
}
