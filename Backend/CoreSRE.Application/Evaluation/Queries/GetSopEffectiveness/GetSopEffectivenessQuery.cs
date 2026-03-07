using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Evaluation.Queries.GetSopEffectiveness;

/// <summary>
/// 获取 SOP 效能排名
/// </summary>
public record GetSopEffectivenessQuery(
    DateTime? From = null,
    DateTime? To = null) : IRequest<Result<List<SopEffectivenessDto>>>;

public record SopEffectivenessDto
{
    /// <summary>SOP（SkillRegistration）ID</summary>
    public Guid SopId { get; init; }

    /// <summary>SOP 名称</summary>
    public string SopName { get; init; } = string.Empty;

    /// <summary>使用次数</summary>
    public int UsageCount { get; init; }

    /// <summary>成功率（Resolved / 总使用次数）</summary>
    public double SuccessRate { get; init; }

    /// <summary>平均执行时间（毫秒）</summary>
    public double AverageExecutionMs { get; init; }

    /// <summary>人工介入次数</summary>
    public int HumanInterventionCount { get; init; }
}
