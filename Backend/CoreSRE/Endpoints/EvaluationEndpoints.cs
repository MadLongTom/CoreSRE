using CoreSRE.Application.Evaluation.Queries.GetDashboard;
using CoreSRE.Application.Evaluation.Queries.GetFeedbackSummary;
using CoreSRE.Application.Evaluation.Queries.GetSopEffectiveness;
using CoreSRE.Application.Incidents.Commands.AnnotatePostMortem;
using CoreSRE.Domain.Enums;
using MediatR;

namespace CoreSRE.Endpoints;

/// <summary>
/// Agent 评估框架 REST API 端点（Spec 023 + 025）
/// </summary>
public static class EvaluationEndpoints
{
    public static IEndpointRouteBuilder MapEvaluationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/evaluation")
            .WithTags("Evaluation");

        group.MapGet("/dashboard", GetDashboard);
        group.MapGet("/sops", GetSopEffectiveness);

        // Spec 025 — 闭环反馈摘要
        group.MapGet("/feedback-summary", GetFeedbackSummary);

        return app;
    }

    /// <summary>GET /api/evaluation/dashboard — 评估仪表盘聚合指标</summary>
    private static async Task<IResult> GetDashboard(
        ISender sender,
        DateTime? from = null,
        DateTime? to = null)
    {
        var result = await sender.Send(new GetEvaluationDashboardQuery(from, to));
        return Results.Ok(result);
    }

    /// <summary>GET /api/evaluation/sops — SOP 效能排名</summary>
    private static async Task<IResult> GetSopEffectiveness(
        ISender sender,
        DateTime? from = null,
        DateTime? to = null)
    {
        var result = await sender.Send(new GetSopEffectivenessQuery(from, to));
        return Results.Ok(result);
    }

    /// <summary>GET /api/evaluation/feedback-summary — 闭环反馈状态摘要（Spec 025）</summary>
    private static async Task<IResult> GetFeedbackSummary(
        ISender sender,
        DateTime? from = null,
        DateTime? to = null)
    {
        var result = await sender.Send(new GetFeedbackSummaryQuery(from, to));
        if (!result.Success)
            return Results.BadRequest(result);
        return Results.Ok(result);
    }
}
