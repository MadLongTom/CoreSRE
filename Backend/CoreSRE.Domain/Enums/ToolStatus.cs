namespace CoreSRE.Domain.Enums;

/// <summary>
/// 工具状态枚举
/// </summary>
public enum ToolStatus
{
    /// <summary>可用，可被调用</summary>
    Active,

    /// <summary>不可用（MCP 握手失败、手动禁用等）</summary>
    Inactive,

    /// <summary>熔断中（SPEC-014 处理，本 Spec 仅定义枚举值）</summary>
    CircuitOpen
}
