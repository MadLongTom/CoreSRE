using CoreSRE.Domain.Enums;

namespace CoreSRE.Application.Alerts.Interfaces;

/// <summary>
/// Incident 后台处置派发器接口。
/// 接收创建好的 Incident ID 后在后台执行 Agent 对话。
/// </summary>
public interface IIncidentDispatcher
{
    /// <summary>后台执行 SOP（链路 A）</summary>
    Task DispatchSopExecutionAsync(
        Guid incidentId,
        Guid agentId,
        Guid sopId,
        string alertName,
        Dictionary<string, string> alertLabels,
        Dictionary<string, string> alertAnnotations,
        CancellationToken cancellationToken = default);

    /// <summary>后台执行根因分析（链路 B）</summary>
    Task DispatchRootCauseAnalysisAsync(
        Guid incidentId,
        Guid teamAgentId,
        Guid? summarizerAgentId,
        string alertName,
        Dictionary<string, string> alertLabels,
        Dictionary<string, string> alertAnnotations,
        CancellationToken cancellationToken = default);
}
