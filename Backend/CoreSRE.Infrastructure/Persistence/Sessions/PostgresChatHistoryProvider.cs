using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CoreSRE.Infrastructure.Persistence.Sessions;

/// <summary>
/// JSONB-safe ChatHistoryProvider for PostgreSQL session storage.
/// 
/// Problem: The framework's InMemoryChatHistoryProvider serializes ChatMessage content using
/// STJ polymorphic "$type" discriminators (e.g. {"text":"hello","$type":"text"}).
/// PostgreSQL JSONB storage reorders object keys alphabetically, and STJ requires "$type"
/// to be the FIRST property in an object for polymorphic deserialization. This mismatch
/// causes deserialization to fail silently (0 messages) or throw.
/// 
/// Solution: This provider uses a ProviderSessionState with a flat DTO format using explicit
/// "kind" field instead of STJ "$type" metadata. Key ordering is irrelevant for standard
/// property deserialization, making it fully compatible with PostgreSQL JSONB.
/// 
/// Architecture (rc2): Uses ProviderSessionState&lt;State&gt; to store per-session message history
/// in AgentSession.StateBag, following the new singleton-provider pattern. The provider instance
/// is shared across sessions; state is per-session via StateBag.
/// </summary>
public sealed class PostgresChatHistoryProvider : ChatHistoryProvider
{
    private readonly ProviderSessionState<ChatHistoryState> _sessionState;

    /// <summary>Gets the chat reducer used to process or reduce chat messages.</summary>
    public IChatReducer? ChatReducer { get; }

    /// <summary>Create a new provider instance (singleton per agent).</summary>
    public PostgresChatHistoryProvider(IChatReducer? chatReducer = null)
    {
        ChatReducer = chatReducer;
        _sessionState = new ProviderSessionState<ChatHistoryState>(
            _ => new ChatHistoryState(),
            nameof(PostgresChatHistoryProvider),
            s_serializerOptions);
    }

    // ── ChatHistoryProvider overrides (new ProvideChatHistoryAsync / StoreChatHistoryAsync pattern) ──

    /// <inheritdoc />
    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
        var state = _sessionState.GetOrInitializeState(context.Session);
        var messages = state.Messages.Select(DtoToChatMessage).ToList();

        // Apply reducer before returning messages (limits history window for token control)
        if (ChatReducer is not null)
        {
            messages = (await ChatReducer.ReduceAsync(messages, cancellationToken)
                .ConfigureAwait(false)).ToList();

            // Sync reduced messages back to state
            state.Messages = messages.Select(ChatMessageToDto).ToList();
            _sessionState.SaveState(context.Session, state);
        }

