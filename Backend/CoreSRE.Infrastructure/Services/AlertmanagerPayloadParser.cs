using System.Text.Json;
using CoreSRE.Application.Alerts.DTOs;
using CoreSRE.Application.Alerts.Interfaces;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// 解析 Alertmanager JSON → List&lt;AlertVO&gt;。
/// Alertmanager payload 格式: { "version": "4", "alerts": [ { "status": "firing", "labels": { ... }, ... } ] }
/// </summary>
public class AlertmanagerPayloadParser(ILogger<AlertmanagerPayloadParser> logger) : IAlertmanagerPayloadParser
{
    public List<AlertVO> Parse(string jsonPayload)
    {
        var alerts = new List<AlertVO>();

        if (string.IsNullOrWhiteSpace(jsonPayload))
            return alerts;

        try
        {
            using var doc = JsonDocument.Parse(jsonPayload);
            var root = doc.RootElement;

            if (!root.TryGetProperty("alerts", out var alertsElement) ||
                alertsElement.ValueKind != JsonValueKind.Array)
            {
                logger.LogWarning("Alertmanager payload does not contain 'alerts' array.");
                return alerts;
            }

            foreach (var alertEl in alertsElement.EnumerateArray())
            {
                var alert = new AlertVO
                {
                    Fingerprint = GetStringProp(alertEl, "fingerprint"),
                    Status = GetStringProp(alertEl, "status"),
                    Labels = GetDictProp(alertEl, "labels"),
                    Annotations = GetDictProp(alertEl, "annotations"),
                    GeneratorUrl = GetStringPropOrNull(alertEl, "generatorURL"),
                    RawJson = alertEl.GetRawText()
                };

                if (alertEl.TryGetProperty("startsAt", out var startsAt) &&
                    DateTime.TryParse(startsAt.GetString(), out var startsAtParsed))
                {
                    alert.StartsAt = startsAtParsed;
                }

                if (alertEl.TryGetProperty("endsAt", out var endsAt))
                {
                    var endsAtStr = endsAt.GetString();
                    if (DateTime.TryParse(endsAtStr, out var endsAtParsed) &&
                        endsAtParsed > alert.StartsAt)
                    {
                        alert.EndsAt = endsAtParsed;
                    }
                }

                alerts.Add(alert);
            }
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse Alertmanager JSON payload.");
        }

        return alerts;
    }

    private static string GetStringProp(JsonElement el, string name) =>
        el.TryGetProperty(name, out var prop) ? prop.GetString() ?? string.Empty : string.Empty;

    private static string? GetStringPropOrNull(JsonElement el, string name) =>
        el.TryGetProperty(name, out var prop) ? prop.GetString() : null;

    private static Dictionary<string, string> GetDictProp(JsonElement el, string name)
    {
        var dict = new Dictionary<string, string>();
        if (!el.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.Object)
            return dict;

        foreach (var kv in prop.EnumerateObject())
        {
            dict[kv.Name] = kv.Value.GetString() ?? string.Empty;
        }

        return dict;
    }
}
