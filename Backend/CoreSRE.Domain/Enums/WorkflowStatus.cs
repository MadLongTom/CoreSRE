namespace CoreSRE.Domain.Enums;

/// <summary>工作流状态</summary>
public enum WorkflowStatus
{
    /// <summary>草稿状态，可编辑删除</summary>
    Draft,
    /// <summary>已发布状态，不可编辑删除</summary>
    Published
}
