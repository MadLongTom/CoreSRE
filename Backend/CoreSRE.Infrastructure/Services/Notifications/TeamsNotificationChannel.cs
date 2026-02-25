using System.Text;
using System.Text.Json;
using CoreSRE.Application.Alerts.Interfaces;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services.Notifications;

/// <summary>
/// Microsoft Teams Webhook 通知渠道实现（Adaptive Card）。
/// channelConfig 格式: Incoming Webhook URL
/// </summary>
public class TeamsNotificationChannel(
    IHttpClientFactory httpClientFactory,
    ILogger<TeamsNotificationChannel> logger) : INotificationChannel
{
    public string ChannelType => "teams";

    public async Task SendAsync(
        string channelConfig,
        IncidentNotification notification,
        CancellationToken cancellationToken = default)
    {
        var webhookUrl = channelConfig;
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            logger.LogWarning("Teams webhook URL is empty. Skipping notification for Incident {IncidentId}.",
                notification.IncidentId);
            return;
        }

        // Adaptive Card payload
        var payload = new
        {
            type = "message",
            attachments = new object[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content = new
                    {
                        type = "AdaptiveCard",
                        version = "1.4",
                        body = new object[]
                        {
                            new
                            {
                                type = "TextBlock",
                                text = $"🚨 [{notification.Severity}] {notification.Title}",
                                weight = "bolder",
                                size = "medium"
                            },
                            new
                            {
                                type = "FactSet",
                                facts = new object[]
                                {
                                    new { title = "状态", value = notification.Status },
                                    new { title = "路由", value = notification.Route },
                                    new { title = "告警", value = notification.AlertName },
                                }
                            }
                        }
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

            logger.LogInformation("Teams notification sent for Incident {IncidentId}.", notification.IncidentId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send Teams notification for Incident {IncidentId}.", notification.IncidentId);
        }
    }
}
