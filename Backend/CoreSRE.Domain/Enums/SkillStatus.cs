namespace CoreSRE.Domain.Enums;

/// <summary>
/// Skill 状态（支持 SOP 质量保证生命周期）
/// </summary>
public enum SkillStatus
{
    /// <summary>可被 Agent 引用</summary>
    Active,

    /// <summary>已归档，不可使用</summary>
    Inactive,

    /// <summary>草稿 — 自动生成后待审核</summary>
    Draft,

    /// <summary>已审核 — 等待发布</summary>
    Reviewed,

    /// <summary>已驳回</summary>
    Rejected,

    /// <summary>已归档（手动下线）</summary>
    Archived,

    /// <summary>已被新版本取代</summary>
    Superseded,

    /// <summary>校验不通过</summary>
    Invalid,

    /// <summary>效能下降（由反馈闭环自动标记）</summary>
    Degraded
}
