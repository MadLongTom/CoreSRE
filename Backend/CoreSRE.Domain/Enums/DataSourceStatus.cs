namespace CoreSRE.Domain.Enums;

/// <summary>
/// 数据源连接状态。
/// </summary>
public enum DataSourceStatus
{
    /// <summary>已注册，尚未测试连接</summary>
    Registered,

    /// <summary>连接正常</summary>
    Connected,

    /// <summary>连接断开（曾连接成功，后续失败）</summary>
    Disconnected,

    /// <summary>连接错误（连续失败超过阈值）</summary>
    Error
}
