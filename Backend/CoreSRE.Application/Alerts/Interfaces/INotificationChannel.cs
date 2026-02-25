namespace CoreSRE.Application.Alerts.Interfaces;

/// <summary>
/// 通知渠道抽象接口。
/// 每种渠道（Slack / Teams / Email）实现此接口。
/// </summary>
public interface INotificationChannel
{
    /// <summary>渠道类型标识（slack / teams / email 等）</summary>
    string ChannelType { get; }

    /// <summary>
    /// 发送 Incident 通知。
    /// </summary>
    /// <param name="channelConfig">渠道配置（如 Webhook URL）— 存储在 AlertRule.NotificationChannels 中</param>
    /// <param name="notification">通知内容</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SendAsync(
        string channelConfig,
        IncidentNotification notification,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Incident 通知内容。
/// </summary>
public record IncidentNotification(
    Guid IncidentId,
    string Title,
    string Severity,
    string Status,
    string Route,
    string AlertName,
    Dictionary<string, string> AlertLabels,
    string? Summary = null,
    string? Resolution = null,
    DateTime? Timestamp = null);
