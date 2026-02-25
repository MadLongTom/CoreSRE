using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// Custom GroupChatManager that uses an LLM to dynamically select the next speaker.
/// Ported from AutoGen Python's SelectorGroupChat logic:
///   1. Build candidate list (optionally exclude previous speaker)
///   2. Construct prompt: role descriptions + conversation history + "select next speaker"
///   3. Call the selector LLM with ResponseFormat=Json, parse the structured JSON response
///   4. Validate name against candidates, retry up to MaxSelectorAttempts times
/// </summary>
public class LlmSelectorGroupChatManager : GroupChatManager
{
    private readonly IReadOnlyList<AIAgent> _agents;
    private readonly IChatClient _selectorClient;
    private readonly string _selectorPrompt;
    private readonly bool _allowRepeatedSpeaker;
    private readonly int _maxSelectorAttempts;
    private string? _previousSpeaker;

    private const string DefaultSelectorPrompt =
        """
        You are in a role play game. The following roles are available:
        {roles}

        Read the following conversation. Then select the next role from [{participants}] to play.
        Respond with a JSON object: { "next_speaker": "<role_name>" }
        """;

    /// <summary>JSON schema for the selector LLM response.</summary>
    private static readonly JsonElement SelectorResponseSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "next_speaker": { "type": "string", "description": "The exact name of the next agent to speak" }
          },
          "required": ["next_speaker"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public LlmSelectorGroupChatManager(
        IReadOnlyList<AIAgent> agents,
        IChatClient selectorClient,
        string? selectorPrompt = null,
        bool allowRepeatedSpeaker = true,
        int maxSelectorAttempts = 3)
    {
        _agents = agents ?? throw new ArgumentNullException(nameof(agents));
        _selectorClient = selectorClient ?? throw new ArgumentNullException(nameof(selectorClient));
        _selectorPrompt = selectorPrompt ?? DefaultSelectorPrompt;
        _allowRepeatedSpeaker = allowRepeatedSpeaker;
        _maxSelectorAttempts = maxSelectorAttempts > 0 ? maxSelectorAttempts : 3;
    }

    protected override async ValueTask<AIAgent> SelectNextAgentAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken)
    {
        if (_agents.Count == 0)
            throw new InvalidOperationException("No agents available for selection.");

        if (_agents.Count == 1)
        {
            _previousSpeaker = _agents[0].Name;
            return _agents[0];
        }

        // 1. Build candidate list (exclude previous speaker if not allowed)
        var candidates = _allowRepeatedSpeaker || _previousSpeaker is null
            ? _agents.ToList()
            : _agents.Where(a => !string.Equals(a.Name, _previousSpeaker, StringComparison.OrdinalIgnoreCase)).ToList();

        if (candidates.Count == 0)
            candidates = _agents.ToList(); // Safety fallback

        if (candidates.Count == 1)
        {
            _previousSpeaker = candidates[0].Name;
            return candidates[0];
        }

        // 2. Build the role descriptions and participant list
        var roles = string.Join("\n", _agents.Select(a =>
            $"- {a.Name}: {(string.IsNullOrWhiteSpace(a.Description) ? "No description" : a.Description)}"));
        var participantNames = string.Join(", ", candidates.Select(a => a.Name));

        // 3. Construct the prompt
        var prompt = _selectorPrompt
            .Replace("{roles}", roles)
            .Replace("{participants}", participantNames);

        // 4. Build conversation for the selector LLM
        var selectorMessages = new List<ChatMessage>
        {
            new(ChatRole.System, prompt)
        };

        // Include recent conversation history (last 20 messages to control token usage)
        var recentHistory = history.Count > 20 ? history.Skip(history.Count - 20) : history;
        foreach (var msg in recentHistory)
        {
            var role = msg.Role == ChatRole.Assistant ? ChatRole.Assistant : ChatRole.User;
            var authorPrefix = msg.AuthorName is not null ? $"[{msg.AuthorName}] " : "";
            var text = msg.Text ?? "";
            selectorMessages.Add(new ChatMessage(role, $"{authorPrefix}{text}"));
        }

        selectorMessages.Add(new ChatMessage(ChatRole.User,
            $"Select the next role from [{participantNames}] to play. " +
            $"Respond with JSON: {{\"next_speaker\": \"<name>\"}}"));

        // 5. Call the selector LLM with JSON response format and retries
        var chatOptions = new ChatOptions
        {
            ResponseFormat = ChatResponseFormat.ForJsonSchema(SelectorResponseSchema)
        };

        for (var attempt = 0; attempt < _maxSelectorAttempts; attempt++)
        {
            var response = await _selectorClient.GetResponseAsync(
                selectorMessages, chatOptions, cancellationToken);

            var text = response.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            // Parse structured JSON response
            var selectedName = ParseSelectorResponse(text);
            if (selectedName is null)
                continue;

            // Match against candidates
            var matched = MatchAgent(selectedName, candidates);
            if (matched is not null)
            {
                _previousSpeaker = matched.Name;
                return matched;
            }

            // Retry: tell the LLM the name was invalid
            selectorMessages.Add(new ChatMessage(ChatRole.Assistant, text));
            selectorMessages.Add(new ChatMessage(ChatRole.User,
                $"'{selectedName}' is not a valid participant. " +
                $"Choose from [{participantNames}]. " +
                $"Respond with JSON: {{\"next_speaker\": \"<name>\"}}"));
        }

        // All attempts failed — deterministic fallback
        var fallback = candidates.FirstOrDefault(a =>
            !string.Equals(a.Name, _previousSpeaker, StringComparison.OrdinalIgnoreCase)) ?? candidates[0];
        _previousSpeaker = fallback.Name;
        return fallback;
    }

    /// <summary>Parse the selector LLM JSON response to extract next_speaker.</summary>
    private static string? ParseSelectorResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("next_speaker", out var prop))
                return prop.GetString()?.Trim();
        }
        catch
        {
            // Not valid JSON — ignore
        }
        return null;
    }

    /// <summary>Match a name string against the candidate list (exact, then contains).</summary>
    private static AIAgent? MatchAgent(string name, List<AIAgent> candidates)
    {
        // Exact match
        var matched = candidates.FirstOrDefault(a =>
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

        // Contains match (LLM may return a slightly different casing or extra text)
        matched ??= candidates.FirstOrDefault(a =>
            name.Contains(a.Name!, StringComparison.OrdinalIgnoreCase));

        return matched;
    }
}
