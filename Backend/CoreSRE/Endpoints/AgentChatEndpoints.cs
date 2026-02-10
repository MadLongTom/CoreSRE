using CoreSRE.Application.Interfaces;
using CoreSRE.Infrastructure.Persistence;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace CoreSRE.Endpoints;

/// <summary>
/// AG-UI 协议流式端点 — 动态解析 Agent 并以 SSE 返回 AG-UI 事件流。
/// MapAGUI 要求静态 AIAgent 且内部类型均为 internal，无法满足按请求动态解析的需求，
/// 因此本端点直接使用 IChatClient 流式调用 + 手动输出 AG-UI SSE 事件。
/// 事件格式：data: {json}\n\n（AG-UI 客户端通过 JSON 中的 type 字段区分事件类型）。
/// </summary>
public static class AgentChatEndpoints
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static IEndpointRouteBuilder MapAgentChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/chat/stream", HandleAgentChat)
            .WithTags("Chat");

        return app;
    }

    private static async Task HandleAgentChat(
        [FromBody] AgentChatInput input,
        HttpContext context,
        IAgentResolver agentResolver,
        IDbContextFactory<AppDbContext> contextFactory,
        CancellationToken cancellationToken)
    {
        // 从 AG-UI context 提取 agentId（前端通过 context 传递）
        var agentIdStr = input.Context?
            .FirstOrDefault(c => c.Description == "agentId")?.Value;

        if (string.IsNullOrEmpty(agentIdStr) || !Guid.TryParse(agentIdStr, out var agentId))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { message = "Missing or invalid agentId in context." }, cancellationToken);
            return;
        }

        var threadId = input.ThreadId ?? Guid.NewGuid().ToString();
        var runId = input.RunId ?? Guid.NewGuid().ToString();

        // 设置 SSE 响应头
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache,no-store";
        context.Response.Headers.Pragma = "no-cache";

        try
        {
            // 动态解析 Agent → 获取底层 IChatClient
            var aiAgent = await agentResolver.ResolveAsync(agentId, threadId, cancellationToken);
            var chatClient = aiAgent.GetService<IChatClient>()
                ?? throw new InvalidOperationException("Resolved agent does not expose an IChatClient.");

            // 转换 AG-UI 消息为 ChatMessage
            var chatMessages = input.Messages?.Select(m =>
                new ChatMessage(MapRole(m.Role), m.Content ?? string.Empty)
            ).ToList() ?? [];

            // 添加系统指令（如果 Agent 有配置）
            var chatOptions = aiAgent.GetService<ChatOptions>();
            if (chatOptions?.Instructions is not null)
            {
                chatMessages.Insert(0, new ChatMessage(ChatRole.System, chatOptions.Instructions));
            }

            // 发送 RUN_STARTED
            await WriteSseEventAsync(context.Response, new
            {
                type = "RUN_STARTED",
                threadId,
                runId
            }, cancellationToken);

            // 生成消息 ID
            var messageId = Guid.NewGuid().ToString();

            // 发送 TEXT_MESSAGE_START
            await WriteSseEventAsync(context.Response, new
            {
                type = "TEXT_MESSAGE_START",
                messageId,
                role = "assistant"
            }, cancellationToken);

            // 流式调用 LLM
            var responseBuilder = new System.Text.StringBuilder();
            await foreach (var update in chatClient.GetStreamingResponseAsync(chatMessages, chatOptions, cancellationToken))
            {
                foreach (var content in update.Contents)
                {
                    if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                    {
                        responseBuilder.Append(textContent.Text);
                        await WriteSseEventAsync(context.Response, new
                        {
                            type = "TEXT_MESSAGE_CONTENT",
                            messageId,
                            delta = textContent.Text
                        }, cancellationToken);
                    }
                }
            }

            // 发送 TEXT_MESSAGE_END
            await WriteSseEventAsync(context.Response, new
            {
                type = "TEXT_MESSAGE_END",
                messageId
            }, cancellationToken);

            // 发送 RUN_FINISHED
            await WriteSseEventAsync(context.Response, new
            {
                type = "RUN_FINISHED",
                threadId,
                runId
            }, cancellationToken);

            // 持久化消息历史到 AgentSessionRecord.SessionData
            // 格式与 InMemoryChatHistoryProvider.Serialize() 一致，以便 US2 读取
            try
            {
                // 构建消息数组（不含 system 指令，只保存 user + assistant）
                var persistMessages = new List<object>();
                foreach (var m in input.Messages ?? [])
                {
                    persistMessages.Add(new { role = m.Role ?? "user", contents = new object[] { new { text = m.Content ?? "", type = "text" } } });
                }
                persistMessages.Add(new { role = "assistant", contents = new object[] { new { text = responseBuilder.ToString(), type = "text" } } });

                var sessionData = JsonSerializer.SerializeToElement(new
                {
                    chatHistoryProviderState = new { messages = persistMessages }
                }, s_jsonOptions);

                var agentName = aiAgent.GetService<ChatClientAgentOptions>()?.Name
                    ?? aiAgent.GetService<AIAgentMetadata>()?.ProviderName
                    ?? agentId.ToString();

                await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
                var now = DateTime.UtcNow;
                await db.Database.ExecuteSqlAsync(
                    $"""
                    INSERT INTO agent_sessions (agent_id, conversation_id, session_data, session_type, created_at, updated_at)
                    VALUES ({agentName}, {threadId}, {sessionData.GetRawText()}::jsonb, {"ChatClientAgentSession"}, {now}, {now})
                    ON CONFLICT (agent_id, conversation_id)
                    DO UPDATE SET
                        session_data = EXCLUDED.session_data,
                        session_type = EXCLUDED.session_type,
                        updated_at = EXCLUDED.updated_at
                    """,
                    cancellationToken);
            }
            catch (Exception persistEx)
            {
                // 消息持久化失败不影响用户体验，仅记录日志
                var logger = context.RequestServices.GetService<ILoggerFactory>()?
                    .CreateLogger("AgentChatEndpoints");
                logger?.LogWarning(persistEx, "Failed to persist chat messages for thread {ThreadId}", threadId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 发送 RUN_ERROR 事件
            await WriteSseEventAsync(context.Response, new
            {
                type = "RUN_ERROR",
                message = ex.Message,
                code = "StreamingError"
            }, cancellationToken);
        }

        await context.Response.Body.FlushAsync(cancellationToken);
    }

    private static async Task WriteSseEventAsync(HttpResponse response, object eventData, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(eventData, s_jsonOptions);
        await response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private static ChatRole MapRole(string? role) => role?.ToLowerInvariant() switch
    {
        "system" => ChatRole.System,
        "user" => ChatRole.User,
        "assistant" => ChatRole.Assistant,
        "tool" => ChatRole.Tool,
        _ => ChatRole.User
    };
}

/// <summary>
/// AG-UI 协议输入 DTO — 匹配 RunAgentInput 格式（该类型在 AG-UI 包中为 internal）
/// </summary>
public sealed class AgentChatInput
{
    public string? ThreadId { get; set; }
    public string? RunId { get; set; }
    public List<AgentChatMessage>? Messages { get; set; }
    public List<AgentChatContextItem>? Context { get; set; }
    public JsonElement? State { get; set; }
    public JsonElement? ForwardedProps { get; set; }
}

public sealed class AgentChatMessage
{
    public string? Role { get; set; }
    public string? Content { get; set; }
}

public sealed class AgentChatContextItem
{
    public string Description { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

