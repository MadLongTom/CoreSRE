namespace CoreSRE.Domain.Enums;

/// <summary>
/// 事故时间线事件类型
/// </summary>
public enum TimelineEventType
{
    /// <summary>收到告警</summary>
    AlertReceived,

    /// <summary>告警重复（冷却窗口内已有 Incident）</summary>
    AlertRepeated,

    /// <summary>SOP 执行开始</summary>
    SopStarted,

    /// <summary>SOP 执行完成</summary>
    SopCompleted,

    /// <summary>Agent 发送了消息</summary>
    AgentMessage,

    /// <summary>工具调用</summary>
    ToolCall,

    /// <summary>根因分析开始</summary>
    RcaStarted,

    /// <summary>根因分析完成</summary>
    RcaCompleted,

    /// <summary>发现根因</summary>
    RootCauseFound,

    /// <summary>SOP 自动生成完成</summary>
    SopGenerated,

    /// <summary>已上报（人工介入）</summary>
    Escalated,

    /// <summary>超时</summary>
    Timeout,

    /// <summary>已解决</summary>
    Resolved,

    /// <summary>状态变更</summary>
    StatusChanged,

    /// <summary>人工备注</summary>
    ManualNote,

    /// <summary>人工介入消息（操作员向 Agent 发送指令）</summary>
    HumanIntervention,

    /// <summary>Agent 请求人工介入（Agent 主动暂停等待人类输入）</summary>
    InterventionRequested,

    /// <summary>工具调用需要人工审批</summary>
    ToolApprovalRequested,

    /// <summary>工具调用审批结果（批准或拒绝）</summary>
    ToolApprovalResponded,

    /// <summary>SOP 执行失败后降级到 RCA（Spec 025）</summary>
    SopFallbackToRca,

    /// <summary>SOP 因连续失败被自动解绑（Spec 025）</summary>
    SopAutoDisabled
}
