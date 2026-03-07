using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Alerts.Commands.StopCanary;

public class StopCanaryCommandHandler(
    IAlertRuleRepository alertRuleRepo)
    : IRequestHandler<StopCanaryCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        StopCanaryCommand request, CancellationToken cancellationToken)
    {
        var alertRule = await alertRuleRepo.GetByIdAsync(request.AlertRuleId, cancellationToken);
        if (alertRule is null)
            return Result<bool>.NotFound($"AlertRule '{request.AlertRuleId}' not found.");

        if (!alertRule.CanaryMode)
            return Result<bool>.Fail("AlertRule is not in Canary mode.");

        alertRule.StopCanary();
        await alertRuleRepo.UpdateAsync(alertRule, cancellationToken);
        return Result<bool>.Ok(true);
    }
}
