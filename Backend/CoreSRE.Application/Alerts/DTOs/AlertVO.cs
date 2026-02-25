namespace CoreSRE.Application.Alerts.DTOs;

/// <summary>
/// Alertmanager 单条告警解析结果。
/// </summary>
public class AlertVO
{
    /// <summary>告警唯一指纹（Alertmanager fingerprint）</summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>告警状态：firing / resolved</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>告警标签字典</summary>
    public Dictionary<string, string> Labels { get; set; } = new();

    /// <summary>告警注解字典（summary / description 等）</summary>
    public Dictionary<string, string> Annotations { get; set; } = new();

    /// <summary>告警名称（labels.alertname）</summary>
    public string AlertName => Labels.GetValueOrDefault("alertname", "unknown");

    /// <summary>告警开始时间</summary>
    public DateTime StartsAt { get; set; }

    /// <summary>告警结束时间</summary>
    public DateTime? EndsAt { get; set; }

    /// <summary>Generator URL</summary>
    public string? GeneratorUrl { get; set; }

    /// <summary>原始 JSON payload（备份）</summary>
    public string? RawJson { get; set; }
}
