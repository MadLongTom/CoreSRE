namespace CoreSRE.Domain.Enums;

/// <summary>
/// 沙箱模式
/// </summary>
public enum SandboxMode
{
    /// <summary>不使用沙箱</summary>
    None,

    /// <summary>临时沙箱 — 每对话创建/销毁（向后兼容）</summary>
    Ephemeral,

    /// <summary>持久化沙箱 — 跨对话复用</summary>
    Persistent
}
