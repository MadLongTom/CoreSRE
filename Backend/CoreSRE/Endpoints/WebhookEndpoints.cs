using System.Text.Json;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using MediatR;

namespace CoreSRE.Endpoints;

/// <summary>
/// DataSource Webhook 端点。接收外部告警推送（如 Alertmanager webhook）。
/// </summary>
public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/datasources/webhook")
            .WithTags("DataSource Webhooks")
            .WithOpenApi();

        group.MapPost("/{dataSourceId:guid}", ReceiveWebhook);

        return app;
    }

    /// <summary>
    /// POST /api/datasources/webhook/{dataSourceId}
    /// 接收 Alertmanager / PagerDuty 等告警推送。
    /// 当前版本仅日志记录 + 返回 200 ACK，后续可触发 AIOps 工作流。
    /// </summary>
    private static async Task<IResult> ReceiveWebhook(
        Guid dataSourceId,
        HttpRequest request,
        IDataSourceRegistrationRepository repository,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("WebhookEndpoints");

        // Verify datasource exists
        var ds = await repository.GetByIdAsync(dataSourceId);
        if (ds is null)
            return Results.NotFound(new { success = false, message = $"DataSource '{dataSourceId}' not found." });

        // Read raw payload
        string payload;
        using (var reader = new StreamReader(request.Body))
        {
            payload = await reader.ReadToEndAsync();
        }

        logger.LogInformation(
            "Webhook received for DataSource '{Name}' (ID: {Id}, Product: {Product}). Payload size: {Size} bytes",
            ds.Name, ds.Id, ds.Product, payload.Length);

        // Parse and count alerts (Alertmanager format)
        var alertCount = 0;
        try
        {
            var json = JsonDocument.Parse(payload);
            if (json.RootElement.TryGetProperty("alerts", out var alerts) && alerts.ValueKind == JsonValueKind.Array)
            {
                alertCount = alerts.GetArrayLength();
                logger.LogInformation(
                    "Webhook contains {AlertCount} alerts for DataSource '{Name}'", alertCount, ds.Name);
            }
        }
        catch (JsonException)
        {
            // Not JSON or not Alertmanager format — that's okay, still ACK
            logger.LogDebug("Webhook payload is not Alertmanager JSON format for DataSource '{Name}'", ds.Name);
        }

        // ACK — webhook received successfully
        // TODO: In future, trigger AIOps workflow based on alert content
        return Results.Ok(new
        {
            success = true,
            message = "Webhook received",
            dataSourceId = ds.Id,
            dataSourceName = ds.Name,
            alertsReceived = alertCount
        });
    }
}
