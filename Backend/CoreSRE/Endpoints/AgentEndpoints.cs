using CoreSRE.Application.Agents.Commands.DeleteAgent;
using CoreSRE.Application.Agents.Commands.RegisterAgent;
using CoreSRE.Application.Agents.Commands.UpdateAgent;
using CoreSRE.Application.Agents.Queries.GetAgentById;
using CoreSRE.Application.Agents.Queries.GetAgents;
using CoreSRE.Application.Agents.Queries.SearchAgents;
using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CoreSRE.Endpoints;

/// <summary>
/// Agent 注册与 CRUD 管理端点
/// </summary>
public static class AgentEndpoints
{
    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/agents")
            .WithTags("Agents");

        group.MapPost("/", RegisterAgent);
        group.MapGet("/", GetAgents);
        group.MapGet("/search", SearchAgents);
        group.MapGet("/{id:guid}", GetAgentById);
        group.MapPut("/{id:guid}", UpdateAgent);
        group.MapDelete("/{id:guid}", DeleteAgent);

        return app;
    }

    /// <summary>POST /api/agents — 注册新 Agent</summary>
    private static async Task<IResult> RegisterAgent(
        RegisterAgentCommand command,
        ISender sender)
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

        return Results.Created($"/api/agents/{result.Data!.Id}", result);
    }

    /// <summary>GET /api/agents?type= — 查询 Agent 列表（可按类型过滤）</summary>
    private static async Task<IResult> GetAgents(
        ISender sender,
        string? type = null)
    {
        AgentType? agentType = null;
        if (type is not null)
        {
            if (!Enum.TryParse<AgentType>(type, ignoreCase: true, out var parsed))
                return Results.BadRequest(new { success = false, message = $"Invalid type filter. Must be one of: {string.Join(", ", Enum.GetNames<AgentType>())}." });
            agentType = parsed;
        }

        var result = await sender.Send(new GetAgentsQuery(agentType));
        return Results.Ok(result);
    }

    /// <summary>GET /api/agents/{id} — 获取 Agent 详情</summary>
    private static async Task<IResult> GetAgentById(
        Guid id,
        ISender sender)
    {
        var result = await sender.Send(new GetAgentByIdQuery(id));

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

    /// <summary>PUT /api/agents/{id} — 更新 Agent 配置</summary>
    private static async Task<IResult> UpdateAgent(
        Guid id,
        UpdateAgentCommand command,
        ISender sender)
    {
        // Bind the route ID to the command
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

    /// <summary>DELETE /api/agents/{id} — 注销 Agent</summary>
    private static async Task<IResult> DeleteAgent(
        Guid id,
        ISender sender)
    {
        var result = await sender.Send(new DeleteAgentCommand(id));

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

    /// <summary>GET /api/agents/search?q= — 按技能关键词搜索 Agent</summary>
    private static async Task<IResult> SearchAgents(
        [FromQuery(Name = "q")] string? q,
        ISender sender)
    {
        // FluentValidation via MediatR pipeline handles empty/null/whitespace/too-long cases.
        // ValidationException is caught by ExceptionHandlingMiddleware → 400.
        var result = await sender.Send(new SearchAgentsQuery(q ?? string.Empty));
        return Results.Ok(result.Data);
    }
}
