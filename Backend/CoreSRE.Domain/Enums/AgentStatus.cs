namespace CoreSRE.Domain.Enums;

/// <summary>
/// Agent 生命周期状态枚举
/// </summary>
public enum AgentStatus
{
    /// <summary>已注册（初始状态）</summary>
    Registered,

    /// <summary>活跃（健康检查通过）</summary>
    Active,

    /// <summary>不活跃（连续健康检查失败）</summary>
    Inactive,

    /// <summary>错误状态</summary>
    Error
}
