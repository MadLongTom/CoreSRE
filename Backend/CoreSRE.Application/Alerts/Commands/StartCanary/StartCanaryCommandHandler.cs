using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Alerts.Commands.StartCanary;

public class StartCanaryCommandHandler(
    IAlertRuleRepository alertRuleRepo,
    ISkillRegistrationRepository skillRepo)
    : IRequestHandler<StartCanaryCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        StartCanaryCommand request, CancellationToken cancellationToken)
    {
        var alertRule = await alertRuleRepo.GetByIdAsync(request.AlertRuleId, cancellationToken);
        if (alertRule is null)
            return Result<bool>.NotFound($"AlertRule '{request.AlertRuleId}' not found.");

        if (alertRule.CanaryMode)
            return Result<bool>.Fail("AlertRule is already in Canary mode.");

        var sop = await skillRepo.GetByIdAsync(request.CanarySopId, cancellationToken);
        if (sop is null)
            return Result<bool>.NotFound($"SOP '{request.CanarySopId}' not found.");

        if (sop.Status != SkillStatus.Reviewed)
            return Result<bool>.Fail($"SOP must be in Reviewed status to start canary. Current: {sop.Status}.");

        alertRule.StartCanary(request.CanarySopId);
        await alertRuleRepo.UpdateAsync(alertRule, cancellationToken);
        return Result<bool>.Ok(true);
    }
}
