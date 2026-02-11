using CoreSRE.Application.Workflows.Commands.CreateWorkflow;
using CoreSRE.Application.Workflows.Commands.DeleteWorkflow;
using CoreSRE.Application.Workflows.Commands.UpdateWorkflow;
using CoreSRE.Application.Workflows.Queries.GetWorkflowById;
using CoreSRE.Application.Workflows.Queries.GetWorkflows;
using CoreSRE.Domain.Enums;
using MediatR;

namespace CoreSRE.Endpoints;

public static class WorkflowEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workflows")
            .WithTags("Workflows")
            .WithOpenApi();

        group.MapPost("/", CreateWorkflow);
        group.MapGet("/", GetWorkflows);
        group.MapGet("/{id:guid}", GetWorkflowById);
        group.MapPut("/{id:guid}", UpdateWorkflow);
        group.MapDelete("/{id:guid}", DeleteWorkflow);

        return app;
    }

    private static async Task<IResult> CreateWorkflow(
        CreateWorkflowCommand command, ISender sender)
    {
        var result = await sender.Send(command);
        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                409 => Results.Conflict(result),
                _ => Results.BadRequest(result)
            };
        }
        return Results.Created($"/api/workflows/{result.Data!.Id}", result);
    }

    private static async Task<IResult> GetWorkflows(
        ISender sender, string? status = null)
    {
        WorkflowStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<WorkflowStatus>(status, ignoreCase: true, out var s))
                return Results.BadRequest(new { success = false, message = $"Invalid status: {status}. Valid values: {string.Join(", ", Enum.GetNames<WorkflowStatus>())}" });
            parsedStatus = s;
        }

        var result = await sender.Send(new GetWorkflowsQuery(parsedStatus));
        return Results.Ok(result);
    }

    private static async Task<IResult> GetWorkflowById(Guid id, ISender sender)
    {
        var result = await sender.Send(new GetWorkflowByIdQuery(id));
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

    private static async Task<IResult> UpdateWorkflow(
        Guid id, UpdateWorkflowCommand command, ISender sender)
    {
        var commandWithId = command with { Id = id };
        var result = await sender.Send(commandWithId);
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

    private static async Task<IResult> DeleteWorkflow(Guid id, ISender sender)
    {
        var result = await sender.Send(new DeleteWorkflowCommand(id));
        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                _ => Results.BadRequest(result)
            };
        }
        return Results.NoContent();
    }
}
