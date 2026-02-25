using System.Text.Json;
using CoreSRE.Application.Alerts.Interfaces;
using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Application.Alerts.Commands.DispatchSopExecution;

/// <summary>
/// 链路 A Handler：创建 Incident(Route=SopExecution) → 派发后台 SOP 执行。
/// </summary>
public class DispatchSopExecutionCommandHandler(
    IIncidentRepository incidentRepository,
    IIncidentDispatcher dispatcher,
    IConversationRepository conversationRepository,
    ILogger<DispatchSopExecutionCommandHandler> logger)
    : IRequestHandler<DispatchSopExecutionCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        DispatchSopExecutionCommand request,
        CancellationToken cancellationToken)
    {
        // 1. 创建 Incident
        JsonDocument? alertPayload = null;
        if (request.AlertPayload is not null)
        {
            try { alertPayload = JsonDocument.Parse(request.AlertPayload); }
            catch { /* ignore parse failures */ }
        }

        var incident = Incident.CreateForSopExecution(
            title: $"[SOP] {request.AlertName}",
            severity: request.Severity,
            alertRuleId: request.AlertRuleId,
            alertFingerprint: request.Fingerprint,
            alertPayload: alertPayload,
            alertLabels: request.AlertLabels,
            sopId: request.SopId);

        await incidentRepository.AddAsync(incident, cancellationToken);

        // 2. 确定 ResponderAgent
        var agentId = request.ResponderAgentId;
        if (agentId is null || agentId == Guid.Empty)
        {
            logger.LogWarning(
                "SOP execution dispatched for Incident {IncidentId} without ResponderAgentId. SOP will not execute automatically.",
                incident.Id);
            return Result<Guid>.Ok(incident.Id);
        }

        // 3. 创建 Conversation 并关联到 Incident
        var conversation = Conversation.Create(agentId.Value);
        await conversationRepository.AddAsync(conversation, cancellationToken);
        incident.SetConversation(conversation.Id);
        incident.TransitionTo(IncidentStatus.Investigating);
        incident.AddTimelineEvent(TimelineEventType.SopStarted,
            $"SOP 自动执行已启动 (Agent: {agentId}, SOP: {request.SopId})");
        await incidentRepository.UpdateAsync(incident, cancellationToken);

        // 4. Fire-and-forget 后台执行
        _ = Task.Run(async () =>
        {
            try
            {
                await dispatcher.DispatchSopExecutionAsync(
                    incidentId: incident.Id,
                    agentId: agentId.Value,
                    sopId: request.SopId,
                    alertName: request.AlertName,
                    alertLabels: request.AlertLabels,
                    alertAnnotations: request.AlertAnnotations,
                    cancellationToken: CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Background SOP execution failed for Incident {IncidentId}", incident.Id);
            }
        }, CancellationToken.None);

        logger.LogInformation(
            "Incident {IncidentId} created (SOP={SopId}, Agent={AgentId}). Background execution dispatched.",
            incident.Id, request.SopId, agentId);

        return Result<Guid>.Ok(incident.Id);
    }
}
