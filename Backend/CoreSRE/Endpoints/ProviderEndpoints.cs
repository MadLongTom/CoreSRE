using CoreSRE.Application.Providers.Commands.DiscoverModels;
using CoreSRE.Application.Providers.Commands.RegisterProvider;
using CoreSRE.Application.Providers.Commands.UpdateProvider;
using CoreSRE.Application.Providers.Commands.DeleteProvider;
using CoreSRE.Application.Providers.Queries.GetProviderById;
using CoreSRE.Application.Providers.Queries.GetProviderModels;
using CoreSRE.Application.Providers.Queries.GetProviders;
using MediatR;

namespace CoreSRE.Endpoints;

/// <summary>
/// LLM Provider 管理端点
/// </summary>
public static class ProviderEndpoints
{
    public static IEndpointRouteBuilder MapProviderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/providers")
            .WithTags("Providers");

        group.MapPost("/", RegisterProvider);
        group.MapGet("/", GetProviders);
        group.MapGet("/{id:guid}", GetProviderById);
        group.MapPut("/{id:guid}", UpdateProvider);
        group.MapDelete("/{id:guid}", DeleteProvider);
        group.MapPost("/{id:guid}/discover", DiscoverModels);
        group.MapGet("/{id:guid}/models", GetProviderModels);

        return app;
    }

    /// <summary>POST /api/providers — 注册新 Provider</summary>
    private static async Task<IResult> RegisterProvider(
        RegisterProviderCommand command,
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

        return Results.Created($"/api/providers/{result.Data!.Id}", result);
    }

    /// <summary>GET /api/providers — 查询 Provider 列表</summary>
    private static async Task<IResult> GetProviders(ISender sender)
    {
        var result = await sender.Send(new GetProvidersQuery());
        return Results.Ok(result);
    }

    /// <summary>GET /api/providers/{id} — 获取 Provider 详情</summary>
    private static async Task<IResult> GetProviderById(
        Guid id,
        ISender sender)
    {
        var result = await sender.Send(new GetProviderByIdQuery(id));

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

    /// <summary>PUT /api/providers/{id} — 更新 Provider</summary>
    private static async Task<IResult> UpdateProvider(
        Guid id,
        UpdateProviderCommand command,
        ISender sender)
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

    /// <summary>DELETE /api/providers/{id} — 删除 Provider</summary>
    private static async Task<IResult> DeleteProvider(
        Guid id,
        ISender sender)
    {
        var result = await sender.Send(new DeleteProviderCommand(id));

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

    /// <summary>POST /api/providers/{id}/discover — 触发模型发现</summary>
    private static async Task<IResult> DiscoverModels(
        Guid id,
        ISender sender)
    {
        var result = await sender.Send(new DiscoverModelsCommand(id));

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                502 => Results.Json(result, statusCode: 502),
                _ => Results.BadRequest(result)
            };
        }

        return Results.Ok(result);
    }

    /// <summary>GET /api/providers/{id}/models — 获取 Provider 的模型列表</summary>
    private static async Task<IResult> GetProviderModels(
        Guid id,
        ISender sender)
    {
        var result = await sender.Send(new GetProviderModelsQuery(id));

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
}
