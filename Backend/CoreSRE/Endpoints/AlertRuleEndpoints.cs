using CoreSRE.Application.AlertRules.Commands.CreateAlertRule;
using CoreSRE.Application.AlertRules.Commands.DeleteAlertRule;
using CoreSRE.Application.AlertRules.Commands.UpdateAlertRule;
using CoreSRE.Application.AlertRules.DTOs;
using CoreSRE.Application.AlertRules.Queries.GetAlertRuleById;
using CoreSRE.Application.AlertRules.Queries.GetAlertRules;
using MediatR;

namespace CoreSRE.Endpoints;

/// <summary>
/// 告警路由规则 CRUD 端点
/// </summary>
public static class AlertRuleEndpoints
{
    public static IEndpointRouteBuilder MapAlertRuleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/alert-rules")
            .WithTags("AlertRules");

        group.MapPost("/", CreateAlertRule);
        group.MapGet("/", GetAlertRules);
        group.MapGet("/{id:guid}", GetAlertRuleById);
        group.MapPut("/{id:guid}", UpdateAlertRule);
        group.MapDelete("/{id:guid}", DeleteAlertRule);

        // ── 金丝雀验证 & 健康评分（Spec 025）──
        group.MapPost("/{id:guid}/canary/start", StartCanary);
        group.MapPost("/{id:guid}/canary/stop", StopCanary);
        group.MapGet("/{id:guid}/canary/report", GetCanaryReport);
        group.MapGet("/{id:guid}/health", GetAlertRuleHealth);

        return app;
    }

    /// <summary>POST /api/alert-rules — 创建告警路由规则</summary>
    private static async Task<IResult> CreateAlertRule(
        CreateAlertRuleRequest request,
        ISender sender)
    {
        var command = new CreateAlertRuleCommand
        {
            Name = request.Name,
            Description = request.Description,
            Matchers = request.Matchers,
            Severity = request.Severity,
            SopId = request.SopId,
            ResponderAgentId = request.ResponderAgentId,
            TeamAgentId = request.TeamAgentId,
            SummarizerAgentId = request.SummarizerAgentId,
            NotificationChannels = request.NotificationChannels,
            CooldownMinutes = request.CooldownMinutes,
            Tags = request.Tags
        };

        var result = await sender.Send(command);

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                409 => Results.Conflict(result),
                _ => Results.BadRequest(result)
            };
        }

        return Results.Created($"/api/alert-rules/{result.Data!.Id}", result);
    }

    /// <summary>GET /api/alert-rules?status=&amp;severity= — 查询告警规则列表</summary>
    private static async Task<IResult> GetAlertRules(
        ISender sender,
        string? status = null,
        string? severity = null)
    {
        var result = await sender.Send(new GetAlertRulesQuery(status, severity));
        return Results.Ok(result);
    }

    /// <summary>GET /api/alert-rules/{id} — 获取告警规则详情</summary>
    private static async Task<IResult> GetAlertRuleById(
        Guid id,
        ISender sender)
    {
        var result = await sender.Send(new GetAlertRuleByIdQuery(id));

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                _ => Results.BadRequest(result)
            };
        }

        return Results.Ok(result);
    }

    /// <summary>PUT /api/alert-rules/{id} — 更新告警路由规则</summary>
    private static async Task<IResult> UpdateAlertRule(
        Guid id,
        UpdateAlertRuleRequest request,
        ISender sender)
    {
        var command = new UpdateAlertRuleCommand
        {
            Id = id,
            Name = request.Name,
            Description = request.Description,
            Matchers = request.Matchers,
            Severity = request.Severity,
            Status = request.Status,
            SopId = request.SopId,
            ResponderAgentId = request.ResponderAgentId,
            TeamAgentId = request.TeamAgentId,
            SummarizerAgentId = request.SummarizerAgentId,
            NotificationChannels = request.NotificationChannels,
            CooldownMinutes = request.CooldownMinutes,
            Tags = request.Tags
        };

        var result = await sender.Send(command);

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                409 => Results.Conflict(result),
                _ => Results.BadRequest(result)
            };
        }

        return Results.Ok(result);
    }

    /// <summary>DELETE /api/alert-rules/{id} — 删除告警路由规则</summary>
    private static async Task<IResult> DeleteAlertRule(
        Guid id,
        ISender sender)
    {
        var result = await sender.Send(new DeleteAlertRuleCommand(id));

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                409 => Results.Conflict(result),
                _ => Results.BadRequest(result)
            };
        }

        return Results.NoContent();
    }

    // ── 金丝雀验证 & 健康评分（Spec 025）──

    /// <summary>POST /api/alert-rules/{id}/canary/start — 启动金丝雀模式</summary>
    private static async Task<IResult> StartCanary(
        Guid id, StartCanaryRequest body, ISender sender)
    {
        var result = await sender.Send(
            new Application.Alerts.Commands.StartCanary.StartCanaryCommand(id, body.CanarySopId));
        if (!result.Success)
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                _ => Results.BadRequest(result)
            };
        return Results.Ok(result);
    }

    /// <summary>POST /api/alert-rules/{id}/canary/stop — 停止金丝雀模式</summary>
    private static async Task<IResult> StopCanary(Guid id, ISender sender)
    {
        var result = await sender.Send(
            new Application.Alerts.Commands.StopCanary.StopCanaryCommand(id));
        if (!result.Success)
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                _ => Results.BadRequest(result)
            };
        return Results.Ok(result);
    }

    /// <summary>GET /api/alert-rules/{id}/canary/report — 获取金丝雀报告</summary>
    private static async Task<IResult> GetCanaryReport(Guid id, ISender sender)
    {
        var result = await sender.Send(
            new Application.Alerts.Queries.GetCanaryReport.GetCanaryReportQuery(id));
        if (!result.Success)
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                _ => Results.BadRequest(result)
            };
        return Results.Ok(result);
    }

    /// <summary>GET /api/alert-rules/{id}/health — 获取健康评分</summary>
    private static async Task<IResult> GetAlertRuleHealth(Guid id, ISender sender)
    {
        var result = await sender.Send(
            new Application.Alerts.Queries.GetAlertRuleHealth.GetAlertRuleHealthQuery(id));
        if (!result.Success)
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                _ => Results.BadRequest(result)
            };
        return Results.Ok(result);
    }

    private record StartCanaryRequest(Guid CanarySopId);
}
