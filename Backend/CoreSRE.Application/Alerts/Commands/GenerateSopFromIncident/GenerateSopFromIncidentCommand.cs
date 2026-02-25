using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Alerts.Commands.GenerateSopFromIncident;

/// <summary>
/// 链路 C：从根因分析结果自动生成 SOP。
/// 由 DispatchRootCauseAnalysis 完成后触发。
/// </summary>
public record GenerateSopFromIncidentCommand : IRequest<Result<Guid?>>
{
    /// <summary>Incident ID</summary>
    public Guid IncidentId { get; init; }

    /// <summary>AlertRule ID（用于自动更新 SopId + ResponderAgentId）</summary>
    public Guid AlertRuleId { get; init; }

    /// <summary>总结 Agent ID（ChatClient Agent）</summary>
    public Guid? SummarizerAgentId { get; init; }

    /// <summary>告警名称</summary>
    public string AlertName { get; init; } = string.Empty;

    /// <summary>告警标签</summary>
    public Dictionary<string, string> AlertLabels { get; init; } = new();

    /// <summary>根因分析结论</summary>
    public string? RootCause { get; init; }
}
