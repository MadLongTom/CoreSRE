namespace CoreSRE.Application.Alerts.Interfaces;

/// <summary>
/// 通知分发器接口。根据 AlertRule 配置的 NotificationChannels 列表，
/// 将 Incident 通知路由到对应渠道。
/// </summary>
public interface INotificationDispatcher
{
    /// <summary>
    /// 向指定的通知渠道列表发送 Incident 通知。
    /// </summary>
    /// <param name="channels">渠道配置列表（格式: "type:config"，如 "slack:https://hooks.slack.com/..."）</param>
    /// <param name="notification">通知内容</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task DispatchAsync(
        IEnumerable<string> channels,
        IncidentNotification notification,
        CancellationToken cancellationToken = default);
}
