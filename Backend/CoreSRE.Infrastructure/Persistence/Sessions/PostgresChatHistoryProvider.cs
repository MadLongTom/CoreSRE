using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoreSRE.Infrastructure.Persistence.Sessions;

/// <summary>
/// JSONB-safe ChatHistoryProvider for PostgreSQL session storage.
/// 
/// Problem: The framework's InMemoryChatHistoryProvider serializes ChatMessage content using
/// STJ polymorphic "$type" discriminators (e.g. {"text":"hello","$type":"text"}).
/// PostgreSQL JSONB storage reorders object keys alphabetically, and STJ previously required
/// "$type" to be the FIRST property in an object for polymorphic deserialization.
/// 
/// Solution: With <see cref="JsonSerializerOptions.AllowOutOfOrderMetadataProperties"/> = true
/// (available since .NET 9), "$type" position is irrelevant. This allows direct serialization
/// of <see cref="ChatMessage"/> without intermediate DTO mapping, eliminating allocation overhead.
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

        // Apply reducer before returning messages (limits history window for token control)
        if (ChatReducer is not null)
        {
            var reduced = (await ChatReducer.ReduceAsync(state.Messages, cancellationToken)
                .ConfigureAwait(false)).ToList();

            state.Messages = reduced;
            _sessionState.SaveState(context.Session, state);
        }

        return state.Messages;
    }

    /// <inheritdoc />
    protected override ValueTask StoreChatHistoryAsync(
        InvokedContext context, CancellationToken cancellationToken = default)
    {
        var state = _sessionState.GetOrInitializeState(context.Session);

        state.Messages.AddRange(context.RequestMessages);
        if (context.ResponseMessages is { } responses)
            state.Messages.AddRange(responses);

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
            foreach (var c in msg.Contents)
            {
                if (c is FunctionCallContent fc && fc.CallId is not null)
                    existingCallIds.Add(fc.CallId);
                else if (c is FunctionResultContent fr && fr.CallId is not null)
                    existingCallIds.Add(fr.CallId);
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
                state.Messages.Insert(insertIdx, msg);
                injected++;
            }
        }

        if (injected > 0)
            _sessionState.SaveState(session, state);
    }

    /// <summary>Find the position to insert tool messages — before the last assistant text-only message.</summary>
    private static int FindToolInsertionIndex(List<ChatMessage> messages)
    {
        // Walk backwards to find the last assistant message that has ONLY text content (the final answer).
        // Tool messages should be inserted before it.
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            if (msg.Role == ChatRole.Assistant && msg.Contents is { Count: > 0 })
            {
                bool hasOnlyText = msg.Contents.All(c => c is TextContent);
                if (hasOnlyText)
                    return i;
            }
        }
        return messages.Count; // fallback: append at end
    }

    // ── Serialization ────────────────────────────────────────────────

    /// <summary>
    /// Uses <see cref="AIJsonUtilities.DefaultOptions"/> as the base to get full MEAI type support
    /// (ChatMessage, AIContent polymorphic hierarchy, ChatRole converter, etc.).
    /// <see cref="JsonSerializerOptions.AllowOutOfOrderMetadataProperties"/> = true ensures
    /// "$type" discriminators work correctly after PostgreSQL JSONB key reordering.
    /// </summary>
    private static readonly JsonSerializerOptions s_serializerOptions = CreateSerializerOptions();

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(AIJsonUtilities.DefaultOptions)
        {
            AllowOutOfOrderMetadataProperties = true,
            WriteIndented = false,
        };
        return options;
    }

    // ── State type (stores ChatMessage directly — no DTO mapping needed) ─

    /// <summary>Per-session state stored in AgentSession.StateBag.</summary>
    internal sealed class ChatHistoryState
    {
        [JsonPropertyName("messages")]
        public List<ChatMessage> Messages { get; set; } = [];
    }
}
