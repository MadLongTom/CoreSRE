using CoreSRE.Application.Chat.Commands.CreateConversation;
using CoreSRE.Application.Chat.Commands.DeleteConversation;
using CoreSRE.Application.Chat.Commands.TouchConversation;
using CoreSRE.Application.Chat.Queries.GetConversationById;
using CoreSRE.Application.Chat.Queries.GetConversations;
using MediatR;

namespace CoreSRE.Endpoints;

/// <summary>
/// 对话 CRUD 端点（REST）
/// </summary>
public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/chat/conversations")
            .WithTags("Chat");

        group.MapPost("/", CreateConversation);
        group.MapGet("/", GetConversations);
        group.MapGet("/{id:guid}", GetConversationById);
        group.MapPost("/{id:guid}/touch", TouchConversation);
        group.MapDelete("/{id:guid}", DeleteConversation);

        return app;
    }

    /// <summary>POST /api/chat/conversations — 创建新对话</summary>
    private static async Task<IResult> CreateConversation(
        CreateConversationCommand command,
        ISender sender)
    {
        var result = await sender.Send(command);

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                _ => Results.BadRequest(result)
            };
        }

        return Results.Created($"/api/chat/conversations/{result.Data!.Id}", result);
    }

    /// <summary>GET /api/chat/conversations — 查询对话列表</summary>
    private static async Task<IResult> GetConversations(ISender sender)
    {
        var result = await sender.Send(new GetConversationsQuery());
        return Results.Ok(result);
    }

    /// <summary>GET /api/chat/conversations/{id} — 查询对话详情（含消息历史）</summary>
    private static async Task<IResult> GetConversationById(
        Guid id,
        ISender sender)
    {
        var result = await sender.Send(new GetConversationByIdQuery(id));

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

    /// <summary>POST /api/chat/conversations/{id}/touch — 刷新对话时间戳和标题</summary>
    private static async Task<IResult> TouchConversation(
        Guid id,
        TouchConversationCommand command,
        ISender sender)
    {
        var commandWithId = command with { ConversationId = id };
        var result = await sender.Send(commandWithId);

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

    /// <summary>DELETE /api/chat/conversations/{id} — 删除对话及关联会话记录</summary>
    private static async Task<IResult> DeleteConversation(
        Guid id,
        ISender sender)
    {
        var result = await sender.Send(new DeleteConversationCommand(id));

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
