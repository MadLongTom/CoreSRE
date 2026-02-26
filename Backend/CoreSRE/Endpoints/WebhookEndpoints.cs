using System.Text.Json;
using CoreSRE.Application.Alerts.Commands.DispatchRootCauseAnalysis;
using CoreSRE.Application.Alerts.Commands.DispatchSopExecution;
using CoreSRE.Application.Alerts.DTOs;
using CoreSRE.Application.Alerts.Interfaces;
using CoreSRE.Application.Alerts.Queries.MatchAlertRules;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using MediatR;

namespace CoreSRE.Endpoints;

/// <summary>
/// DataSource Webhook 端点。接收 Alertmanager 告警推送 → 解析 → 匹配规则 → 去重 → 派发处置命令。
/// </summary>
public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/datasources/webhook")
            .WithTags("DataSource Webhooks");

        group.MapPost("/{dataSourceId:guid}", ReceiveWebhook);

        return app;
    }

    /// <summary>
    /// POST /api/datasources/webhook/{dataSourceId}
    /// 接收 Alertmanager 告警推送 → 解析 → 匹配 → 去重 → 派发。
    /// </summary>
    private static async Task<IResult> ReceiveWebhook(
        Guid dataSourceId,
        HttpRequest request,
        IDataSourceRegistrationRepository dsRepository,
        IAlertmanagerPayloadParser parser,
        IIncidentRepository incidentRepository,
        ISender sender,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("WebhookEndpoints");

        // 1. 验证 DataSource 存在
        var ds = await dsRepository.GetByIdAsync(dataSourceId);
        if (ds is null)
            return Results.NotFound(new { success = false, message = $"DataSource '{dataSourceId}' not found." });

        // 2. 读取 raw payload
        string payload;
        using (var reader = new StreamReader(request.Body))
        {
            payload = await reader.ReadToEndAsync();
        }

        logger.LogInformation(
            "Webhook received for DataSource '{Name}' (ID: {Id}). Payload size: {Size} bytes",
            ds.Name, ds.Id, payload.Length);

        // 3. 解析 Alertmanager payload
        var alerts = parser.Parse(payload);
        if (alerts.Count == 0)
        {
            return Results.Ok(new { success = true, message = "No alerts in payload", incidentIds = Array.Empty<Guid>(), ignoredCount = 0 });
        }

        // 4. 匹配 AlertRule
        var matchResult = await sender.Send(new MatchAlertRulesQuery(alerts));
        if (!matchResult.Success || matchResult.Data is null)
        {
            logger.LogWarning("Alert rule matching failed: {Message}", matchResult.Message);
            return Results.Ok(new { success = true, message = "Webhook received but rule matching failed", incidentIds = Array.Empty<Guid>(), ignoredCount = alerts.Count });
        }

        var matches = matchResult.Data;
        var ignoredCount = alerts.Count - matches.Count;
        var incidentIds = new List<Guid>();
        var errors = new List<string>();

        logger.LogWarning("Webhook: {MatchCount} matches, {IgnoredCount} ignored", matches.Count, ignoredCount);

        // 5. 对每条匹配的告警：去重 → 派发
        foreach (var match in matches)
        {
            logger.LogWarning("Webhook: Processing match AlertRule={AlertRuleId}, Alert={AlertName}, Status={Status}, SopId={SopId}",
                match.AlertRuleId, match.Alert.AlertName, match.Alert.Status, match.SopId);

            // 5a. resolved 状态处理：自动关闭关联 Incident
            if (string.Equals(match.Alert.Status, "resolved", StringComparison.OrdinalIgnoreCase))
            {
                await HandleResolvedAlert(match, incidentRepository, logger);
                continue;
            }

            // 5b. 去重检查
            var existing = await incidentRepository.FindActiveByFingerprintAsync(
                match.AlertRuleId, match.Alert.Fingerprint, match.CooldownMinutes);

            if (existing is not null)
            {
                // 冷却窗口内重复告警 → 追加 Timeline
                existing.AddTimelineEvent(IncidentTimelineVO.Create(
                    TimelineEventType.AlertRepeated,
                    $"Duplicate alert received (fingerprint: {match.Alert.Fingerprint})",
                    metadata: new Dictionary<string, string>
                    {
                        ["alertName"] = match.Alert.AlertName,
                        ["startsAt"] = match.Alert.StartsAt.ToString("O")
                    }));
                await incidentRepository.UpdateAsync(existing);

                logger.LogWarning(
                    "Webhook: Duplicate alert '{AlertName}' (fingerprint: {Fingerprint}) within cooldown. Appended to Incident {IncidentId}.",
                    match.Alert.AlertName, match.Alert.Fingerprint, existing.Id);
                continue;
            }

            // 5c. 派发 Command（创建 Incident 由 Handler 负责）
            try
            {
                if (match.SopId.HasValue)
                {
                    // 链路 A：SOP 自动执行
                    logger.LogWarning("Webhook: Dispatching SOP command for {AlertName}", match.Alert.AlertName);
                    var result = await sender.Send(new DispatchSopExecutionCommand
                    {
                        AlertRuleId = match.AlertRuleId,
                        SopId = match.SopId.Value,
                        ResponderAgentId = match.ResponderAgentId,
                        Fingerprint = match.Alert.Fingerprint,
                        AlertName = match.Alert.AlertName,
                        Severity = match.Severity,
                        AlertLabels = match.Alert.Labels,
                        AlertAnnotations = match.Alert.Annotations,
                        AlertPayload = match.Alert.RawJson,
                        NotificationChannels = match.NotificationChannels
                    });

                    logger.LogWarning("Webhook: SOP dispatch result: Success={Success}, Data={Data}, Message={Message}",
                        result.Success, result.Data, result.Message);

                    if (result.Success)
                        incidentIds.Add(result.Data);
                    else
                    {
                        var msg = $"SOP dispatch failed for '{match.Alert.AlertName}': {result.Message}";
                        logger.LogWarning(msg);
                        errors.Add(msg);
                    }
                }
                else
                {
                    // 链路 B：根因分析
                    logger.LogWarning("Webhook: Dispatching RCA command for {AlertName}", match.Alert.AlertName);
                    var result = await sender.Send(new DispatchRootCauseAnalysisCommand
                    {
                        AlertRuleId = match.AlertRuleId,
                        TeamAgentId = match.TeamAgentId,
                        SummarizerAgentId = match.SummarizerAgentId,
                        Fingerprint = match.Alert.Fingerprint,
                        AlertName = match.Alert.AlertName,
                        Severity = match.Severity,
                        AlertLabels = match.Alert.Labels,
                        AlertAnnotations = match.Alert.Annotations,
                        AlertPayload = match.Alert.RawJson,
                        NotificationChannels = match.NotificationChannels
                    });

                    logger.LogWarning("Webhook: RCA dispatch result: Success={Success}, Data={Data}, Message={Message}",
                        result.Success, result.Data, result.Message);

                    if (result.Success)
                        incidentIds.Add(result.Data);
                    else
                    {
                        var msg = $"RCA dispatch failed for '{match.Alert.AlertName}': {result.Message}";
                        logger.LogWarning(msg);
                        errors.Add(msg);
                    }
                }
            }
            catch (Exception ex)
            {
                var msg = $"Exception dispatching '{match.Alert.AlertName}': {ex.GetType().Name}: {ex.Message}";
                logger.LogError(ex, "Exception dispatching alert '{AlertName}' (fingerprint: {Fingerprint})",
                    match.Alert.AlertName, match.Alert.Fingerprint);
                errors.Add(msg);
            }
        }

        return Results.Ok(new
        {
            success = true,
            incidentIds,
            ignoredCount,
            errors
        });
    }

    /// <summary>
    /// 处理 resolved 状态的告警：查找关联的未关闭 Incident 并自动 Resolve。
    /// </summary>
    private static async Task HandleResolvedAlert(
        AlertRuleMatch match,
        IIncidentRepository incidentRepository,
        ILogger logger)
    {
        var incident = await incidentRepository.FindActiveByFingerprintAsync(
            match.AlertRuleId, match.Alert.Fingerprint, cooldownMinutes: 0);

        if (incident is null)
        {
            logger.LogDebug(
                "Resolved alert '{AlertName}' (fingerprint: {Fingerprint}) has no active Incident to close.",
                match.Alert.AlertName, match.Alert.Fingerprint);
            return;
        }

        incident.Resolve($"Auto-resolved: Alertmanager sent 'resolved' for {match.Alert.AlertName}");
        await incidentRepository.UpdateAsync(incident);

        logger.LogInformation(
            "Auto-resolved Incident {IncidentId} for alert '{AlertName}' (fingerprint: {Fingerprint}).",
            incident.Id, match.Alert.AlertName, match.Alert.Fingerprint);
    }
}
