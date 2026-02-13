using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Application.Sandboxes.Commands.CreateSandbox;
using CoreSRE.Application.Sandboxes.Commands.DeleteSandbox;
using CoreSRE.Application.Sandboxes.Commands.ExecSandbox;
using CoreSRE.Application.Sandboxes.Commands.StartSandbox;
using CoreSRE.Application.Sandboxes.Commands.StopSandbox;
using CoreSRE.Application.Sandboxes.Commands.UpdateSandbox;
using CoreSRE.Application.Sandboxes.Queries.GetSandboxById;
using CoreSRE.Application.Sandboxes.Queries.GetSandboxes;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Endpoints;

/// <summary>
/// 沙箱管理 REST API 端点
/// </summary>
public static class SandboxEndpoints
{
    public static IEndpointRouteBuilder MapSandboxEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sandboxes")
            .WithTags("Sandboxes")
            .WithOpenApi();

        group.MapPost("/", CreateSandbox);
        group.MapGet("/", GetSandboxes);
        group.MapGet("/{id:guid}", GetSandboxById);
        group.MapPut("/{id:guid}", UpdateSandbox);
        group.MapDelete("/{id:guid}", DeleteSandbox);
        group.MapPost("/{id:guid}/start", StartSandbox);
        group.MapPost("/{id:guid}/stop", StopSandbox);
        group.MapPost("/{id:guid}/exec", ExecSandbox);

        // WebSocket 交互式终端
        group.Map("/{id:guid}/terminal", SandboxTerminalHandler.HandleAsync)
            .ExcludeFromDescription(); // WebSocket 端点不走 OpenAPI

        return app;
    }

    private static async Task<IResult> CreateSandbox(
        CreateSandboxCommand command, ISender sender)
    {
        var result = await sender.Send(command);
        if (!result.Success) return Results.BadRequest(result);
        return Results.Created($"/api/sandboxes/{result.Data!.Id}", result);
    }

    private static async Task<IResult> GetSandboxes(
        ISender sender,
        string? status = null,
        Guid? agentId = null,
        string? search = null,
        int page = 1,
        int pageSize = 20)
    {
        SandboxStatus? parsedStatus = null;
        if (status is not null && !Enum.TryParse(status, true, out SandboxStatus s))
            return Results.BadRequest(new { success = false, message = "Invalid status." });
        else if (status is not null)
            parsedStatus = Enum.Parse<SandboxStatus>(status, true);

        var result = await sender.Send(new GetSandboxesQuery(parsedStatus, agentId, search, page, pageSize));
        return Results.Ok(result);
    }

    private static async Task<IResult> GetSandboxById(Guid id, ISender sender)
    {
        var result = await sender.Send(new GetSandboxByIdQuery(id));
        return result.Success ? Results.Ok(result) : Results.NotFound(result);
    }

    private static async Task<IResult> UpdateSandbox(
        Guid id, UpdateSandboxCommand command, ISender sender)
    {
        var result = await sender.Send(command with { Id = id });
        if (!result.Success)
            return result.ErrorCode == 404 ? Results.NotFound(result) : Results.BadRequest(result);
        return Results.Ok(result);
    }

    private static async Task<IResult> DeleteSandbox(Guid id, ISender sender)
    {
        var result = await sender.Send(new DeleteSandboxCommand(id));
        if (!result.Success) return Results.NotFound(result);
        return Results.NoContent();
    }

    private static async Task<IResult> StartSandbox(Guid id, ISender sender)
    {
        var result = await sender.Send(new StartSandboxCommand(id));
        if (!result.Success)
            return result.ErrorCode == 404 ? Results.NotFound(result) : Results.BadRequest(result);
        return Results.Ok(result);
    }

    private static async Task<IResult> StopSandbox(Guid id, ISender sender)
    {
        var result = await sender.Send(new StopSandboxCommand(id));
        if (!result.Success)
            return result.ErrorCode == 404 ? Results.NotFound(result) : Results.BadRequest(result);
        return Results.Ok(result);
    }

    private static async Task<IResult> ExecSandbox(Guid id, ExecSandboxCommand command, ISender sender)
    {
        var result = await sender.Send(command with { Id = id });
        if (!result.Success)
            return result.ErrorCode == 404 ? Results.NotFound(result) : Results.BadRequest(result);
        return Results.Ok(result);
    }
}
