using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Enums;
using MediatR;

namespace CoreSRE.Application.Alerts.Commands.DispatchSopExecution;

/// <summary>
/// 链路 A：SOP 自动执行派发命令。
/// 由 Webhook 路由匹配后发布，Handler 在 SPEC-113 实现。
/// </summary>
public record DispatchSopExecutionCommand : IRequest<Result<Guid>>
{
    /// <summary>匹配到的 AlertRule ID</summary>
    public Guid AlertRuleId { get; init; }

    /// <summary>关联的 SOP (SkillRegistration) ID</summary>
    public Guid SopId { get; init; }

    /// <summary>执行 SOP 的 ChatClient Agent ID</summary>
    public Guid? ResponderAgentId { get; init; }

    /// <summary>告警指纹</summary>
    public string Fingerprint { get; init; } = string.Empty;

    /// <summary>告警名称</summary>
    public string AlertName { get; init; } = string.Empty;

    /// <summary>事故严重等级</summary>
    public IncidentSeverity Severity { get; init; }

    /// <summary>告警标签</summary>
    public Dictionary<string, string> AlertLabels { get; init; } = new();

    /// <summary>告警注解</summary>
    public Dictionary<string, string> AlertAnnotations { get; init; } = new();

    /// <summary>原始告警 JSON</summary>
    public string? AlertPayload { get; init; }

    /// <summary>通知渠道</summary>
    public List<string> NotificationChannels { get; init; } = [];
}
