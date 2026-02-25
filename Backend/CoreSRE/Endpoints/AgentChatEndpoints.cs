using CoreSRE.Application.Chat.DTOs;
using CoreSRE.Application.Interfaces;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Http.Features;
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

        // 设置 SSE 响应头 — 必须在任何写入之前配置
        context.Response.ContentType = "text/event-stream; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache,no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.Headers["X-Accel-Buffering"] = "no"; // Disable nginx/proxy buffering

        // 关键：禁用 ASP.NET Core 响应缓冲，确保每次 FlushAsync 立即推送到客户端
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

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
            else if (resolved.IsTeam)
            {
                // ===== Team Agent 路径（多 Agent 协同编排）=====
                await HandleTeamStreamAsync(context, aiAgent, input, threadId, runId, resolved.TeamEventQueue, sessionStore, cancellationToken);
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

        // 3. Build full message list = session history + new user message.
        //    WORKAROUND: Microsoft.Agents.AI 1.0.0-preview.260209.1 has a bug where
        //    RunCoreStreamingAsync passes inputMessagesForProviders (which excludes session history)
        //    to chatClient.GetStreamingResponseAsync instead of inputMessagesForChatClient.
        //    By passing the full history as input messages ourselves, inputMessagesForProviders
        //    will contain all messages that need to reach the LLM.
        var lastMessage = input.Messages?.LastOrDefault();
        var newUserMessage = new ChatMessage(
            ChatRole.User,
            lastMessage?.Content ?? string.Empty);

        var allMessages = new List<ChatMessage>();
        if (historyProvider is { Count: > 0 })
        {
            allMessages.AddRange(historyProvider);
        }
        allMessages.Add(newUserMessage);

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

        // 5. Stream via agent pipeline
        //    Pass the full message list (history + new) directly — see WORKAROUND note above.
        logger?.LogInformation("[SessionDebug] Starting RunStreamingAsync — historyCount={HistoryCount}, totalMessages={TotalMessages}",
            historyProvider?.Count ?? 0, allMessages.Count);
        await foreach (var update in aiAgent.RunStreamingAsync(allMessages, session, cancellationToken: cancellationToken))
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

    /// <summary>Team Agent 流式处理 — 通过 Workflow-backed AIAgent.RunStreamingAsync 编排多个参与者。
    /// 支持参与者归属（participantAgentId/Name）和交接通知（TEAM_HANDOFF）。
    ///
    /// Framework streaming pipeline (decompiled from Microsoft.Agents.AI.Workflows 1.0.0-preview.260209.1):
    ///   WorkflowHostAgent.RunCoreStreamingAsync → WorkflowSession.InvokeStageAsync
    ///     → StreamingRun.WatchStreamAsync → yields WorkflowEvent objects
    ///     → AgentResponseUpdateEvent  → yields update.Update (streaming content from participant agent)
    ///     → SuperStepCompletedEvent    → yields EMPTY AgentResponseUpdate (no contents, just metadata)
    ///     → WorkflowErrorEvent         → yields AgentResponseUpdate with ErrorContent
    ///     → RequestInfoEvent           → yields AgentResponseUpdate with FunctionCallContent
    ///     → other events               → yields EMPTY AgentResponseUpdate with RawRepresentation = event
    ///
    /// Key: GroupChatHost forwards TurnToken(emitEvents=true) to AIAgentHostExecutor,
    ///      which calls agent.RunStreamingAsync and emits AgentResponseUpdateEvent per token.
    ///      Empty updates from SuperStepCompletedEvent etc. must be filtered / handled.
    /// </summary>
    private static async Task HandleTeamStreamAsync(
        HttpContext context,
        AIAgent aiAgent,
        AgentChatInput input,
        string threadId,
        string runId,
        System.Collections.Concurrent.ConcurrentQueue<TeamChatEventDto>? eventQueue,
        AgentSessionStore sessionStore,
        CancellationToken cancellationToken)
    {
        var logger = context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("AgentChatEndpoints");

        logger?.LogInformation(
            "[TeamStream] ENTER HandleTeamStreamAsync — agentId={AgentId}, agentName={AgentName}, agentType={AgentType}, threadId={ThreadId}, runId={RunId}, messageCount={MsgCount}, hasEventQueue={HasQueue}",
            aiAgent.Id, aiAgent.Name, aiAgent.GetType().Name, threadId, runId,
            input.Messages?.Count ?? 0, eventQueue is not null);

        // 转换 AG-UI 消息为 ChatMessage
        var chatMessages = input.Messages?.Select(m =>
            new ChatMessage(MapRole(m.Role), m.Content ?? string.Empty)
        ).ToList() ?? [];

        // Load or create session for history persistence
        AgentSession? session = null;
        try
        {
            session = await sessionStore.GetSessionAsync(aiAgent, threadId, cancellationToken);
            logger?.LogInformation("[TeamStream] Session loaded from store — threadId={ThreadId}", threadId);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[TeamStream] Session load failed, creating new — threadId={ThreadId}", threadId);
            session = await aiAgent.CreateSessionAsync(cancellationToken);
        }

        logger?.LogInformation("[TeamStream] Session ready — type={SessionType}", session?.GetType().Name);

        // 发送 RUN_STARTED
        logger?.LogInformation("[TeamStream] Writing RUN_STARTED SSE event");
        await WriteSseEventAsync(context.Response, new
        {
            type = "RUN_STARTED",
            threadId,
            runId
        }, cancellationToken);

        var messageId = Guid.NewGuid().ToString();
        string? currentParticipantId = null;
        string? currentParticipantName = null;
        bool messageStarted = false;
        int stepCount = 0;
        int totalUpdateCount = 0;
        int contentUpdateCount = 0;

        // 通过 Workflow AIAgent.RunStreamingAsync 编排参与者
        logger?.LogInformation(
            "[TeamStream] Starting RunStreamingAsync — chatMessages={Count}, sessionType={SessionType}",
            chatMessages.Count, session?.GetType().Name);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
        // Use manual async enumeration with heartbeat: during MagneticOne's orchestration
        // phase (facts/plan/ledger LLM calls), the framework yields ZERO events for minutes.
        // Without periodic heartbeats, the browser/proxy considers the SSE connection dead.
        const int HeartbeatIntervalMs = 5_000;
        var enumerator = aiAgent.RunStreamingAsync(chatMessages, session, cancellationToken: cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        try
        {
        bool hasMore = true;
        while (hasMore)
        {
            // Race: next update vs heartbeat timer
            var moveTask = enumerator.MoveNextAsync().AsTask();
            while (!moveTask.IsCompleted)
            {
                var completed = await Task.WhenAny(moveTask, Task.Delay(HeartbeatIntervalMs, cancellationToken));
                if (completed != moveTask)
                {
                    // Heartbeat — keep SSE connection alive during long orchestration phases
                    logger?.LogDebug("[TeamStream] Sending heartbeat @{Elapsed}ms", sw.ElapsedMilliseconds);
                    await context.Response.WriteAsync(": heartbeat\n\n", cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                    // Also drain pending ledger events that accumulated during orchestration
                    if (eventQueue is not null)
                    {
                        while (eventQueue.TryDequeue(out var pendingEvent))
                        {
                            if (pendingEvent is TeamLedgerUpdateEventDto plu)
                            {
                                await WriteSseEventAsync(context.Response, new
                                {
                                    type = "TEAM_LEDGER_UPDATE",
                                    ledgerType = plu.LedgerType,
                                    agentName = plu.AgentName,
                                    content = plu.Content
                                }, cancellationToken);
                            }
                        }
                    }
                }
            }
            hasMore = moveTask.Result;
            if (!hasMore) break;

            var update = enumerator.Current;
            totalUpdateCount++;
            if (totalUpdateCount == 1)
            {
                logger?.LogInformation("[TeamStream] First update received after {Elapsed}ms", sw.ElapsedMilliseconds);
            }

            // ── Detect participant change via AgentId/AuthorName ───────────────
            var updateAgentId = update.AgentId;
            var updateAgentName = update.AuthorName;

            // Skip empty "metadata-only" updates from internal framework events
            // (SuperStepCompletedEvent, etc.) that have no content and no agent identity.
            // These yield AgentResponseUpdate(Role=Assistant, Contents=[]) as heartbeats.
            var hasContent = update.Contents.Count > 0;
            var hasAgentIdentity = updateAgentId is not null;

            // Log every update at Information level for full-chain debugging
            logger?.LogInformation(
                "[TeamStream] Update #{Count} @{Elapsed}ms: AgentId={AgentId}, AuthorName={AuthorName}, " +
                "ContentsCount={ContentsCount}, ContentTypes=[{ContentTypes}], RawRepType={RawType}, Role={Role}, TextLen={TextLen}",
                totalUpdateCount, sw.ElapsedMilliseconds,
                updateAgentId ?? "(null)", updateAgentName ?? "(null)",
                update.Contents.Count,
                string.Join(",", update.Contents.Select(c => c.GetType().Name)),
                update.RawRepresentation?.GetType().Name ?? "(null)",
                update.Role.Value,
                update.Text?.Length ?? 0);

            if (!hasContent && !hasAgentIdentity)
            {
                // Pure metadata update (e.g. SuperStepCompletedEvent) — no content to display.
                // Send SSE comment as keepalive to prevent proxy/browser from timing out the connection.
                var rawType = update.RawRepresentation?.GetType().Name ?? "Unknown";
                logger?.LogInformation("[TeamStream] Metadata event #{Count}: {RawType} — sending keepalive", totalUpdateCount, rawType);
                await context.Response.WriteAsync($": keepalive {rawType}\n\n", cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);

                // Close current message on iteration boundary so each iteration
                // gets its own message bubble, even when the same agent continues.
                if (rawType.Contains("SuperStepCompleted", StringComparison.OrdinalIgnoreCase) && messageStarted)
                {
                    logger?.LogInformation("[TeamStream] SuperStepCompleted — closing message {MessageId} for iteration boundary", messageId);
                    await WriteSseEventAsync(context.Response, new
                    {
                        type = "TEXT_MESSAGE_END",
                        messageId
                    }, cancellationToken);
                    messageStarted = false;
                    // Keep currentParticipantId/Name — same agent may continue in next iteration
                }
                // Drain any pending ledger events before continuing.
                if (eventQueue is not null)
                {
                    while (eventQueue.TryDequeue(out var ledgerEvent))
                    {
                        if (ledgerEvent is TeamLedgerUpdateEventDto lu)
                        {
                            await WriteSseEventAsync(context.Response, new
                            {
                                type = "TEAM_LEDGER_UPDATE",
                                ledgerType = lu.LedgerType,
                                agentName = lu.AgentName,
                                content = lu.Content
                            }, cancellationToken);
                        }
                    }
                }
                continue;
            }

            // Skip WorkflowOutputEvent duplicate: after streaming via AgentResponseUpdateEvent,
            // the framework yields a final WorkflowOutputEvent with the full aggregated response
            // (AgentId=null, Contents=all output). If we already streamed content, skip it.
            if (!hasAgentIdentity && messageStarted && hasContent)
            {
                logger?.LogInformation(
                    "[TeamStream] Skipping WorkflowOutputEvent duplicate — {ContentCount} content items, already streamed via agent events",
                    update.Contents.Count);
                continue;
            }

            if (hasAgentIdentity && updateAgentId != currentParticipantId)
            {
                logger?.LogInformation(
                    "[TeamStream] Participant change: {OldId}({OldName}) → {NewId}({NewName})",
                    currentParticipantId ?? "(none)", currentParticipantName ?? "(none)",
                    updateAgentId, updateAgentName);
                // Close previous participant's message if one was open
                if (messageStarted)
                {
                    await WriteSseEventAsync(context.Response, new
                    {
                        type = "TEXT_MESSAGE_END",
                        messageId
                    }, cancellationToken);

                    // Mark previous agent's inner ledger entry as completed
                    if (currentParticipantName is not null)
                    {
                        await WriteSseEventAsync(context.Response, new
                        {
                            type = "TEAM_LEDGER_UPDATE",
                            ledgerType = "inner",
                            agentName = currentParticipantName,
                            content = JsonSerializer.Serialize(new
                            {
                                agentName = currentParticipantName,
                                task = (string?)null,
                                status = "completed",
                                summary = (string?)null,
                                timestamp = DateTime.UtcNow
                            }, s_jsonOptions)
                        }, cancellationToken);
                    }

                    // Emit TEAM_HANDOFF event for participant transitions
                    await WriteSseEventAsync(context.Response, new
                    {
                        type = "TEAM_HANDOFF",
                        fromAgentId = currentParticipantId,
                        fromAgentName = currentParticipantName,
                        toAgentId = updateAgentId,
                        toAgentName = updateAgentName
                    }, cancellationToken);
                }

                // Start a new message for the new participant
                currentParticipantId = updateAgentId;
                currentParticipantName = updateAgentName;
                messageId = Guid.NewGuid().ToString();
                stepCount++;

                // Emit TEAM_PROGRESS event
                await WriteSseEventAsync(context.Response, new
                {
                    type = "TEAM_PROGRESS",
                    currentAgentId = updateAgentId,
                    currentAgentName = updateAgentName,
                    step = stepCount
                }, cancellationToken);

                await WriteSseEventAsync(context.Response, new
                {
                    type = "TEXT_MESSAGE_START",
                    messageId,
                    role = "assistant",
                    participantAgentId = updateAgentId,
                    participantAgentName = updateAgentName
                }, cancellationToken);

                messageStarted = true;
            }
            else if (hasAgentIdentity && updateAgentId == currentParticipantId && !messageStarted && hasContent)
            {
                // Same agent continuing in a new iteration — start a fresh message with participant identity
                messageId = Guid.NewGuid().ToString();
                stepCount++;

                await WriteSseEventAsync(context.Response, new
                {
                    type = "TEAM_PROGRESS",
                    currentAgentId = updateAgentId,
                    currentAgentName = updateAgentName,
                    step = stepCount
                }, cancellationToken);

                await WriteSseEventAsync(context.Response, new
                {
                    type = "TEXT_MESSAGE_START",
                    messageId,
                    role = "assistant",
                    participantAgentId = updateAgentId,
                    participantAgentName = updateAgentName
                }, cancellationToken);
                messageStarted = true;
            }
            else if (!messageStarted && hasContent)
            {
                // First update with content but no AgentId — start a generic message
                await WriteSseEventAsync(context.Response, new
                {
                    type = "TEXT_MESSAGE_START",
                    messageId,
                    role = "assistant"
                }, cancellationToken);
                messageStarted = true;
            }

            // ── Process content items ─────────────────────────────────────────
            foreach (var content in update.Contents)
            {
                if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                {
                    contentUpdateCount++;
                    await WriteSseEventAsync(context.Response, new
                    {
                        type = "TEXT_MESSAGE_CONTENT",
                        messageId,
                        delta = textContent.Text,
                        participantAgentId = currentParticipantId,
                        participantAgentName = currentParticipantName
                    }, cancellationToken);
                }
                else if (content is FunctionCallContent functionCall)
                {
                    var toolCallId = functionCall.CallId ?? Guid.NewGuid().ToString();

                    // Detect handoff tool calls (handoff_to_* pattern)
                    var toolName = functionCall.Name ?? "unknown";
                    if (toolName.StartsWith("handoff_to_", StringComparison.OrdinalIgnoreCase))
                    {
                        var targetName = toolName["handoff_to_".Length..];
                        await WriteSseEventAsync(context.Response, new
                        {
                            type = "TEAM_HANDOFF",
                            fromAgentId = currentParticipantId,
                            fromAgentName = currentParticipantName,
                            toAgentId = (string?)null,
                            toAgentName = targetName
                        }, cancellationToken);
                    }

                    await WriteToolCallStartAsync(context.Response, toolCallId, toolName, messageId, cancellationToken);

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
                else if (content is ErrorContent errorContent)
                {
                    // WorkflowErrorEvent from framework — surface as RUN_ERROR
                    logger?.LogError("Workflow error event: {Error}", errorContent.Message);
                    await WriteSseEventAsync(context.Response, new
                    {
                        type = "RUN_ERROR",
                        message = errorContent.Message ?? "An error occurred during team orchestration.",
                        participantAgentName = currentParticipantName
                    }, cancellationToken);
                }
            }

            // ── Drain MagneticOne ledger update events ────────────────────────
            if (eventQueue is not null)
            {
                while (eventQueue.TryDequeue(out var ledgerEvent))
                {
                    if (ledgerEvent is TeamLedgerUpdateEventDto lu)
                    {
                        await WriteSseEventAsync(context.Response, new
                        {
                            type = "TEAM_LEDGER_UPDATE",
                            ledgerType = lu.LedgerType,
                            agentName = lu.AgentName,
                            content = lu.Content
                        }, cancellationToken);
                    }
                }
            }
        } // end while (hasMore)

        // Final drain of pending ledger events (e.g., final answer update from ShouldTerminateAsync)
        if (eventQueue is not null)
        {
            while (eventQueue.TryDequeue(out var finalEvent))
            {
                if (finalEvent is TeamLedgerUpdateEventDto flu)
                {
                    await WriteSseEventAsync(context.Response, new
                    {
                        type = "TEAM_LEDGER_UPDATE",
                        ledgerType = flu.LedgerType,
                        agentName = flu.AgentName,
                        content = flu.Content
                    }, cancellationToken);
                }
            }
        }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Client disconnected — don't send error events (connection is dead)
            logger?.LogInformation(
                "[TeamStream] CANCELLED by client after {Elapsed}ms — thread={ThreadId}, steps={Steps}, updates={Updates}, contentUpdates={Content}",
                sw.ElapsedMilliseconds, threadId, stepCount, totalUpdateCount, contentUpdateCount);
            return; // Skip TEXT_MESSAGE_END / RUN_FINISHED — client is gone
        }
        catch (Exception ex)
        {
            logger?.LogError(ex,
                "[TeamStream] EXCEPTION after {Elapsed}ms — exType={ExType}, participant={ParticipantName}, thread={ThreadId}, steps={Steps}, totalUpdates={TotalUpdates}, contentUpdates={ContentUpdates}",
                sw.ElapsedMilliseconds, ex.GetType().Name, currentParticipantName ?? "(unknown)", threadId, stepCount, totalUpdateCount, contentUpdateCount);

            // Close any open message before emitting error
            if (messageStarted)
            {
                try
                {
                    await WriteSseEventAsync(context.Response, new
                    {
                        type = "TEXT_MESSAGE_END",
                        messageId
                    }, cancellationToken);
                }
                catch { /* best effort */ }
                messageStarted = false;
            }

            // Detect max iterations exceeded (framework may throw with iteration-related message)
            var isMaxIterations = ex.Message.Contains("iteration", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("maximum", StringComparison.OrdinalIgnoreCase);

            var errorMessage = isMaxIterations
                ? $"Team orchestration reached maximum iterations ({stepCount} steps completed). The response may be incomplete."
                : ex is OperationCanceledException or TimeoutException
                    ? $"Team orchestration timed out after {stepCount} steps: {ex.Message}"
                    : currentParticipantName is not null
                        ? $"Participant '{currentParticipantName}' encountered an error: {ex.Message}"
                        : $"Team orchestration error: {ex.Message}";

            try
            {
                await WriteSseEventAsync(context.Response, new
                {
                    type = "RUN_ERROR",
                    message = errorMessage,
                    participantAgentName = currentParticipantName,
                    isMaxIterations
                }, cancellationToken);
            }
            catch { /* best effort — connection may already be dead */ }
        }

        sw.Stop();
        logger?.LogInformation(
            "[TeamStream] COMPLETE in {Elapsed}ms — thread={ThreadId}, steps={Steps}, totalUpdates={TotalUpdates}, contentUpdates={ContentUpdates}",
            sw.ElapsedMilliseconds, threadId, stepCount, totalUpdateCount, contentUpdateCount);

        // Close the last participant's message
        if (messageStarted)
        {
            await WriteSseEventAsync(context.Response, new
            {
                type = "TEXT_MESSAGE_END",
                messageId
            }, cancellationToken);
        }

        // 发送 RUN_FINISHED
        await WriteSseEventAsync(context.Response, new
        {
            type = "RUN_FINISHED",
            threadId,
            runId
        }, cancellationToken);

        // Persist team session (best-effort — don't block chat on persistence failure)
        try
        {
            await sessionStore.SaveSessionAsync(aiAgent, threadId, session, cancellationToken);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to save team session for thread {ThreadId}", threadId);
        }
    }

    /// <summary>SSE 事件写入辅助方法 — logs event type for debugging</summary>
    private static async Task WriteSseEventAsync(HttpResponse response, object eventData, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(eventData, s_jsonOptions);

        // Log every SSE event write for full-chain tracing
        var logger = response.HttpContext.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("AgentChatEndpoints");
        // Extract "type" value safely: find :"<value>" after "type"
        string eventType = "?";
        var typeIdx = json.IndexOf("\"type\"");
        if (typeIdx >= 0)
        {
            // Skip past "type":" to find the opening quote of the value
            var valueStart = json.IndexOf('"', typeIdx + 6 + 1); // skip "type" (6 chars) then : to find opening "
            if (valueStart >= 0)
            {
                var valueEnd = json.IndexOf('"', valueStart + 1); // closing quote
                if (valueEnd > valueStart)
                    eventType = json[(valueStart + 1)..valueEnd];
            }
        }
        logger?.LogInformation("[SSE] Writing event type={EventType}, jsonLen={Len}, connected={Connected}",
            eventType, json.Length, !cancellationToken.IsCancellationRequested);

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

