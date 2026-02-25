namespace CoreSRE.Domain.Enums;

/// <summary>
/// 事故生命周期状态。
/// 合法流转: Open → Investigating → Mitigated → Resolved → Closed
/// </summary>
public enum IncidentStatus
{
    /// <summary>已创建，尚未开始处理</summary>
    Open,

    /// <summary>正在调查/处理中</summary>
    Investigating,

    /// <summary>已缓解，影响已减轻但未根治</summary>
    Mitigated,

    /// <summary>已解决</summary>
    Resolved,

    /// <summary>已关闭（归档）</summary>
    Closed
}
