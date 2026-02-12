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
/// SessionData JSON 结构：{ chatHistoryProviderState: { messages: [{ role, contents: [{ text, type }] }] } }
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
    /// 路径：chatHistoryProviderState.messages[].{role, contents[]}
    ///
    /// 处理策略：
    /// 1. system + source=memory → 提取内容，附加到下一条 user 消息的 MemoryContext
    /// 2. system (非 memory) → 跳过
    /// 3. assistant → 提取文本 + functionCall 内容 → 输出一条带 toolCalls 的消息
    /// 4. tool → 将 functionResult 匹配回前一条 assistant 的 toolCalls
    /// 5. user → 正常输出，并附加待处理的 memory context
    /// </summary>
    private static List<ChatMessageDto> ExtractMessages(JsonElement sessionData)
    {
        var messages = new List<ChatMessageDto>();

        if (!sessionData.TryGetProperty("chatHistoryProviderState", out var historyState))
            return messages;

        if (!historyState.TryGetProperty("messages", out var messagesArray))
            return messages;

        var index = 0;
        string? pendingMemory = null; // memory context waiting to attach to next user message

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
                var text = ExtractTextContent(contents);
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

                messages.Add(new ChatMessageDto
                {
                    Index = index++,
                    Role = "assistant",
                    Content = text,
                    ToolCalls = toolCalls
                });
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
}
