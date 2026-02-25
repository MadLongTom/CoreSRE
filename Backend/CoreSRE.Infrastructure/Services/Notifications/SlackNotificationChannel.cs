using System.Text;
using System.Text.Json;
using CoreSRE.Application.Alerts.Interfaces;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services.Notifications;

/// <summary>
/// Slack Webhook 通知渠道实现。
/// channelConfig 格式: Webhook URL (https://hooks.slack.com/services/...)
/// </summary>
public class SlackNotificationChannel(
    IHttpClientFactory httpClientFactory,
    ILogger<SlackNotificationChannel> logger) : INotificationChannel
{
    public string ChannelType => "slack";

    public async Task SendAsync(
        string channelConfig,
        IncidentNotification notification,
        CancellationToken cancellationToken = default)
    {
        var webhookUrl = channelConfig;
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            logger.LogWarning("Slack webhook URL is empty. Skipping notification for Incident {IncidentId}.",
                notification.IncidentId);
            return;
        }

        var payload = new
        {
            text = $"🚨 *[{notification.Severity}] {notification.Title}*",
            blocks = new object[]
            {
                new
                {
                    type = "section",
                    text = new
                    {
                        type = "mrkdwn",
                        text = $"*[{notification.Severity}] {notification.Title}*\n" +
                               $"状态: {notification.Status} | 路由: {notification.Route}\n" +
                               $"告警: {notification.AlertName}\n" +
                               (notification.Summary is not null ? $"摘要: {notification.Summary}" : "")
                    }
                }
            }
        };

        try
        {
            var client = httpClientFactory.CreateClient("NotificationChannel");
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(webhookUrl, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            logger.LogInformation("Slack notification sent for Incident {IncidentId}.", notification.IncidentId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send Slack notification for Incident {IncidentId}.", notification.IncidentId);
        }
    }
}
