using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Application.Skills.Commands.UpdateSopExecutionStats;

public class UpdateSopExecutionStatsCommandHandler(
    ISkillRegistrationRepository skillRepo,
    ILogger<UpdateSopExecutionStatsCommandHandler> logger)
    : IRequestHandler<UpdateSopExecutionStatsCommand, Result<bool>>
{
    private const double DegradedThreshold = 0.40;
    private const int DegradedMinExecutions = 10;

    public async Task<Result<bool>> Handle(
        UpdateSopExecutionStatsCommand request, CancellationToken cancellationToken)
    {
        var sop = await skillRepo.GetByIdAsync(request.SopId, cancellationToken);
        if (sop is null)
            return Result<bool>.NotFound($"SOP '{request.SopId}' not found.");

        sop.RecordExecution(request.Success, request.Timeout, request.MttrMs);

        // 自动降级检测：成功率持续低于阈值
        var stats = sop.ExecutionStats;
        if (stats is not null
            && stats.RecentResults.Count >= DegradedMinExecutions
            && stats.RollingSuccessRate < DegradedThreshold)
        {
            sop.MarkDegraded();
            logger.LogWarning(
                "SOP '{SopId}' ({Name}) marked as Degraded. Rolling success rate: {Rate:P1}.",
                sop.Id, sop.Name, stats.RollingSuccessRate);
        }

        await skillRepo.UpdateAsync(sop, cancellationToken);
        return Result<bool>.Ok(true);
    }
}
