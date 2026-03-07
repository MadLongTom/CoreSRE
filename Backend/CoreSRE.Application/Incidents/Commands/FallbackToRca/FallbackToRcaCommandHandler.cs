using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Application.Incidents.Commands.FallbackToRca;

public class FallbackToRcaCommandHandler(
    IIncidentRepository incidentRepository,
    IAlertRuleRepository alertRuleRepository,
    ILogger<FallbackToRcaCommandHandler> logger)
    : IRequestHandler<FallbackToRcaCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        FallbackToRcaCommand request, CancellationToken cancellationToken)
    {
        var incident = await incidentRepository.GetByIdAsync(request.IncidentId, cancellationToken);
        if (incident is null)
            return Result<bool>.NotFound($"Incident '{request.IncidentId}' not found.");

        if (incident.Route != IncidentRoute.SopExecution)
            return Result<bool>.Fail($"Incident is not on SOP execution route. Current: {incident.Route}.");

        if (incident.AlertRuleId is null)
            return Result<bool>.Fail("Incident has no associated AlertRule.");

        var alertRule = await alertRuleRepository.GetByIdAsync(incident.AlertRuleId.Value, cancellationToken);
        if (alertRule?.TeamAgentId is null)
            return Result<bool>.Fail("AlertRule has no configured TeamAgentId for RCA fallback.");

        // 执行降级
        incident.FallbackToRca(request.Reason);
        await incidentRepository.UpdateAsync(incident, cancellationToken);

        logger.LogWarning(
            "Incident '{IncidentId}' fallback from SOP execution to RCA. Reason: {Reason}",
            request.IncidentId, request.Reason);

        // 注意：实际的 RCA Chain B 调度由 IncidentDispatcherService 负责检测
        // FallbackRca 路由并启动多 Agent 根因分析。这里只完成状态变更。
        return Result<bool>.Ok(true);
    }
}
