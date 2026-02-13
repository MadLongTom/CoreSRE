namespace CoreSRE.Domain.Enums;

/// <summary>
/// 沙箱实例状态
/// </summary>
public enum SandboxStatus
{
    /// <summary>正在创建 Pod</summary>
    Creating,

    /// <summary>Pod 正在运行</summary>
    Running,

    /// <summary>已停止（工作区已持久化到 S3）</summary>
    Stopped,

    /// <summary>已终止并删除</summary>
    Terminated
}
