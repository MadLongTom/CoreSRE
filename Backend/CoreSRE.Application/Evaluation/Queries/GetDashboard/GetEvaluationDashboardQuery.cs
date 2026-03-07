using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Evaluation.Queries.GetDashboard;

/// <summary>
/// 获取评估仪表盘聚合指标
/// </summary>
public record GetEvaluationDashboardQuery(
    DateTime? From = null,
    DateTime? To = null) : IRequest<Result<EvaluationDashboardDto>>;

/// <summary>
/// 评估仪表盘数据
/// </summary>
public record EvaluationDashboardDto
{
    /// <summary>总 Incident 数</summary>
    public int TotalIncidents { get; init; }

    /// <summary>自动修复率 = Chain A 成功数 / 总 Incident 数</summary>
    public double AutoResolveRate { get; init; }

    /// <summary>平均 MTTR（毫秒）</summary>
    public double AverageMttrMs { get; init; }

    /// <summary>按 Severity 分组的平均 MTTR</summary>
    public Dictionary<string, double> MttrBySeverity { get; init; } = new();

    /// <summary>SOP 覆盖率 = 有 SOP 的 AlertRule 数 / 总 AlertRule 数</summary>
    public double SopCoverageRate { get; init; }

    /// <summary>人工介入率</summary>
    public double HumanInterventionRate { get; init; }

    /// <summary>超时率</summary>
    public double TimeoutRate { get; init; }

    /// <summary>RCA 准确率（按 Post-mortem 标注计算）</summary>
    public double? RcaAccuracyRate { get; init; }

    /// <summary>有 Post-mortem 标注的 Incident 数</summary>
    public int AnnotatedIncidentCount { get; init; }
}
