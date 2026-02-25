using System.Text.Json;
using CoreSRE.Application.Alerts.Interfaces;
using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Application.Alerts.Commands.DispatchRootCauseAnalysis;

/// <summary>
/// 链路 B Handler：创建 Incident(Route=RootCauseAnalysis) → 派发后台根因分析。
/// </summary>
public class DispatchRootCauseAnalysisCommandHandler(
    IIncidentRepository incidentRepository,
    IIncidentDispatcher dispatcher,
    IConversationRepository conversationRepository,
    ILogger<DispatchRootCauseAnalysisCommandHandler> logger)
    : IRequestHandler<DispatchRootCauseAnalysisCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        DispatchRootCauseAnalysisCommand request,
        CancellationToken cancellationToken)
    {
        // 1. 创建 Incident
        JsonDocument? alertPayload = null;
        if (request.AlertPayload is not null)
        {
            try { alertPayload = JsonDocument.Parse(request.AlertPayload); }
            catch { /* ignore parse failures */ }
        }

        var incident = Incident.CreateForRootCauseAnalysis(
            title: $"[RCA] {request.AlertName}",
            severity: request.Severity,
            alertRuleId: request.AlertRuleId,
            alertFingerprint: request.Fingerprint,
            alertPayload: alertPayload,
            alertLabels: request.AlertLabels);

        await incidentRepository.AddAsync(incident, cancellationToken);

        // 2. 确定 TeamAgent
        var teamAgentId = request.TeamAgentId;
        if (teamAgentId is null || teamAgentId == Guid.Empty)
        {
            logger.LogWarning(
                "RCA dispatched for Incident {IncidentId} without TeamAgentId. Analysis will not execute automatically.",
                incident.Id);
            return Result<Guid>.Ok(incident.Id);
        }

        // 3. 创建 Conversation 并关联
        var conversation = Conversation.Create(teamAgentId.Value);
        await conversationRepository.AddAsync(conversation, cancellationToken);
        incident.SetConversation(conversation.Id);
        incident.TransitionTo(IncidentStatus.Investigating);
        incident.AddTimelineEvent(TimelineEventType.RcaStarted,
            $"根因分析已启动 (Team Agent: {teamAgentId})");
        await incidentRepository.UpdateAsync(incident, cancellationToken);

        // 4. Fire-and-forget 后台执行
        _ = Task.Run(async () =>
        {
            try
            {
                await dispatcher.DispatchRootCauseAnalysisAsync(
                    incidentId: incident.Id,
                    teamAgentId: teamAgentId.Value,
                    summarizerAgentId: request.SummarizerAgentId,
                    alertName: request.AlertName,
                    alertLabels: request.AlertLabels,
                    alertAnnotations: request.AlertAnnotations,
                    cancellationToken: CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Background RCA execution failed for Incident {IncidentId}", incident.Id);
            }
        }, CancellationToken.None);

        logger.LogInformation(
            "Incident {IncidentId} created (RCA, TeamAgent={TeamAgentId}). Background analysis dispatched.",
            incident.Id, teamAgentId);

        return Result<Guid>.Ok(incident.Id);
    }
}
