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
/// 支持 ChatClient Agent（通过 IChatClient 流式调用）和 A2A Agent（通过 AIAgent.RunStreamingAsync）。
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
            // 动态解析 Agent（ChatClient 或 A2A 类型均返回 AIAgent）
            var aiAgent = await agentResolver.ResolveAsync(agentId, threadId, cancellationToken);
            var chatClient = aiAgent.GetService<IChatClient>();

            if (chatClient is not null)
            {
                // ===== ChatClient Agent 路径 =====
                await HandleChatClientStreamAsync(context, aiAgent, chatClient, input, threadId, runId, agentId, contextFactory, cancellationToken);
            }
            else
            {
                // ===== A2A Agent 路径（通过 AIAgent.RunStreamingAsync）=====
                await HandleA2AStreamAsync(context, aiAgent, input, threadId, runId, agentId, contextFactory, cancellationToken);
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

    /// <summary>ChatClient Agent 流式处理（通过 IChatClient）</summary>
    private static async Task HandleChatClientStreamAsync(
        HttpContext context,
        AIAgent aiAgent,
        IChatClient chatClient,
        AgentChatInput input,
        string threadId,
        string runId,
        Guid agentId,
        IDbContextFactory<AppDbContext> contextFactory,
        CancellationToken cancellationToken)
    {
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
                    else if (content is FunctionCallContent functionCall)
                    {
                        // TOOL_CALL_START — LLM has decided to call a tool
                        var toolCallId = functionCall.CallId ?? Guid.NewGuid().ToString();
                        await WriteToolCallStartAsync(context.Response, toolCallId, functionCall.Name ?? "unknown", messageId, cancellationToken);

                        // TOOL_CALL_ARGS — serialize arguments
                        if (functionCall.Arguments is { Count: > 0 })
                        {
                            var argsJson = JsonSerializer.Serialize(functionCall.Arguments, s_jsonOptions);
                            await WriteToolCallArgsAsync(context.Response, toolCallId, argsJson, cancellationToken);
                        }
                        else
                        {
                            await WriteToolCallArgsAsync(context.Response, toolCallId, "{}", cancellationToken);
                        }
                    }
                    else if (content is FunctionResultContent functionResult)
                    {
                        // TOOL_CALL_END — tool execution completed
                        var toolCallId = functionResult.CallId ?? Guid.NewGuid().ToString();
                        await WriteToolCallEndAsync(context.Response, toolCallId, cancellationToken);
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
            await PersistChatHistoryAsync(context, aiAgent, input, responseBuilder.ToString(), threadId, agentId, contextFactory, cancellationToken);
    }

    /// <summary>A2A Agent 流式处理（通过 AIAgent.RunStreamingAsync）</summary>
    private static async Task HandleA2AStreamAsync(
        HttpContext context,
        AIAgent aiAgent,
        AgentChatInput input,
        string threadId,
        string runId,
        Guid agentId,
        IDbContextFactory<AppDbContext> contextFactory,
        CancellationToken cancellationToken)
    {
        // 转换 AG-UI 消息为 ChatMessage
        var chatMessages = input.Messages?.Select(m =>
            new ChatMessage(MapRole(m.Role), m.Content ?? string.Empty)
        ).ToList() ?? [];

        // 发送 RUN_STARTED
        await WriteSseEventAsync(context.Response, new
        {
            type = "RUN_STARTED",
            threadId,
            runId
        }, cancellationToken);

        var messageId = Guid.NewGuid().ToString();

        // 发送 TEXT_MESSAGE_START
        await WriteSseEventAsync(context.Response, new
        {
            type = "TEXT_MESSAGE_START",
            messageId,
            role = "assistant"
        }, cancellationToken);

        // 通过 AIAgent.RunStreamingAsync 调用 A2A 远程代理
        var responseBuilder = new System.Text.StringBuilder();
        await foreach (var update in aiAgent.RunStreamingAsync(chatMessages, cancellationToken: cancellationToken))
        {
            var text = update.Text;
            if (!string.IsNullOrEmpty(text))
            {
                responseBuilder.Append(text);
                await WriteSseEventAsync(context.Response, new
                {
                    type = "TEXT_MESSAGE_CONTENT",
                    messageId,
                    delta = text
                }, cancellationToken);
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

        // 持久化
        await PersistChatHistoryAsync(context, aiAgent, input, responseBuilder.ToString(), threadId, agentId, contextFactory, cancellationToken);
    }

    /// <summary>持久化消息历史到 AgentSessionRecord.SessionData</summary>
    private static async Task PersistChatHistoryAsync(
        HttpContext context,
        AIAgent aiAgent,
        AgentChatInput input,
        string assistantResponse,
        string threadId,
        Guid agentId,
        IDbContextFactory<AppDbContext> contextFactory,
        CancellationToken cancellationToken)
    {
            try
            {
                // 构建消息数组（不含 system 指令，只保存 user + assistant）
                var persistMessages = new List<object>();
                foreach (var m in input.Messages ?? [])
                {
                    persistMessages.Add(new { role = m.Role ?? "user", contents = new object[] { new { text = m.Content ?? "", type = "text" } } });
                }
                persistMessages.Add(new { role = "assistant", contents = new object[] { new { text = assistantResponse, type = "text" } } });

                var sessionData = JsonSerializer.SerializeToElement(new
                {
                    chatHistoryProviderState = new { messages = persistMessages }
                }, s_jsonOptions);

                var agentName = aiAgent.GetService<ChatClientAgentOptions>()?.Name
                    ?? aiAgent.Name
                    ?? aiAgent.GetService<AIAgentMetadata>()?.ProviderName
                    ?? agentId.ToString();

                await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
                var now = DateTime.UtcNow;
                var sessionType = aiAgent.GetService<IChatClient>() is not null ? "ChatClientAgentSession" : "A2AAgentSession";
                await db.Database.ExecuteSqlAsync(
                    $"""
                    INSERT INTO agent_sessions (agent_id, conversation_id, session_data, session_type, created_at, updated_at)
                    VALUES ({agentName}, {threadId}, {sessionData.GetRawText()}::jsonb, {sessionType}, {now}, {now})
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

    private static async Task WriteSseEventAsync(HttpResponse response, object eventData, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(eventData, s_jsonOptions);
        await response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private static async Task WriteToolCallStartAsync(HttpResponse response, string toolCallId, string toolCallName, string parentMessageId, CancellationToken cancellationToken)
    {
        await WriteSseEventAsync(response, new
        {
            type = "TOOL_CALL_START",
            toolCallId,
            toolCallName,
            parentMessageId
        }, cancellationToken);
    }

    private static async Task WriteToolCallArgsAsync(HttpResponse response, string toolCallId, string delta, CancellationToken cancellationToken)
    {
        await WriteSseEventAsync(response, new
        {
            type = "TOOL_CALL_ARGS",
            toolCallId,
            delta
        }, cancellationToken);
    }

    private static async Task WriteToolCallEndAsync(HttpResponse response, string toolCallId, CancellationToken cancellationToken)
    {
        await WriteSseEventAsync(response, new
        {
            type = "TOOL_CALL_END",
            toolCallId
        }, cancellationToken);
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

