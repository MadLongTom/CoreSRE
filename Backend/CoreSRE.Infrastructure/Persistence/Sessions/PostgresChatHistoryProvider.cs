using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;

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
/// Solution: This provider uses its own flat DTO format with explicit "kind" field instead
/// of STJ "$type" metadata. Key ordering is irrelevant for standard property deserialization,
/// making it fully compatible with PostgreSQL JSONB.
/// </summary>
public sealed class PostgresChatHistoryProvider : ChatHistoryProvider, IList<ChatMessage>, IReadOnlyList<ChatMessage>
{
    private List<ChatMessage> _messages;

    /// <summary>Gets the chat reducer used to process or reduce chat messages.</summary>
    public IChatReducer? ChatReducer { get; }

    /// <summary>Create a new provider for a fresh session (no existing history).</summary>
    public PostgresChatHistoryProvider(IChatReducer? chatReducer = null)
    {
        ChatReducer = chatReducer;
        _messages = [];
    }

    /// <summary>Restore a provider from previously serialized state (JSONB-safe format).</summary>
    public PostgresChatHistoryProvider(IChatReducer? chatReducer, JsonElement serializedState)
    {
        ChatReducer = chatReducer;
        _messages = DeserializeMessages(serializedState);
    }

    // ── ChatHistoryProvider abstract members ─────────────────────────

    /// <inheritdoc />
    protected override async ValueTask<IEnumerable<ChatMessage>> InvokingCoreAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
        // Apply reducer before returning messages (limits history window for token control)
        if (ChatReducer is not null)
        {
            _messages = (await ChatReducer.ReduceAsync(_messages, cancellationToken)
                .ConfigureAwait(false)).ToList();
        }

        return _messages;
    }

    /// <inheritdoc />
    protected override ValueTask InvokedCoreAsync(
        InvokedContext context, CancellationToken cancellationToken = default)
    {
        if (context.InvokeException is not null)
            return ValueTask.CompletedTask;

        // Mirror InMemoryChatHistoryProvider: store both request and response messages
        var allNewMessages = context.RequestMessages.Concat(context.ResponseMessages ?? []);
        _messages.AddRange(allNewMessages);

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Serialize using our own JSONB-safe DTO format.
    /// Uses explicit "kind" field instead of STJ "$type" discriminator.
    /// Key ordering by PostgreSQL JSONB does not affect correctness.
    /// </summary>
    public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        var dtos = _messages.Select(ChatMessageToDto).ToList();
        var state = new ProviderState { Messages = dtos };
        return JsonSerializer.SerializeToElement(state, s_serializerOptions);
    }

    // ── IList<ChatMessage> / IReadOnlyList<ChatMessage> ──────────────

    /// <inheritdoc />
    public int Count => _messages.Count;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public ChatMessage this[int index]
    {
        get => _messages[index];
        set => _messages[index] = value;
    }

    /// <inheritdoc />
    public int IndexOf(ChatMessage item) => _messages.IndexOf(item);
    /// <inheritdoc />
    public void Insert(int index, ChatMessage item) => _messages.Insert(index, item);
    /// <inheritdoc />
    public void RemoveAt(int index) => _messages.RemoveAt(index);
    /// <inheritdoc />
    public void Add(ChatMessage item) => _messages.Add(item);
    /// <inheritdoc />
    public void Clear() => _messages.Clear();
    /// <inheritdoc />
    public bool Contains(ChatMessage item) => _messages.Contains(item);
    /// <inheritdoc />
    public void CopyTo(ChatMessage[] array, int arrayIndex) => _messages.CopyTo(array, arrayIndex);
    /// <inheritdoc />
    public bool Remove(ChatMessage item) => _messages.Remove(item);
    /// <inheritdoc />
    public IEnumerator<ChatMessage> GetEnumerator() => _messages.GetEnumerator();
    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // ── Serialization ────────────────────────────────────────────────

    private static readonly JsonSerializerOptions s_serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    /// <summary>Deserialize from our JSONB-safe format back to ChatMessages.</summary>
    private static List<ChatMessage> DeserializeMessages(JsonElement serializedState)
    {
        if (serializedState.ValueKind != JsonValueKind.Object)
            return [];

        try
        {
            var state = JsonSerializer.Deserialize<ProviderState>(serializedState, s_serializerOptions);
            if (state?.Messages is not { Count: > 0 } dtos)
                return [];

            return dtos.Select(DtoToChatMessage).ToList();
        }
        catch
        {
            // Old format (InMemoryChatHistoryProvider with $type) or corrupted data — start fresh.
            // The next save will persist in the new format.
            return [];
        }
    }

    // ── ChatMessage ↔ DTO conversion ─────────────────────────────────

    private static MessageDto ChatMessageToDto(ChatMessage msg)
    {
        return new MessageDto
        {
            Role = msg.Role.Value,
            AuthorName = msg.AuthorName,
            MessageId = msg.MessageId,
            CreatedAt = msg.CreatedAt,
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

    // ── DTO types (JSONB-safe, no $type discriminator) ────────────────

    private sealed class ProviderState
    {
        [JsonPropertyName("messages")]
        public List<MessageDto> Messages { get; set; } = [];
    }

    private sealed class MessageDto
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("authorName")]
        public string? AuthorName { get; set; }

        [JsonPropertyName("messageId")]
        public string? MessageId { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTimeOffset? CreatedAt { get; set; }

        [JsonPropertyName("contents")]
        public List<ContentDto>? Contents { get; set; }
    }

    private sealed class ContentDto
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
