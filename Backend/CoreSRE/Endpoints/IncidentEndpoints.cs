using CoreSRE.Application.Alerts.Interfaces;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Incidents.Commands.AnnotatePostMortem;
using CoreSRE.Application.Incidents.Commands.RespondToIntervention;
using CoreSRE.Application.Incidents.Commands.SendHumanIntervention;
using CoreSRE.Application.Incidents.Commands.UpdateIncidentStatus;
using CoreSRE.Application.Incidents.DTOs;
using CoreSRE.Application.Incidents.Queries.GetIncidentById;
using CoreSRE.Application.Incidents.Queries.GetIncidentChatHistory;
using CoreSRE.Application.Incidents.Queries.ListIncidents;
using CoreSRE.Domain.Enums;
using MediatR;

namespace CoreSRE.Endpoints;

public static class IncidentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/incidents")
            .WithTags("Incidents");

        group.MapGet("/", GetIncidents);
        group.MapGet("/{id:guid}", GetIncidentById);
        group.MapGet("/{id:guid}/chat", GetIncidentChatHistory);
        group.MapGet("/{id:guid}/active", GetIncidentActiveStatus);
        group.MapPatch("/{id:guid}/status", UpdateIncidentStatus);
        group.MapPost("/{id:guid}/intervene", SendHumanIntervention);
        group.MapGet("/{id:guid}/interventions/pending", GetPendingInterventions);
        group.MapPost("/{id:guid}/interventions/{requestId}/respond", RespondToIntervention);

        // Post-mortem 标注（Spec 023）
        group.MapPost("/{id:guid}/post-mortem", AnnotatePostMortem);

        // 步骤重试（Spec 024）
        group.MapPost("/{id:guid}/steps/{stepNumber:int}/retry", RetryStepExecution);

        // SOP 执行降级（Spec 025）
        group.MapPost("/{id:guid}/fallback-rca", FallbackToRca);

        return app;
    }

    /// <summary>GET /api/incidents?status=&amp;severity=&amp;from=&amp;to=</summary>
    private static async Task<IResult> GetIncidents(
        ISender sender,
        string? status = null,
        string? severity = null,
        DateTime? from = null,
        DateTime? to = null)
    {
        var result = await sender.Send(new ListIncidentsQuery(status, severity, from, to));
        return Results.Ok(result);
    }

    /// <summary>GET /api/incidents/{id}</summary>
    private static async Task<IResult> GetIncidentById(Guid id, ISender sender)
    {
        var result = await sender.Send(new GetIncidentByIdQuery(id));
        if (!result.Success)
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                _ => Results.BadRequest(result)
            };
        return Results.Ok(result);
    }

    /// <summary>PATCH /api/incidents/{id}/status</summary>
    private static async Task<IResult> UpdateIncidentStatus(
        Guid id, UpdateStatusRequest request, ISender sender)
    {
        var result = await sender.Send(new UpdateIncidentStatusCommand(id, request.NewStatus, request.Note));
        if (!result.Success)
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                409 => Results.Conflict(result),
                _ => Results.BadRequest(result)
            };
        return Results.Ok(result);
    }

    /// <summary>GET /api/incidents/{id}/chat — 获取 Incident 关联的对话历史</summary>
    private static async Task<IResult> GetIncidentChatHistory(Guid id, ISender sender)
    {
        var result = await sender.Send(new GetIncidentChatHistoryQuery(id));
        if (!result.Success)
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                _ => Results.BadRequest(result)
            };
        return Results.Ok(result);
    }

    /// <summary>GET /api/incidents/{id}/active — 检查 Incident 是否有活跃的 Agent 处理</summary>
    private static Task<IResult> GetIncidentActiveStatus(Guid id, IActiveIncidentTracker tracker)
    {
        var isActive = tracker.IsActive(id);
        return Task.FromResult(Results.Ok(new { isActive }));
    }

    /// <summary>POST /api/incidents/{id}/intervene — 向活跃 Incident 的 Agent 对话注入人工消息</summary>
    private static async Task<IResult> SendHumanIntervention(
        Guid id, HumanInterventionRequest request, ISender sender)
    {
        var result = await sender.Send(new SendHumanInterventionCommand(id, request.Message, request.OperatorName));
        if (!result.Success)
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                409 => Results.Conflict(result),
                _ => Results.BadRequest(result)
            };
        return Results.Ok(result);
    }

    /// <summary>GET /api/incidents/{id}/interventions/pending — 获取待处理的干预请求列表</summary>
    private static Task<IResult> GetPendingInterventions(Guid id, IActiveIncidentTracker tracker)
    {
        var pending = tracker.GetPendingRequestsForIncident(id);
        return Task.FromResult(Results.Ok(pending));
    }

    /// <summary>POST /api/incidents/{id}/interventions/{requestId}/respond — 回复结构化干预请求</summary>
    private static async Task<IResult> RespondToIntervention(
        Guid id, string requestId, InterventionResponseRequest request, ISender sender)
    {
        var result = await sender.Send(new RespondToInterventionCommand(
            id, requestId, request.ResponseType,
            request.Content, request.Approved, request.OperatorName));
        if (!result.Success)
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                409 => Results.Conflict(result),
                400 => Results.BadRequest(result),
                _ => Results.BadRequest(result)
            };
        return Results.Ok(result);
    }

    private record UpdateStatusRequest(string NewStatus, string? Note = null);
    private record HumanInterventionRequest(string Message, string? OperatorName = null);
    private record InterventionResponseRequest(
        string ResponseType,
        string? Content = null,
        bool? Approved = null,
        string? OperatorName = null);

    /// <summary>POST /api/incidents/{id}/post-mortem — 提交 Post-mortem 标注</summary>
    private static async Task<IResult> AnnotatePostMortem(
        Guid id, PostMortemRequest body, ISender sender)
    {
        if (!Enum.TryParse<RcaAccuracyRating>(body.RcaAccuracy, true, out var rating))
            return Results.BadRequest(new { success = false, message = "Invalid RcaAccuracy value." });

        SopEffectivenessRating? sopRating = null;
        if (body.SopEffectiveness is not null)
        {
            if (!Enum.TryParse<SopEffectivenessRating>(body.SopEffectiveness, true, out var sr))
                return Results.BadRequest(new { success = false, message = "Invalid SopEffectiveness value." });
            sopRating = sr;
        }

        var result = await sender.Send(new AnnotatePostMortemCommand(
            id, body.ActualRootCause, rating, body.AnnotatedBy, sopRating, body.ImprovementNotes));

        if (!result.Success)
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                _ => Results.BadRequest(result)
            };
        return Results.Ok(result);
    }

    /// <summary>POST /api/incidents/{id}/steps/{stepNumber}/retry — 重试失败的 SOP 步骤</summary>
    private static async Task<IResult> RetryStepExecution(
        Guid id, int stepNumber, ISender sender)
    {
        var result = await sender.Send(
            new Application.Incidents.Commands.RetryStepExecution.RetryStepExecutionCommand(id, stepNumber));
        if (!result.Success)
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                _ => Results.BadRequest(result)
            };
        return Results.Ok(result);
    }

    /// <summary>POST /api/incidents/{id}/fallback-rca — SOP 执行失败降级到 RCA（Spec 025）</summary>
    private static async Task<IResult> FallbackToRca(
        Guid id, FallbackRequest body, ISender sender)
    {
        var result = await sender.Send(
            new Application.Incidents.Commands.FallbackToRca.FallbackToRcaCommand(id, body.Reason));
        if (!result.Success)
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                _ => Results.BadRequest(result)
            };
        return Results.Ok(result);
    }

    private record FallbackRequest(string Reason);

    private record PostMortemRequest(
        string ActualRootCause,
        string RcaAccuracy,
        string AnnotatedBy,
        string? SopEffectiveness = null,
        string? ImprovementNotes = null);
}
