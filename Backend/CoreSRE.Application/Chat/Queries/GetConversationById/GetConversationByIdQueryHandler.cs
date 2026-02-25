using System.Text.Json;
using AutoMapper;
using CoreSRE.Application.Chat.DTOs;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Chat.Queries.GetConversationById;

/// <summary>
/// 查询对话详情处理器 — 加载对话元数据 + 从 AgentSessionRecord.SessionData 提取消息历史。
///
/// Agent Framework 有两种 Session 格式：
///
/// 1. ChatClientAgentSession（单 Agent）：
///    {
///      chatHistoryProviderState: { messages: [...] },   ← 完整对话
///      aiContextProviderState: { ... }
///    }
///
/// 2. WorkflowSession（Team/Workflow Agent）：
///    {
///      runId: "...",
///      chatHistoryProviderState: { bookmark: N, messages: [...] },  ← 仅入站用户消息
///      checkpointManager: {                                         ← InMemoryCheckpointManager
///        store: {
///          "&lt;runId&gt;": {                                       ← RunCheckpointCache
///            checkpointIndex: [{ runId, checkpointId }],
///            cache: {
///              "&lt;runId&gt;|&lt;cpId&gt;": {                      ← Checkpoint (pipe-delimited key)
///                stateData: {
///                  "&lt;executorId&gt;||AIAgentHostState": {         ← PortableValue (ScopeKey pipe format)
///                    typeId: { assemblyName, typeName },
///                    value: {                                        ← AIAgentHostState
///                      threadState: {                                ← 内部 Agent 的序列化 Session
///                        chatHistoryProviderState: {
///                          messages: [...]                           ← 完整对话（user + assistant + tool）
///                        }
///                      }
///                    }
///                  }
///                }
///              }
///            }
///          }
///        }
///      }
///    }
/// </summary>
public class GetConversationByIdQueryHandler : IRequestHandler<GetConversationByIdQuery, Result<ConversationDto>>
{
    private readonly IConversationRepository _conversationRepository;
    private readonly IAgentRegistrationRepository _agentRepository;
    private readonly IChatHistoryReader _chatHistoryReader;
    private readonly IMapper _mapper;

    public GetConversationByIdQueryHandler(
        IConversationRepository conversationRepository,
        IAgentRegistrationRepository agentRepository,
        IChatHistoryReader chatHistoryReader,
        IMapper mapper)
    {
        _conversationRepository = conversationRepository;
        _agentRepository = agentRepository;
        _chatHistoryReader = chatHistoryReader;
        _mapper = mapper;
    }

    public async Task<Result<ConversationDto>> Handle(
        GetConversationByIdQuery request,
        CancellationToken cancellationToken)
    {
        var conversation = await _conversationRepository.GetByIdAsync(request.Id, cancellationToken);
        if (conversation is null)
            return Result<ConversationDto>.NotFound($"Conversation with ID '{request.Id}' not found.");

        var dto = _mapper.Map<ConversationDto>(conversation);

        // 加载 Agent 信息
        var agent = await _agentRepository.GetByIdAsync(conversation.AgentId, cancellationToken);
        if (agent is not null)
        {
            dto.AgentName = agent.Name;
            dto.AgentType = agent.AgentType.ToString();
        }

        // 从 AgentSessionRecord.SessionData 提取消息
        // AgentSessionRecord 使用 (agentId, conversationId) 复合主键
        var agentId = conversation.AgentId.ToString();
        var sessionData = await _chatHistoryReader.GetSessionDataAsync(
            agentId, conversation.Id.ToString(), cancellationToken);

        if (sessionData.HasValue)
        {
            dto.Messages = ExtractMessages(sessionData.Value);
        }

        return Result<ConversationDto>.Ok(dto);
    }

