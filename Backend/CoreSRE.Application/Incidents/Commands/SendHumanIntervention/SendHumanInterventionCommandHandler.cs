using CoreSRE.Application.Alerts.Interfaces;
using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using MediatR;

namespace CoreSRE.Application.Incidents.Commands.SendHumanIntervention;

public class SendHumanInterventionCommandHandler(
    IIncidentRepository incidentRepository,
    IActiveIncidentTracker sessionTracker,
    IIncidentNotifier notifier)
    : IRequestHandler<SendHumanInterventionCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        SendHumanInterventionCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Validate incident exists
        var incident = await incidentRepository.GetByIdAsync(request.IncidentId, cancellationToken);
        if (incident is null)
            return Result<bool>.NotFound($"Incident '{request.IncidentId}' not found.");

        // 2. Check if agent is actively processing
        if (!sessionTracker.IsActive(request.IncidentId))
            return Result<bool>.Fail("Incident 当前没有活跃的 Agent 对话，无法注入人工消息。", errorCode: 409);

        // 3. Inject message into the active agent conversation
        if (!sessionTracker.TryInjectMessage(request.IncidentId, request.Message, request.OperatorName))
            return Result<bool>.Fail("消息注入失败，Agent 对话可能已结束。", errorCode: 409);

        // 4. Record timeline event
        incident.AddTimelineEvent(IncidentTimelineVO.Create(
            TimelineEventType.HumanIntervention,
            $"人工介入: {(request.Message.Length > 100 ? request.Message[..100] + "…" : request.Message)}",
            request.Message));

        await incidentRepository.UpdateAsync(incident, cancellationToken);

        // 5. Push to SignalR
        await notifier.ChatMessageReceivedAsync(
            request.IncidentId, "user", request.Message,
            request.OperatorName ?? "操作员", DateTime.UtcNow, cancellationToken);

        await notifier.TimelineEventAddedAsync(
            request.IncidentId, nameof(TimelineEventType.HumanIntervention),
            $"操作员人工介入",
            DateTime.UtcNow, ct: cancellationToken);

        return Result<bool>.Ok(true);
    }
}
