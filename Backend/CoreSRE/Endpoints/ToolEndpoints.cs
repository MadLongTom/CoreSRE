using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Tools.Commands.DeleteTool;
using CoreSRE.Application.Tools.Commands.ImportOpenApi;
using CoreSRE.Application.Tools.Commands.InvokeTool;
using CoreSRE.Application.Tools.Commands.RegisterTool;
using CoreSRE.Application.Tools.Commands.UpdateTool;
using CoreSRE.Application.Tools.Queries.GetMcpTools;
using CoreSRE.Application.Tools.Queries.GetToolById;
using CoreSRE.Application.Tools.Queries.GetTools;
using CoreSRE.Domain.Enums;
using MediatR;

namespace CoreSRE.Endpoints;

/// <summary>
/// Tool Gateway 工具注册与管理端点
/// </summary>
public static class ToolEndpoints
{
    public static IEndpointRouteBuilder MapToolEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tools")
            .WithTags("Tools")
            .WithOpenApi();

        group.MapPost("/", RegisterTool);
        group.MapGet("/", GetTools);
        group.MapGet("/{id:guid}", GetToolById);
        group.MapPut("/{id:guid}", UpdateTool);
        group.MapDelete("/{id:guid}", DeleteTool);
        group.MapGet("/{id:guid}/mcp-tools", GetMcpTools);
        group.MapPost("/import-openapi", ImportOpenApi).DisableAntiforgery();
        group.MapPost("/{id:guid}/invoke", InvokeTool);

        return app;
    }

    /// <summary>POST /api/tools — 注册新工具</summary>
    private static async Task<IResult> RegisterTool(
        RegisterToolCommand command,
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

        return Results.Created($"/api/tools/{result.Data!.Id}", result);
    }

    /// <summary>GET /api/tools — 查询工具列表（支持分页、类型/状态过滤、关键词搜索）</summary>
    private static async Task<IResult> GetTools(
        ISender sender,
        string? toolType = null,
        string? status = null,
        string? search = null,
        int page = 1,
        int pageSize = 20)
    {
        ToolType? parsedType = null;
        if (toolType is not null)
        {
            if (!Enum.TryParse<ToolType>(toolType, ignoreCase: true, out var t))
                return Results.BadRequest(new { success = false, message = $"Invalid toolType. Must be one of: {string.Join(", ", Enum.GetNames<ToolType>())}." });
            parsedType = t;
        }

        ToolStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<ToolStatus>(status, ignoreCase: true, out var s))
                return Results.BadRequest(new { success = false, message = $"Invalid status. Must be one of: {string.Join(", ", Enum.GetNames<ToolStatus>())}." });
            parsedStatus = s;
        }

        var result = await sender.Send(new GetToolsQuery(parsedType, parsedStatus, search, page, pageSize));
        return Results.Ok(result);
    }

    /// <summary>GET /api/tools/{id} — 获取工具详情</summary>
    private static async Task<IResult> GetToolById(
        Guid id,
        ISender sender)
    {
        var result = await sender.Send(new GetToolByIdQuery(id));

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

    /// <summary>PUT /api/tools/{id} — 更新工具配置</summary>
    private static async Task<IResult> UpdateTool(
        Guid id,
        UpdateToolCommand command,
        ISender sender)
    {
        // Ensure the ID from the route matches the command
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

    /// <summary>DELETE /api/tools/{id} — 删除工具（级联删除 McpToolItems）</summary>
    private static async Task<IResult> DeleteTool(
        Guid id,
        ISender sender)
    {
        var result = await sender.Send(new DeleteToolCommand(id));

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

    /// <summary>GET /api/tools/{id}/mcp-tools — 查询 MCP Server 下的子工具项列表</summary>
    private static async Task<IResult> GetMcpTools(
        Guid id,
        ISender sender)
    {
        var result = await sender.Send(new GetMcpToolsQuery(id));

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

    /// <summary>POST /api/tools/import-openapi — 通过 OpenAPI 文档批量导入工具</summary>
    private static async Task<IResult> ImportOpenApi(
        IFormFile file,
        ISender sender,
        string? baseUrl = null)
    {
        if (file is null || file.Length == 0)
            return Results.BadRequest(new { success = false, message = "File is required." });

        // 10MB limit
        if (file.Length > 10 * 1024 * 1024)
            return Results.BadRequest(new { success = false, message = "File size must not exceed 10MB." });

        using var stream = file.OpenReadStream();
        var command = new ImportOpenApiCommand
        {
            Document = stream,
            BaseUrl = baseUrl,
            ImportSource = file.FileName
        };

        var result = await sender.Send(command);

        if (!result.Success)
        {
            return Results.BadRequest(result);
        }

        return Results.Ok(result);
    }
    /// <summary>POST /api/tools/{id}/invoke — 统一工具调用</summary>
    private static async Task<IResult> InvokeTool(
        Guid id,
        InvokeToolCommand command,
        ISender sender)
    {
        var commandWithId = command with { ToolRegistrationId = id };
        var result = await sender.Send(commandWithId);

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                502 => Results.Json(result, statusCode: 502),
                503 => Results.Json(result, statusCode: 503),
                _ => Results.BadRequest(result)
            };
        }

        return Results.Ok(result);
    }
}