        return messages;
    }

    /// <inheritdoc />
    protected override ValueTask StoreChatHistoryAsync(
        InvokedContext context, CancellationToken cancellationToken = default)
    {
        var state = _sessionState.GetOrInitializeState(context.Session);

        var newDtos = context.RequestMessages
            .Concat(context.ResponseMessages ?? [])
            .Select(ChatMessageToDto);

        state.Messages.AddRange(newDtos);
        _sessionState.SaveState(context.Session, state);

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Ensures function call/result messages are persisted in the session state.
    /// Call AFTER RunStreamingAsync and BEFORE SaveSessionAsync.
    ///
    /// FunctionInvokingChatClient may handle function calls internally during streaming
    /// and not include them in the response messages passed to StoreChatHistoryAsync.
    /// This method injects any missing tool messages collected from the streaming output.
    /// </summary>
    public void EnsureToolMessagesStored(AgentSession session, IReadOnlyList<ChatMessage> toolMessages)
    {
        if (toolMessages.Count == 0) return;

        var state = _sessionState.GetOrInitializeState(session);

        // Collect existing function call IDs to avoid duplicates
        var existingCallIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var msg in state.Messages)
        {
            foreach (var c in msg.Contents ?? [])
            {
                if (c.Kind is "functionCall" or "functionResult" && c.CallId is not null)
                    existingCallIds.Add(c.CallId);
            }
        }

        var injected = 0;
        foreach (var msg in toolMessages)
        {
            bool hasNewContent = msg.Contents.Any(c =>
                (c is FunctionCallContent fc && !existingCallIds.Contains(fc.CallId ?? "")) ||
                (c is FunctionResultContent fr && !existingCallIds.Contains(fr.CallId ?? "")));

            if (hasNewContent)
            {
                // Find insertion point: just before the last assistant text-only message
                // to maintain correct chronological order
                var insertIdx = FindToolInsertionIndex(state.Messages);
                state.Messages.Insert(insertIdx, ChatMessageToDto(msg));
                injected++;
            }
        }

        if (injected > 0)
            _sessionState.SaveState(session, state);
    }

    /// <summary>Find the position to insert tool messages — before the last assistant text-only message.</summary>
    private static int FindToolInsertionIndex(List<MessageDto> messages)
    {
        // Walk backwards to find the last assistant message that has ONLY text content (the final answer).
        // Tool messages should be inserted before it.
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            if (msg.Role == "assistant" && msg.Contents is { Count: > 0 })
            {
                bool hasOnlyText = msg.Contents.All(c => c.Kind == "text");
                if (hasOnlyText)
                    return i;
            }
        }
        return messages.Count; // fallback: append at end
    }

    // ── Serialization ────────────────────────────────────────────────

    private static readonly JsonSerializerOptions s_serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    // ── ChatMessage ↔ DTO conversion ─────────────────────────────────

    private static MessageDto ChatMessageToDto(ChatMessage msg)
    {
        return new MessageDto
        {
            Role = msg.Role.Value,
            AuthorName = msg.AuthorName,
            MessageId = msg.MessageId,
            CreatedAt = msg.CreatedAt,
            Source = msg.AdditionalProperties?.TryGetValue("source", out var src) == true
                ? src?.ToString()
                : null,
            Contents = msg.Contents.Select(ContentToDto).ToList(),
        };
    }

    private static ChatMessage DtoToChatMessage(MessageDto dto)
    {
        var role = new ChatRole(dto.Role ?? "user");
        var contents = dto.Contents?.Select(DtoToContent).ToList()
                       ?? new List<AIContent>();

        var msg = new ChatMessage(role, contents)
        {
            AuthorName = dto.AuthorName,
            MessageId = dto.MessageId,
            CreatedAt = dto.CreatedAt,
        };

        if (!string.IsNullOrEmpty(dto.Source))
        {
            msg.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            msg.AdditionalProperties["source"] = dto.Source;
        }

        return msg;
    }

    /// <summary>Convert AIContent to flat DTO with explicit "kind" field (no $type).</summary>
    private static ContentDto ContentToDto(AIContent content)
    {
        return content switch
        {
            FunctionCallContent fc => new ContentDto
            {
                Kind = "functionCall",
                CallId = fc.CallId,
                Name = fc.Name,
                Arguments = fc.Arguments is not null
                    ? JsonSerializer.SerializeToElement(fc.Arguments)
                    : null,
            },
            FunctionResultContent fr => new ContentDto
            {
                Kind = "functionResult",
                CallId = fr.CallId,
                Result = fr.Result is not null
                    ? JsonSerializer.SerializeToElement(fr.Result)
                    : null,
                ExceptionMessage = fr.Exception?.Message,
            },
            TextContent tc => new ContentDto
            {
                Kind = "text",
                Text = tc.Text,
            },
            DataContent dc => new ContentDto
            {
                Kind = "data",
                Uri = dc.Uri?.ToString(),
                MediaType = dc.MediaType,
            },
            // Fallback: preserve as much text as possible
            _ => new ContentDto
            {
                Kind = "text",
                Text = content.ToString(),
            },
        };
    }

    /// <summary>Convert flat DTO back to AIContent using explicit "kind" field.</summary>
    private static AIContent DtoToContent(ContentDto dto)
    {
        return dto.Kind switch
        {
            "functionCall" => new FunctionCallContent(
                dto.CallId ?? "",
                dto.Name ?? "",
                dto.Arguments is { ValueKind: JsonValueKind.Object } args
                    ? args.Deserialize<IDictionary<string, object?>>()
                    : null),
            "functionResult" => new FunctionResultContent(
                dto.CallId ?? "",
                dto.Result is { } res ? (object)res : null),
            "data" => new DataContent(
                dto.Uri is not null ? new Uri(dto.Uri) : new Uri("about:blank"),
                dto.MediaType ?? "application/octet-stream"),
            // Default and "text"
            _ => new TextContent(dto.Text ?? ""),
        };
    }

    // ── State & DTO types (JSONB-safe, no $type discriminator) ────────

    /// <summary>Per-session state stored in AgentSession.StateBag.</summary>
    internal sealed class ChatHistoryState
    {
        [JsonPropertyName("messages")]
        public List<MessageDto> Messages { get; set; } = [];
    }

    internal sealed class MessageDto
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("authorName")]
        public string? AuthorName { get; set; }

        [JsonPropertyName("messageId")]
        public string? MessageId { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTimeOffset? CreatedAt { get; set; }

        /// <summary>Message origin: null = normal, "memory" = injected by semantic memory.</summary>
        [JsonPropertyName("source")]
        public string? Source { get; set; }

        [JsonPropertyName("contents")]
        public List<ContentDto>? Contents { get; set; }
    }

    internal sealed class ContentDto
    {
        /// <summary>
        /// Explicit content type discriminator — replaces STJ "$type" metadata.
        /// Values: "text", "functionCall", "functionResult", "data".
        /// </summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "text";

        // ── Text ──
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        // ── FunctionCall / FunctionResult ──
        [JsonPropertyName("callId")]
        public string? CallId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("arguments")]
        public JsonElement? Arguments { get; set; }

        [JsonPropertyName("result")]
        public JsonElement? Result { get; set; }

        [JsonPropertyName("exceptionMessage")]
        public string? ExceptionMessage { get; set; }

        // ── Data / Binary ──
        [JsonPropertyName("uri")]
        public string? Uri { get; set; }

        [JsonPropertyName("mediaType")]
        public string? MediaType { get; set; }
    }
}
