using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Evaluation.Queries.GetFeedbackSummary;

/// <summary>
/// 获取闭环运行状态摘要（Spec 025 — FR-013）
/// </summary>
public record GetFeedbackSummaryQuery(DateTime? From = null, DateTime? To = null)
    : IRequest<Result<FeedbackSummaryDto>>;

public record FeedbackSummaryDto(
    int TotalIncidents,
    int FallbackCount,
    double FallbackRate,
    int CanaryResultCount,
    double CanaryConsistencyRate,
    int PromptSuggestionsTotal,
    int PromptSuggestionsApplied,
    double PromptAdoptionRate,
    int SopsAutoDisabledCount,
    int SopsDegradedCount);