    /// <summary>
    /// 从 SessionData JSON 提取消息列表。
    ///
    /// 策略：
    /// 1. 先检测是否为 WorkflowSession（有 runId + checkpointManager）。
    ///    若是，从最后一个 checkpoint 的 ALL executor stateData 中合并完整对话：
    ///    - 获取真实用户输入（顶层 chatHistoryProviderState.messages）
    ///    - 从每个 participant executor 线程中提取 assistant/tool 消息（含 authorName）
    ///    - 按执行顺序交织合并，去重
    /// 2. 否则为 ChatClientAgentSession，直接从顶层 chatHistoryProviderState.messages[] 读取。
    ///
    /// 消息解析策略：
    /// 1. system + source=memory → 提取内容，附加到下一条 user 消息的 MemoryContext
    /// 2. system (非 memory) → 跳过
    /// 3. assistant → 提取文本 + functionCall 内容 → 输出一条带 toolCalls 的消息
    /// 4. tool → 将 functionResult 匹配回前一条 assistant 的 toolCalls
    /// 5. user → 正常输出，并附加待处理的 memory context（过滤编排器注入的内部消息）
    /// </summary>
    private static List<ChatMessageDto> ExtractMessages(JsonElement sessionData)
    {
        // ── WorkflowSession path ─────────────────────────────────────
        bool isWorkflowSession = sessionData.TryGetProperty("runId", out _)
            && (sessionData.TryGetProperty("checkpointManager", out _)
                || sessionData.TryGetProperty("lastCheckpoint", out _));

        if (isWorkflowSession)
        {
            var merged = ExtractMergedWorkflowMessages(sessionData);
            if (merged is not null)
                return merged;
        }

        // ── ChatClientAgentSession path (or fallback) ────────────────
        if (sessionData.TryGetProperty("chatHistoryProviderState", out var historyState)
            && historyState.TryGetProperty("messages", out var topMsgs))
        {
            return ParseMessageArray(topMsgs);
        }

        return [];
    }

    // ═══════════════════════════════════════════════════════════════════
    // WorkflowSession Extraction — Multi-executor Merge
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// MagneticOne / Workflow 多 executor 合并提取策略。
    ///
    /// WorkflowSession 的 checkpoint 中包含多个 executor：
    ///   - 每个参与 agent 有自己的 executor 线程（含正确的 authorName 归属）
    ///   - 编排器 executor 维护一个"合并"线程，agent 回复被注入为 user-role 消息（错误归属）
    ///
    /// 本方法的策略：
    ///   1. 遍历最后一个 checkpoint 的所有 executor stateData
    ///   2. 从每个 executor 线程中提取消息
    ///   3. 选择"最佳"线程 = assistant 消息中有 authorName 归属的线程（participant executor）
    ///      而非消息总数最多的线程（可能是编排器的合并线程）
    ///   4. 如果无 authorName 归属，降级为取消息最多的线程（向后兼容）
    /// </summary>
    private static List<ChatMessageDto>? ExtractMergedWorkflowMessages(JsonElement sessionData)
    {
        var lastCheckpointStateData = FindLastCheckpointStateData(sessionData);
        if (!lastCheckpointStateData.HasValue)
            return null;

        // Collect all executor threads from the checkpoint stateData
        var executorThreads = new List<(string executorId, JsonElement messages)>();
        foreach (var prop in lastCheckpointStateData.Value.EnumerateObject())
        {
            if (!prop.Name.EndsWith("||AIAgentHostState", StringComparison.Ordinal))
                continue;

            var executorId = prop.Name[..prop.Name.IndexOf("||AIAgentHostState", StringComparison.Ordinal)];
            var msgs = NavigateToMessages(prop.Value);
            if (msgs.HasValue)
                executorThreads.Add((executorId, msgs.Value));
        }

        if (executorThreads.Count == 0)
            return null;

        // ── Strategy: pick the best thread for message extraction ────
        //
        // Participant executor threads have assistant messages with authorName.
        // The orchestrator executor's thread has agent responses re-injected as
        // user-role messages (wrong attribution). Score each thread:
        //   - +1 for each assistant message with authorName (participant thread)
        //   - Thread with highest score wins
        //   - Tie: pick the thread with more total messages
        //   - If no thread has authorName: fallback to most messages (backward compat)

        (string executorId, JsonElement messages) best = executorThreads[0];
        int bestScore = ScoreExecutorThread(best.messages);
        int bestCount = best.messages.GetArrayLength();

        for (var i = 1; i < executorThreads.Count; i++)
        {
            var thread = executorThreads[i];
            var score = ScoreExecutorThread(thread.messages);
            var count = thread.messages.GetArrayLength();

            if (score > bestScore || (score == bestScore && count > bestCount))
            {
                best = thread;
                bestScore = score;
                bestCount = count;
            }
        }

        return ParseMessageArray(best.messages);
    }

