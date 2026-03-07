using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Alerts.Queries.GetCanaryReport;

/// <summary>
/// 获取 AlertRule 金丝雀验证报告（Spec 025 — US3）
/// </summary>
public record GetCanaryReportQuery(Guid AlertRuleId) : IRequest<Result<CanaryReportDto>>;

public record CanaryReportDto(
    Guid AlertRuleId,
    Guid? CanarySopId,
    int TotalResults,
    double ConsistencyRate,
    double AverageTokenDifference,
    List<CanaryResultSummaryDto> Results);

public record CanaryResultSummaryDto(
    Guid IncidentId,
    bool IsConsistent,
    string? ShadowRootCause,
    string? ActualRootCause,
    int ShadowTokenConsumed,
    long ShadowDurationMs,
    DateTime CreatedAt);

public class GetCanaryReportQueryHandler(
    IAlertRuleRepository alertRuleRepo,
    ICanaryResultRepository canaryResultRepo)
    : IRequestHandler<GetCanaryReportQuery, Result<CanaryReportDto>>
{
    public async Task<Result<CanaryReportDto>> Handle(
        GetCanaryReportQuery request, CancellationToken cancellationToken)
    {
        var alertRule = await alertRuleRepo.GetByIdAsync(request.AlertRuleId, cancellationToken);
        if (alertRule is null)
            return Result<CanaryReportDto>.NotFound($"AlertRule '{request.AlertRuleId}' not found.");

        var results = (await canaryResultRepo.GetByAlertRuleIdAsync(
            request.AlertRuleId, cancellationToken)).ToList();

        var consistencyRate = results.Count > 0
            ? (double)results.Count(r => r.IsConsistent) / results.Count : 0;

        var avgTokens = results.Count > 0
            ? results.Average(r => r.ShadowTokenConsumed) : 0;

        var dto = new CanaryReportDto(
            AlertRuleId: request.AlertRuleId,
            CanarySopId: alertRule.CanarySopId,
            TotalResults: results.Count,
            ConsistencyRate: consistencyRate,
            AverageTokenDifference: avgTokens,
            Results: results.Select(r => new CanaryResultSummaryDto(
                r.IncidentId,
                r.IsConsistent,
                r.ShadowRootCause,
                r.ActualRootCause,
                r.ShadowTokenConsumed,
                r.ShadowDurationMs,
                r.CreatedAt)).ToList());

        return Result<CanaryReportDto>.Ok(dto);
    }
}
