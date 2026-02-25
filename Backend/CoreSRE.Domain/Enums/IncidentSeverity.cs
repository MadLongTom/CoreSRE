namespace CoreSRE.Domain.Enums;

/// <summary>
/// 事故严重等级
/// </summary>
public enum IncidentSeverity
{
    /// <summary>最高优先级 — 核心业务完全中断</summary>
    P1,

    /// <summary>高优先级 — 核心业务严重降级</summary>
    P2,

    /// <summary>中等优先级 — 非核心功能受影响</summary>
    P3,

    /// <summary>低优先级 — 次要问题</summary>
    P4
}
