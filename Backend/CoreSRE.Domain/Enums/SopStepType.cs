namespace CoreSRE.Domain.Enums;

/// <summary>
/// SOP 步骤类型
/// </summary>
public enum SopStepType
{
    /// <summary>结构化步骤（声明了工具和参数）</summary>
    Structured,

    /// <summary>自由形式步骤（由 Agent 自由执行）</summary>
    Freeform,
}