    /// <summary>
    /// Score an executor thread by counting assistant messages that have authorName attribution.
    /// A higher score indicates this is a participant executor (not the orchestrator merge thread).
    /// </summary>
    private static int ScoreExecutorThread(JsonElement messagesArray)
    {
        int score = 0;
        foreach (var msg in messagesArray.EnumerateArray())
        {
            var role = msg.TryGetProperty("role", out var rp) ? rp.GetString() : null;
            if (role != "assistant") continue;

            if (msg.TryGetProperty("authorName", out var an) && !string.IsNullOrEmpty(an.GetString()))
                score++;
        }
        return score;
    }

    /// <summary>
    /// Navigate from checkpoint stateData to the last checkpoint's stateData object.
    /// </summary>
    private static JsonElement? FindLastCheckpointStateData(JsonElement sessionData)
    {
        if (!sessionData.TryGetProperty("checkpointManager", out var cpManager))
            return null;
        if (!cpManager.TryGetProperty("store", out var store))
            return null;

        // Find the run's checkpoint cache (iterate — usually just one runId)
        JsonElement? runCache = null;
        foreach (var runEntry in store.EnumerateObject())
        {
            runCache = runEntry.Value;
            break;
        }
        if (!runCache.HasValue)
            return null;

        // Find the last checkpoint via checkpointIndex
        if (!runCache.Value.TryGetProperty("checkpointIndex", out var cpIndex))
            return null;

        JsonElement? lastCpInfo = null;
        foreach (var cpInfo in cpIndex.EnumerateArray())
            lastCpInfo = cpInfo;

        if (!lastCpInfo.HasValue)
            return null;

        // Build the pipe-delimited key for cache lookup: "runId|checkpointId"
        if (!runCache.Value.TryGetProperty("cache", out var cache))
            return null;

        var lastRunId = lastCpInfo.Value.TryGetProperty("runId", out var rid) ? rid.GetString() : null;
        var lastCpId = lastCpInfo.Value.TryGetProperty("checkpointId", out var cid) ? cid.GetString() : null;

        JsonElement? checkpoint = null;
        if (lastRunId is not null && lastCpId is not null)
        {
            var cacheKey = $"{lastRunId}|{lastCpId}";
            if (cache.TryGetProperty(cacheKey, out var cp))
                checkpoint = cp;
        }

        // Fallback: iterate all cache entries (take highest stepNumber)
        if (!checkpoint.HasValue)
        {
            int maxStep = -2;
            foreach (var cpEntry in cache.EnumerateObject())
            {
                var step = cpEntry.Value.TryGetProperty("stepNumber", out var sn) ? sn.GetInt32() : -2;
                if (step > maxStep)
                {
                    maxStep = step;
                    checkpoint = cpEntry.Value;
                }
            }
        }

        if (!checkpoint.HasValue)
            return null;

        return checkpoint.Value.TryGetProperty("stateData", out var stateData)
            ? stateData
            : null;
    }

    /// <summary>
    /// Navigate a PortableValue entry to its messages array.
    ///   value.threadState.chatHistoryProviderState.messages[]
    ///   OR threadState.chatHistoryProviderState.messages[] (unwrapped fallback)
    /// </summary>
    private static JsonElement? NavigateToMessages(JsonElement portableValue)
    {
        JsonElement agentHostState;
        if (portableValue.TryGetProperty("value", out var wrapped))
            agentHostState = wrapped;
        else
            agentHostState = portableValue;

        if (!agentHostState.TryGetProperty("threadState", out var threadState))
            return null;
        if (!threadState.TryGetProperty("chatHistoryProviderState", out var hist))
            return null;
        if (!hist.TryGetProperty("messages", out var msgs))
            return null;

        return msgs;
    }

