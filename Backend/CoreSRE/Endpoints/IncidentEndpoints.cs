using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Incidents.Commands.UpdateIncidentStatus;
using CoreSRE.Application.Incidents.DTOs;
using CoreSRE.Application.Incidents.Queries.GetIncidentById;
using CoreSRE.Application.Incidents.Queries.ListIncidents;
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
        group.MapPatch("/{id:guid}/status", UpdateIncidentStatus);

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

    private record UpdateStatusRequest(string NewStatus, string? Note = null);
}
