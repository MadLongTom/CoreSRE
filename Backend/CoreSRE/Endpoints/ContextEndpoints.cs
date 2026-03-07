using CoreSRE.Application.DataSources.Commands.PreviewContext;
using MediatR;

namespace CoreSRE.Endpoints;

/// <summary>
/// 上下文预览端点 — 用于调试和测试上下文初始化查询。
/// </summary>
public static class ContextEndpoints
{
    public static IEndpointRouteBuilder MapContextEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/context")
            .WithTags("Context")
            .WithOpenApi();

        group.MapPost("/preview", PreviewContext);

        return app;
    }

    /// <summary>POST /api/context/preview — 预览上下文初始化结果</summary>
    private static async Task<IResult> PreviewContext(
        PreviewContextCommand command,
        ISender sender)
    {
        var result = await sender.Send(command);
        return result.Success
            ? Results.Ok(result)
            : Results.BadRequest(result);
    }
}
