using CoreSRE.Application.Interfaces;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Mvc;
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
        AgentSessionStore sessionStore,
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
            // 动态解析 Agent（ChatClient 或 A2A 类型均返回 ResolvedAgent）
            var resolved = await agentResolver.ResolveAsync(agentId, threadId, cancellationToken);
            var aiAgent = resolved.Agent;
            var chatClient = aiAgent.GetService<IChatClient>();
            var enableHistory = resolved.LlmConfig?.EnableChatHistory ?? true;

            if (chatClient is not null)
            {
                if (enableHistory)
                {
                    // ===== ChatClient Agent — 框架管理历史模式 =====
                    await HandleChatClientWithHistoryAsync(context, aiAgent, input, threadId, runId, sessionStore, cancellationToken);
                }
                else
                {
                    // ===== ChatClient Agent — 无状态回退模式 =====
                    await HandleChatClientStatelessAsync(context, aiAgent, chatClient, input, threadId, runId, cancellationToken);
                }
            }
            else
            {
                // ===== A2A Agent 路径（通过 AIAgent.RunStreamingAsync）=====
                await HandleA2AStreamAsync(context, aiAgent, input, threadId, runId, cancellationToken);
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

    /// <summary>ChatClient Agent — 框架管理历史模式（SessionStore 直接管理）</summary>
    private static async Task HandleChatClientWithHistoryAsync(
        HttpContext context,
        AIAgent aiAgent,
        AgentChatInput input,
        string threadId,
        string runId,
        AgentSessionStore sessionStore,
        CancellationToken cancellationToken)
    {
        var logger = context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("AgentChatEndpoints");

        logger?.LogInformation(
            "[SessionDebug] HandleChatClientWithHistoryAsync START — threadId={ThreadId}, input.ThreadId={InputThreadId}, agent.Id={AgentId}, agent.Name={AgentName}",
            threadId, input.ThreadId, aiAgent.Id, aiAgent.Name);

        // 1. Load or create session via sessionStore directly (not AIHostAgent)
        //    AIHostAgent generates its own internal conversationId, ignoring our threadId.
        //    Calling sessionStore directly ensures threadId == conversation_id in DB.
        AgentSession? session = null;
        bool sessionLoadedFromStore = false;
        try
        {
            session = await sessionStore.GetSessionAsync(aiAgent, threadId, cancellationToken);
            sessionLoadedFromStore = true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[SessionDebug] Failed to load session for thread {ThreadId}, creating fresh session", threadId);
            session = await aiAgent.CreateSessionAsync(cancellationToken);
        }

        // Log session state after load
        var historyProvider = session?.GetService(typeof(ChatHistoryProvider)) as IReadOnlyList<ChatMessage>;
        logger?.LogInformation(
            "[SessionDebug] Session loaded — fromStore={FromStore}, sessionType={SessionType}, historyCount={HistoryCount}",
            sessionLoadedFromStore, session?.GetType().Name, historyProvider?.Count ?? -1);

        // 3. Extract only the new user message from frontend payload
        //    (session store provides full history; frontend messages are redundant except for the latest)
        var lastMessage = input.Messages?.LastOrDefault();
        var newUserMessage = new ChatMessage(
            ChatRole.User,
            lastMessage?.Content ?? string.Empty);

        // 4. Send SSE events: RUN_STARTED
        await WriteSseEventAsync(context.Response, new
        {
            type = "RUN_STARTED",
            threadId,
            runId
        }, cancellationToken);

        var messageId = Guid.NewGuid().ToString();

        // TEXT_MESSAGE_START
        await WriteSseEventAsync(context.Response, new
        {
            type = "TEXT_MESSAGE_START",
            messageId,
            role = "assistant"
        }, cancellationToken);

        // 5. Stream via agent pipeline (ChatHistoryProvider auto-manages history)
        logger?.LogInformation("[SessionDebug] Starting RunStreamingAsync — historyCountBefore={Count}", historyProvider?.Count ?? -1);
        await foreach (var update in aiAgent.RunStreamingAsync(newUserMessage, session, cancellationToken: cancellationToken))
        {
            foreach (var content in update.Contents)
            {
                if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                {
                    await WriteSseEventAsync(context.Response, new
                    {
                        type = "TEXT_MESSAGE_CONTENT",
                        messageId,
                        delta = textContent.Text
                    }, cancellationToken);
                }
                else if (content is FunctionCallContent functionCall)
                {
                    var toolCallId = functionCall.CallId ?? Guid.NewGuid().ToString();
                    await WriteToolCallStartAsync(context.Response, toolCallId, functionCall.Name ?? "unknown", messageId, cancellationToken);

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
                    var toolCallId = functionResult.CallId ?? Guid.NewGuid().ToString();
                    var resultStr = functionResult.Result?.ToString();
                    await WriteToolCallEndAsync(context.Response, toolCallId, resultStr, cancellationToken);
                }
            }
        }

        // Re-check history count after streaming
        var historyAfter = session?.GetService(typeof(ChatHistoryProvider)) as IReadOnlyList<ChatMessage>;
        logger?.LogInformation("[SessionDebug] RunStreamingAsync done — historyCountAfter={Count}", historyAfter?.Count ?? -1);

        // TEXT_MESSAGE_END
        await WriteSseEventAsync(context.Response, new
        {
            type = "TEXT_MESSAGE_END",
            messageId
        }, cancellationToken);

        // RUN_FINISHED
        await WriteSseEventAsync(context.Response, new
        {
            type = "RUN_FINISHED",
            threadId,
            runId
        }, cancellationToken);

        // 6. Persist session (best-effort — don't block chat on persistence failure)
        try
        {
            var serializedPreview = aiAgent.SerializeSession(session);
            logger?.LogInformation(
                "[SessionDebug] About to save session — threadId={ThreadId}, agentId={AgentId}, serializedLength={Len}",
                threadId, aiAgent.Id, serializedPreview.GetRawText().Length);
            await sessionStore.SaveSessionAsync(aiAgent, threadId, session, cancellationToken);
            logger?.LogInformation("[SessionDebug] Session saved successfully");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[SessionDebug] Failed to save session for thread {ThreadId}", threadId);
        }
    }

    /// <summary>ChatClient Agent — 无状态模式（backward-compatible, EnableChatHistory=false）</summary>
    private static async Task HandleChatClientStatelessAsync(
        HttpContext context,
        AIAgent aiAgent,
        IChatClient chatClient,
        AgentChatInput input,
        string threadId,
        string runId,
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
                        var resultStr = functionResult.Result?.ToString();
                        await WriteToolCallEndAsync(context.Response, toolCallId, resultStr, cancellationToken);
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
    }

    /// <summary>A2A Agent 流式处理（通过 AIAgent.RunStreamingAsync）</summary>
    private static async Task HandleA2AStreamAsync(
        HttpContext context,
        AIAgent aiAgent,
        AgentChatInput input,
        string threadId,
        string runId,
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
    }

    /// <summary>SSE 事件写入辅助方法</summary>
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

    private static async Task WriteToolCallEndAsync(HttpResponse response, string toolCallId, string? result, CancellationToken cancellationToken)
    {
        await WriteSseEventAsync(response, new
        {
            type = "TOOL_CALL_END",
            toolCallId,
            result
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

