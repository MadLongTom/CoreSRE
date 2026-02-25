namespace CoreSRE.Domain.Enums;

/// <summary>
/// 事故处置链路
/// </summary>
public enum IncidentRoute
{
    /// <summary>链路 A — 有 SOP，单 Agent 自动执行</summary>
    SopExecution,

    /// <summary>链路 B — 无 SOP，多 Agent 根因分析</summary>
    RootCauseAnalysis
}
