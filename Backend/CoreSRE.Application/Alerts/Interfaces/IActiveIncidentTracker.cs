using CoreSRE.Application.Incidents.Models;

namespace CoreSRE.Application.Alerts.Interfaces;

/// <summary>
/// 活跃 Incident 会话追踪器接口。
/// - 结构化干预请求/响应 (Feature B)
/// - 主动人工消息注入
/// - TaskCompletionSource 暂停/恢复 (Feature C)
/// </summary>
public interface IActiveIncidentTracker
{
    /// <summary>检查 Incident 是否正在被 Agent 处理</summary>
    bool IsActive(Guid incidentId);

    /// <summary>注入主动人工消息（不是回复某个请求，而是人工主动发言）</summary>
    bool TryInjectMessage(Guid incidentId, string content, string? operatorName = null);

    /// <summary>Agent 发起结构化干预请求，阻塞等待人工响应 (Feature B + C)</summary>
    Task<InterventionResponse> RequestInterventionAsync(
        InterventionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>人工回复某个干预请求 — 解除 Agent 阻塞 (Feature C)</summary>
    bool TryRespondToRequest(string requestId, InterventionResponse response);

    /// <summary>获取某个待处理的请求</summary>
    InterventionRequest? GetPendingRequest(string requestId);

    /// <summary>获取某 Incident 所有待处理的干预请求</summary>
    IReadOnlyList<InterventionRequest> GetPendingRequestsForIncident(Guid incidentId);
}