    /// <summary>
    /// 解析 messages JSON 数组为 ChatMessageDto 列表。
    /// 过滤系统消息、编排器注入的内部指令、重复消息。
    ///
    /// 特殊处理：Framework 的 ReassignOtherAgentsAsUsers 机制会将 GroupChat 中
    /// 其他 agent 的 assistant 消息转换为 user role（但保留 authorName）。
    /// 本方法通过检测 user-role + 非空 authorName（非 Orchestrator）来反转此转换，
    /// 将这些消息正确显示为 assistant role。
    /// </summary>
    private static List<ChatMessageDto> ParseMessageArray(JsonElement messagesArray)
    {
        var messages = new List<ChatMessageDto>();
        var index = 0;
        string? pendingMemory = null; // memory context waiting to attach to next user message
        string? lastAssistantContent = null; // dedup consecutive identical assistant messages

        foreach (var msg in messagesArray.EnumerateArray())
        {
            var role = msg.TryGetProperty("role", out var roleProp)
                ? roleProp.GetString() ?? "user"
                : "user";

            var contents = msg.TryGetProperty("contents", out var contentsProp)
                ? contentsProp
                : (JsonElement?)null;

            var source = msg.TryGetProperty("source", out var sourceProp)
                ? sourceProp.GetString()
                : null;

            // ── system messages ──────────────────────────────────────
            if (role == "system")
            {
                if (source == "memory")
                {
                    // Extract text content and hold it for the next user message
                    pendingMemory = ExtractTextContent(contents);
                }
                // Skip all system messages (don't emit to frontend)
                continue;
            }

            // ── tool messages (functionResult) ───────────────────────
            if (role == "tool")
            {
                // Match results back to the last assistant message's toolCalls
                if (contents.HasValue && messages.Count > 0)
                {
                    var lastMsg = messages[^1];
                    if (lastMsg.Role == "assistant" && lastMsg.ToolCalls is { Count: > 0 })
                    {
                        foreach (var c in contents.Value.EnumerateArray())
                        {
                            if (GetContentKind(c) != "functionResult") continue;

                            var callId = c.TryGetProperty("callId", out var idProp)
                                ? idProp.GetString() ?? ""
                                : "";

                            var result = c.TryGetProperty("result", out var resProp)
                                ? resProp.ToString()
                                : null;

                            var tc = lastMsg.ToolCalls.Find(t => t.ToolCallId == callId);
                            if (tc is not null)
                            {
                                tc.Result = result;
                            }
                        }
                    }
                }
                // Don't emit tool messages as separate entries
                continue;
            }

            // ── user messages ────────────────────────────────────────
            if (role == "user")
            {
                // MagneticOne orchestrator injects user-role messages with authorName="Orchestrator"
                // via UpdateHistoryAsync. Filter these definitively by authorName.
                var userAuthorName = msg.TryGetProperty("authorName", out var uanProp)
                    ? uanProp.GetString()
                    : null;
                if (string.Equals(userAuthorName, "Orchestrator", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Framework's ReassignOtherAgentsAsUsers in AIAgentHostExecutor.ContinueTurnAsync
                // converts assistant messages from OTHER agents to user role (for LLM context)
                // but preserves the original authorName. Detect and revert to assistant:
                //   - Real user messages have authorName = null
                //   - Orchestrator injections have authorName = "Orchestrator" (handled above)
                //   - Converted assistant messages have authorName = <agent-name>
                if (!string.IsNullOrEmpty(userAuthorName))
                {
                    var convertedText = ExtractTextContent(contents);

                    // Apply same dedup logic as assistant messages
                    if (!string.IsNullOrEmpty(convertedText) && convertedText == lastAssistantContent)
                        continue;

                    messages.Add(new ChatMessageDto
                    {
                        Index = index++,
                        Role = "assistant",
                        Content = convertedText,
                        ParticipantAgentName = userAuthorName
                    });
                    lastAssistantContent = string.IsNullOrEmpty(convertedText) ? null : convertedText;
                    continue;
                }

                var text = ExtractTextContent(contents);

                // Fallback heuristic for older sessions without authorName:
                // orchestrator-injected prompts contain plan/facts markers.
                if (IsOrchestratorInjectedMessage(text))
                    continue;

                var dto = new ChatMessageDto
                {
                    Index = index++,
                    Role = "user",
                    Content = text,
                    MemoryContext = pendingMemory
                };
                pendingMemory = null;
                messages.Add(dto);
                continue;
            }

            // ── assistant messages ───────────────────────────────────
            if (role == "assistant")
            {
                var text = "";
                List<ToolCallDto>? toolCalls = null;

                // Extract participant attribution from authorName (Team mode)
                var authorName = msg.TryGetProperty("authorName", out var anProp)
                    ? anProp.GetString()
                    : null;

                if (contents.HasValue)
                {
                    foreach (var c in contents.Value.EnumerateArray())
                    {
                        var kind = GetContentKind(c);
                        switch (kind)
                        {
                            case "text":
                                var t = c.TryGetProperty("text", out var tp) ? tp.GetString() ?? "" : "";
                                if (!string.IsNullOrEmpty(t))
                                    text = string.IsNullOrEmpty(text) ? t : text + "\n" + t;
                                break;

                            case "functionCall":
                                toolCalls ??= [];
                                var callId = c.TryGetProperty("callId", out var cidProp) ? cidProp.GetString() ?? "" : "";
                                var name = c.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                                var args = c.TryGetProperty("arguments", out var argsProp) ? argsProp.ToString() : null;
                                toolCalls.Add(new ToolCallDto
                                {
                                    ToolCallId = callId,
                                    ToolName = name,
                                    Status = "completed",
                                    Args = args
                                });
                                break;
                        }
                    }
                }

                // Dedup: skip consecutive assistant messages with identical text content
                // (MagneticOne checkpoint state can repeat the same response)
                if (!string.IsNullOrEmpty(text) && text == lastAssistantContent && toolCalls is null)
                    continue;

                messages.Add(new ChatMessageDto
                {
                    Index = index++,
                    Role = "assistant",
                    Content = text,
                    ToolCalls = toolCalls,
                    ParticipantAgentName = authorName
                });

                // Track for dedup
                lastAssistantContent = string.IsNullOrEmpty(text) ? null : text;
                continue;
            }

            // ── fallback for unknown roles ────────────────────────────
            messages.Add(new ChatMessageDto
            {
                Index = index++,
                Role = role,
                Content = ExtractTextContent(contents)
            });
        }

        return messages;
    }

    /// <summary>Extract concatenated text from a contents array.</summary>
    private static string ExtractTextContent(JsonElement? contents)
    {
        if (!contents.HasValue) return "";

        var parts = new List<string>();
        foreach (var c in contents.Value.EnumerateArray())
        {
            if (GetContentKind(c) == "text" && c.TryGetProperty("text", out var tp))
            {
                var t = tp.GetString();
                if (!string.IsNullOrEmpty(t)) parts.Add(t);
            }
        }
        return string.Join("\n", parts);
    }

    /// <summary>Read the "kind" discriminator from a content DTO element.</summary>
    private static string GetContentKind(JsonElement content)
    {
        return content.TryGetProperty("kind", out var kindProp)
            ? kindProp.GetString() ?? "text"
            : "text";
    }

    /// <summary>
    /// Heuristic fallback for older sessions where authorName is not set.
    /// Detects orchestrator-injected user-role messages by content patterns.
    /// </summary>
    private static bool IsOrchestratorInjectedMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        // MagneticOne orchestrator injects prompts containing plan/facts markers
        return text.Contains("## Facts", StringComparison.OrdinalIgnoreCase)
            && text.Contains("## Plan", StringComparison.OrdinalIgnoreCase);
    }
}
