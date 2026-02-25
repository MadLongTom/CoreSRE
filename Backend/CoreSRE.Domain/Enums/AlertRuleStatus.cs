namespace CoreSRE.Domain.Enums;

/// <summary>
/// 告警路由规则状态
/// </summary>
public enum AlertRuleStatus
{
    /// <summary>激活 — 参与告警匹配</summary>
    Active,

    /// <summary>停用 — 不参与告警匹配</summary>
    Inactive
}
