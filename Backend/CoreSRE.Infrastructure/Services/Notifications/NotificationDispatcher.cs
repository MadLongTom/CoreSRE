using CoreSRE.Application.Alerts.Interfaces;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services.Notifications;

/// <summary>
/// 通知分发器实现。解析 "type:config" 格式的渠道配置，
/// 匹配对应 INotificationChannel 实现并发送。
/// </summary>
public class NotificationDispatcher(
    IEnumerable<INotificationChannel> channels,
    ILogger<NotificationDispatcher> logger) : INotificationDispatcher
{
    private readonly Dictionary<string, INotificationChannel> _channelMap =
        channels.ToDictionary(c => c.ChannelType, c => c, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task DispatchAsync(
        IEnumerable<string> channelConfigs,
        IncidentNotification notification,
        CancellationToken cancellationToken = default)
    {
        foreach (var channelConfig in channelConfigs)
        {
            // 格式: "type:config" — 例如 "slack:https://hooks.slack.com/..."
            var separatorIndex = channelConfig.IndexOf(':');
            if (separatorIndex <= 0)
            {
                logger.LogWarning("Invalid channel config format: {Config}. Expected 'type:config'.", channelConfig);
                continue;
            }

            var channelType = channelConfig[..separatorIndex].Trim();
            var config = channelConfig[(separatorIndex + 1)..].Trim();

            if (!_channelMap.TryGetValue(channelType, out var channel))
            {
                logger.LogWarning("No INotificationChannel registered for type '{ChannelType}'.", channelType);
                continue;
            }

            try
            {
                await channel.SendAsync(config, notification, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to dispatch notification via {ChannelType} for Incident {IncidentId}.",
                    channelType, notification.IncidentId);
            }
        }
    }
}
