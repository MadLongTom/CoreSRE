using CoreSRE.Application.Alerts.Interfaces;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Incidents.Models;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using MediatR;

namespace CoreSRE.Application.Incidents.Commands.RespondToIntervention;

public class RespondToInterventionCommandHandler(
    IIncidentRepository incidentRepository,
    IActiveIncidentTracker sessionTracker,
    IIncidentNotifier notifier)
    : IRequestHandler<RespondToInterventionCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        RespondToInterventionCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Validate incident exists
        var incident = await incidentRepository.GetByIdAsync(request.IncidentId, cancellationToken);
        if (incident is null)
            return Result<bool>.NotFound($"Incident '{request.IncidentId}' not found.");

        // 2. Check if agent is actively processing
        if (!sessionTracker.IsActive(request.IncidentId))
            return Result<bool>.Fail("Incident 当前没有活跃的 Agent 对话。", errorCode: 409);

        // 3. Validate pending request exists
        var pendingRequest = sessionTracker.GetPendingRequest(request.RequestId);
        if (pendingRequest is null)
            return Result<bool>.Fail($"未找到待处理的干预请求 '{request.RequestId}'。", errorCode: 404);

        // 4. Parse response type
        if (!Enum.TryParse<InterventionResponseType>(request.ResponseType, true, out var responseType))
            return Result<bool>.Fail($"无效的响应类型: '{request.ResponseType}'。", errorCode: 400);

        // 5. Build response and complete the TaskCompletionSource
        var response = new InterventionResponse(
            RequestId: request.RequestId,
            Type: responseType,
            Content: request.Content,
            Approved: request.Approved,
            OperatorName: request.OperatorName,
            Timestamp: DateTime.UtcNow);

        if (!sessionTracker.TryRespondToRequest(request.RequestId, response))
            return Result<bool>.Fail("请求已被处理或已过期。", errorCode: 409);

        // 6. Record timeline event
        var summary = pendingRequest.Type switch
        {
            InterventionRequestType.ToolApproval =>
                request.Approved == true
                    ? $"✅ 批准工具: {pendingRequest.ToolApproval?.ToolName}"
                    : $"❌ 拒绝工具: {pendingRequest.ToolApproval?.ToolName}",
            InterventionRequestType.FreeTextInput =>
                $"人工输入: {(request.Content?.Length > 100 ? request.Content[..100] + "…" : request.Content)}",
            InterventionRequestType.Choice =>
                $"人工选择: {request.Content}",
            _ => $"干预响应: {request.ResponseType}"
        };

        var eventType = pendingRequest.Type == InterventionRequestType.ToolApproval
            ? TimelineEventType.ToolApprovalResponded
            : TimelineEventType.HumanIntervention;

        incident.AddTimelineEvent(IncidentTimelineVO.Create(eventType, summary, request.Content));
        await incidentRepository.UpdateAsync(incident, cancellationToken);

        // 7. Push acknowledgement to SignalR
        await notifier.InterventionRequestResolvedAsync(
            request.IncidentId, request.RequestId,
            responseType.ToString(),
            responseContent: request.Content,
            approved: request.Approved,
            operatorName: request.OperatorName,
            timestamp: DateTime.UtcNow,
            ct: cancellationToken);

        return Result<bool>.Ok(true);
    }
}
