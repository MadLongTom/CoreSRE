using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Skills.Commands.UpdateSopExecutionStats;

/// <summary>
/// 更新 SOP 执行统计（Spec 025 — US2）
/// </summary>
public record UpdateSopExecutionStatsCommand(
    Guid SopId,
    bool Success,
    bool Timeout,
    long MttrMs) : IRequest<Result<bool>>;
