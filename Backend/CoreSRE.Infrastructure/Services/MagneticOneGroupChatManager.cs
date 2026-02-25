using CoreSRE.Application.Chat.DTOs;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// Custom GroupChatManager implementing the MagneticOne dual-loop ledger pattern,
/// ported from AutoGen Python's MagenticOneOrchestrator:
///
/// Outer Loop (Task Ledger):
///   1. Gather facts (GIVEN / TO LOOK UP / TO DERIVE / GUESS) — JSON schema enforced
///   2. Generate plan (numbered steps with agent assignments) — JSON schema enforced
///   3. When stalled: update facts → update plan → reset → restart
///
/// Inner Loop (Progress Ledger):
///   Each step: orchestrator LLM evaluates progress → JSON schema enforced:
///   { is_request_satisfied, is_in_loop, is_progress_being_made, next_speaker, instruction_or_question }
///   → stall detection: no progress / loop → n_stalls++
///   → n_stalls >= max_stalls → re-enter outer loop for replanning
///
/// All LLM calls use ResponseFormat = ForJsonSchema to guarantee structured output.
/// </summary>
public class MagneticOneGroupChatManager : GroupChatManager
{
    private readonly IReadOnlyList<AIAgent> _agents;
    private readonly IChatClient _orchestratorClient;
    private readonly int _maxStalls;
    private readonly string? _finalAnswerPrompt;
    private readonly int _maxLedgerAttempts;
    private readonly ILogger? _logger;

    // Outer loop state
    private string _task = "";
    private string _facts = "";
    private string _plan = "";
    private bool _needsOuterLoop = true;

    // Inner loop state
    private int _nStalls;
    private ProgressLedger? _currentLedger;

    // Replan windowing: messages before this index are condensed after stall recovery (#1)
    private int _historyWindowStart;

    /// <summary>Outer ledger — high-level plan and progress tracking (for SSE push).</summary>
    public OuterLedger OuterLedger { get; } = new();

    /// <summary>Synthesized final answer (generated when task is satisfied).</summary>
    public string? FinalAnswer { get; private set; }

    /// <summary>Inner ledger — per-agent task execution log.</summary>
    public List<InnerLedgerEntry> InnerLedgerEntries { get; } = [];

    /// <summary>
    /// Callback for emitting TEAM_LEDGER_UPDATE SSE events.
    /// Set by HandleTeamStreamAsync to propagate ledger changes to the frontend.
    /// </summary>
    public Action<TeamLedgerUpdateEventDto>? OnLedgerUpdate { get; set; }

    public MagneticOneGroupChatManager(
        IReadOnlyList<AIAgent> agents,
        IChatClient orchestratorClient,
        int maxStalls = 3,
        string? finalAnswerPrompt = null,
        int maxLedgerAttempts = 3,
        ILogger? logger = null)
    {
        _agents = agents ?? throw new ArgumentNullException(nameof(agents));
        _orchestratorClient = orchestratorClient ?? throw new ArgumentNullException(nameof(orchestratorClient));
        _maxStalls = maxStalls > 0 ? maxStalls : 3;
        _finalAnswerPrompt = finalAnswerPrompt;
        _maxLedgerAttempts = maxLedgerAttempts > 0 ? maxLedgerAttempts : 3;
        _nStalls = 0;
        _logger = logger;
    }

    // ── JSON Schemas ───────────────────────────────────────────────────

    /// <summary>Schema for facts gathering / update responses.</summary>
    private static readonly JsonElement FactsSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "given": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Facts clearly stated in the request or conversation"
            },
            "to_look_up": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Facts that need to be verified or researched"
            },
            "to_derive": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Facts that can be inferred from existing information"
            },
            "guess": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Uncertain assumptions being made"
            }
          },
          "required": ["given", "to_look_up", "to_derive", "guess"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    /// <summary>Schema for plan generation / update responses.</summary>
    private static readonly JsonElement PlanSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "steps": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "step_number": { "type": "integer" },
                  "description": { "type": "string" },
                  "assigned_agent": { "type": "string" }
                },
                "required": ["step_number", "description", "assigned_agent"],
                "additionalProperties": false
              },
              "description": "Ordered list of plan steps"
            }
          },
          "required": ["steps"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    /// <summary>Schema for progress ledger evaluation responses.</summary>
    private static readonly JsonElement ProgressLedgerSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "is_request_satisfied": { "type": "boolean", "description": "Whether the original request has been fully addressed" },
            "is_request_satisfied_reason": { "type": "string", "description": "Explanation for the satisfaction decision" },
            "is_in_loop": { "type": "boolean", "description": "Whether the team is repeating the same actions" },
            "is_progress_being_made": { "type": "boolean", "description": "Whether meaningful progress is being made" },
            "next_speaker": { "type": "string", "description": "The exact name of the next agent that should act" },
            "instruction_or_question": { "type": "string", "description": "The instruction or question to give to the next speaker" }
          },
          "required": ["is_request_satisfied", "is_request_satisfied_reason", "is_in_loop", "is_progress_being_made", "next_speaker", "instruction_or_question"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    // ── Prompt Templates ───────────────────────────────────────────────

    private const string TaskLedgerFactsPrompt =
        """
        Below I will present you a request and a history of attempts at addressing that request.
        Analyze the conversation and categorize the facts.

        Team composition:
        {team_description}

        Original request:
        {task}

        Conversation so far:
        {history_summary}

        Respond with a JSON object categorizing facts into: given, to_look_up, to_derive, guess.
        Each category is an array of strings.
        """;

    private const string TaskLedgerPlanPrompt =
        """
        Based on the following facts and team composition, create a step-by-step plan.
        Each step should indicate which team member is best suited.

        Team members:
        {team_description}

        Facts:
        {facts}

        Original request:
        {task}

        Respond with a JSON object containing a "steps" array of objects with step_number, description, and assigned_agent.
        """;

    private const string ProgressLedgerPrompt =
        """
        You are the orchestrator of a multi-agent team. Evaluate the current progress and decide the next action.

        Team members:
        {team_description}

        Original request:
        {task}

        Current plan:
        {plan}

        Current facts:
        {facts}

        Recent conversation:
        {recent_history}

        Available agent names: [{agent_names}]

        Respond with a JSON object containing:
        - is_request_satisfied (boolean): whether the original request has been fully addressed.
          IMPORTANT: Set this to TRUE in any of these cases:
          • The user's request was a greeting, introduction, or casual conversation — AND at least one agent has already responded.
          • An agent has asked a clarifying question or is waiting for user input to proceed — the current turn is complete.
          • The task has been answered or completed to the best of the team's ability.
          Do NOT continue routing to other agents when the conversation requires the human user to provide more information.
        - is_request_satisfied_reason (string): explanation for the satisfaction decision
        - is_in_loop (boolean): whether the team is repeating the same actions without progress
        - is_progress_being_made (boolean): whether meaningful progress is being made toward answering the request
        - next_speaker (string): the exact name of the next agent from the available list (only relevant if is_request_satisfied is false)
        - instruction_or_question (string): the instruction to give to the next speaker (only relevant if is_request_satisfied is false)
        """;

    private const string FactsUpdatePrompt =
        """
        The team has been working but appears to be stalled. Update the facts with new information.

        Current facts:
        {facts}

        Conversation so far:
        {recent_history}

        Respond with a JSON object categorizing updated facts into: given, to_look_up, to_derive, guess.
        Remove facts that have been resolved and add new insights.
        """;

    private const string PlanUpdatePrompt =
        """
        The team appears stalled. Create a revised plan avoiding approaches that have not been working.

        Previous plan:
        {plan}

        Current facts:
        {facts}

        Team members:
        {team_description}

        Original request:
        {task}

        Respond with a JSON object containing a "steps" array with revised plan steps.
        Focus on alternative strategies and different team member assignments.
        """;

    private const string DefaultFinalAnswerPrompt =
        """
        We are working to address the following user request:

        {task}

        The team has completed its work. Based on the entire conversation above,
        provide a clear and concise final answer that directly addresses the user's
        original request. Respond with the answer only — no extra explanation or
        meta-commentary needed.
        """;

    // ── Lifecycle Overrides ────────────────────────────────────────────

    protected override async ValueTask<bool> ShouldTerminateAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation(
            "[MagneticOne] ShouldTerminateAsync — iteration={Iter}, isRequestSatisfied={Satisfied}, historyCount={History}",
            IterationCount, _currentLedger?.IsRequestSatisfied, history.Count);

        if (_currentLedger?.IsRequestSatisfied == true)
        {
            _logger?.LogInformation("[MagneticOne] TERMINATING — reason: {Reason}", _currentLedger.IsRequestSatisfiedReason);

            // Generate final answer synthesis (#2 — ported from AutoGen _prepare_final_answer)
            try
            {
                _logger?.LogInformation("[MagneticOne] Generating final answer synthesis...");
                FinalAnswer = await GenerateFinalAnswerAsync(history, cancellationToken);
                _logger?.LogInformation("[MagneticOne] Final answer generated ({Len} chars)", FinalAnswer?.Length ?? 0);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[MagneticOne] Final answer generation failed, continuing without synthesis");
            }

            OuterLedger.IsComplete = true;
            OuterLedger.FinalAnswer = FinalAnswer;
            OuterLedger.Progress = "Request satisfied. " + _currentLedger.IsRequestSatisfiedReason;
            EmitOuterLedgerUpdate();
            return true;
        }

        return await base.ShouldTerminateAsync(history, cancellationToken);
    }

    protected override ValueTask<IEnumerable<ChatMessage>> UpdateHistoryAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        if (_currentLedger is not null && !string.IsNullOrWhiteSpace(_currentLedger.InstructionOrQuestion))
        {
            var instruction = new ChatMessage(ChatRole.User, _currentLedger.InstructionOrQuestion)
            {
                AuthorName = "Orchestrator"
            };
            return ValueTask.FromResult<IEnumerable<ChatMessage>>(history.Append(instruction));
        }

        return ValueTask.FromResult<IEnumerable<ChatMessage>>(history);
    }

    protected override void Reset()
    {
        base.Reset();
        _task = "";
        _facts = "";
        _plan = "";
        _nStalls = 0;
        _needsOuterLoop = true;
        _currentLedger = null;
        _historyWindowStart = 0;
        FinalAnswer = null;
    }

    // ── Core Selection Logic ───────────────────────────────────────────

    protected override async ValueTask<AIAgent> SelectNextAgentAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger?.LogInformation(
            "[MagneticOne] SelectNextAgentAsync START — iteration={Iter}, historyCount={History}, needsOuterLoop={NeedsOuter}, nStalls={Stalls}",
            IterationCount, history.Count, _needsOuterLoop, _nStalls);

        if (_agents.Count == 0)
            throw new InvalidOperationException("No agents available for MagneticOne orchestration.");

        // Outer loop: gather facts and create a plan
        if (_needsOuterLoop)
        {
            _task = ExtractTask(history);

            _logger?.LogInformation("[MagneticOne] OUTER LOOP — gathering facts...");
            if (string.IsNullOrWhiteSpace(_facts))
                _facts = await GatherFactsAsync(history, cancellationToken);
            else
                _facts = await UpdateFactsAsync(history, cancellationToken);
            _logger?.LogInformation("[MagneticOne] Facts gathered ({Len} chars)", _facts?.Length ?? 0);

            _logger?.LogInformation("[MagneticOne] OUTER LOOP — creating plan...");
            if (string.IsNullOrWhiteSpace(_plan))
                _plan = await CreatePlanAsync(cancellationToken);
            else
                _plan = await UpdatePlanAsync(cancellationToken);
            _logger?.LogInformation("[MagneticOne] Plan created ({Len} chars)", _plan?.Length ?? 0);

            OuterLedger.Facts = _facts;
            OuterLedger.Plan = _plan;
            EmitOuterLedgerUpdate();

            _needsOuterLoop = false;
            _nStalls = 0;
        }

        // Inner loop: evaluate progress ledger
        _logger?.LogInformation("[MagneticOne] Evaluating progress ledger...");
        _currentLedger = await EvaluateProgressLedgerAsync(history, cancellationToken);
        _logger?.LogInformation(
            "[MagneticOne] Ledger result — satisfied={Satisfied}, inLoop={InLoop}, progress={Progress}, nextSpeaker={Speaker}, instruction={Inst}",
            _currentLedger.IsRequestSatisfied, _currentLedger.IsInLoop,
            _currentLedger.IsProgressBeingMade, _currentLedger.NextSpeaker,
            _currentLedger.InstructionOrQuestion?.Length > 80
                ? _currentLedger.InstructionOrQuestion[..80] + "..."
                : _currentLedger.InstructionOrQuestion);

        // Stall detection
        if (!_currentLedger.IsProgressBeingMade || _currentLedger.IsInLoop)
            _nStalls++;
        else
            _nStalls = Math.Max(0, _nStalls - 1);

        // If stalled beyond threshold, trigger replanning (#1: capture window start for history trimming)
        if (_nStalls >= _maxStalls)
        {
            _historyWindowStart = history.Count;
            _logger?.LogInformation(
                "[MagneticOne] Stall threshold reached — setting history window start to {WindowStart} for replan",
                _historyWindowStart);
            OuterLedger.Progress = $"Stalled after {_nStalls} non-productive iterations. Triggering replan.";
            EmitOuterLedgerUpdate();
            _needsOuterLoop = true;
        }

        // Update outer ledger
        OuterLedger.Iteration = IterationCount + 1;
        OuterLedger.NextStep = _currentLedger.InstructionOrQuestion;
        if (!OuterLedger.IsComplete)
            OuterLedger.Progress = $"Step {IterationCount + 1}: Selecting {_currentLedger.NextSpeaker}";
        EmitOuterLedgerUpdate();

        // Emit orchestrator instruction as a separate "orchestrator" ledger event
        // so the frontend can show the orchestrator's reasoning per step
        if (!string.IsNullOrWhiteSpace(_currentLedger.InstructionOrQuestion))
        {
            var orchestratorContent = JsonSerializer.Serialize(new
            {
                iteration = IterationCount + 1,
                targetAgent = _currentLedger.NextSpeaker,
                instruction = _currentLedger.InstructionOrQuestion,
                isRequestSatisfied = _currentLedger.IsRequestSatisfied,
                isProgressBeingMade = _currentLedger.IsProgressBeingMade,
                isInLoop = _currentLedger.IsInLoop,
                reason = _currentLedger.IsRequestSatisfiedReason
            });
            var orchestratorDto = new TeamLedgerUpdateEventDto(
                "orchestrator",
                _currentLedger.NextSpeaker,
                orchestratorContent);
            OnLedgerUpdate?.Invoke(orchestratorDto);
        }

        // Complete previous iteration's inner ledger entry (handles same-agent continuations)
        if (InnerLedgerEntries.Count > 0)
        {
            var lastEntry = InnerLedgerEntries[^1];
            if (lastEntry.Status == "running")
                CompleteInnerLedgerEntry(lastEntry.AgentName);
        }

        // Log new inner ledger entry for this iteration
        var entry = new InnerLedgerEntry(
            _currentLedger.NextSpeaker,
            _currentLedger.InstructionOrQuestion,
            "running",
            null,
            DateTime.UtcNow);
        InnerLedgerEntries.Add(entry);
        EmitInnerLedgerUpdate(entry);

        var selected = FindAgent(_currentLedger.NextSpeaker);
        sw.Stop();
        _logger?.LogInformation(
            "[MagneticOne] SelectNextAgentAsync DONE in {Elapsed}ms — selected={AgentName}({AgentId})",
            sw.ElapsedMilliseconds, selected.Name, selected.Id);
        return selected;
    }

    // ── Outer Loop Helpers ─────────────────────────────────────────────

    private static string ExtractTask(IReadOnlyList<ChatMessage> history)
    {
        // Use the LAST actual user message (not orchestrator-injected ones)
        // so that multi-turn conversations always reference the latest request.
        var userMsg = history.LastOrDefault(m =>
            m.Role == ChatRole.User
            && !string.Equals(m.AuthorName, "Orchestrator", StringComparison.OrdinalIgnoreCase));
        return userMsg?.Text ?? "No task specified.";
    }

    private string GetTeamDescription()
    {
        return string.Join("\n", _agents.Select(a =>
            $"- {a.Name}: {(string.IsNullOrWhiteSpace(a.Description) ? "A team member" : a.Description)}"));
    }

    private string GetHistorySummary(IReadOnlyList<ChatMessage> history, int maxMessages = 30)
    {
        // Filter out tool call messages (#4: match AutoGen's _thread_to_context filtering)
        static bool IsContentMessage(ChatMessage m)
        {
            // Skip tool result messages
            if (m.Role == ChatRole.Tool) return false;
            // Skip pure function call requests (assistant messages with only FunctionCallContent)
            if (m.Contents.Count > 0 && m.Contents.All(c => c is FunctionCallContent)) return false;
            return true;
        }

        static string FormatMessage(ChatMessage m)
        {
            var author = m.AuthorName ?? m.Role.ToString();
            return $"[{author}]: {m.Text ?? "(no text)"}";
        }

        // After stall replan, condense old history into task summary + recent messages only (#1)
        if (_historyWindowStart > 0 && history.Count > _historyWindowStart)
        {
            var recentMessages = history.Skip(_historyWindowStart).Where(IsContentMessage);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[Task]: {_task}");
            if (!string.IsNullOrWhiteSpace(_facts))
                sb.AppendLine($"[Facts Summary]: {(_facts.Length > 500 ? _facts[..500] + "..." : _facts)}");
            if (!string.IsNullOrWhiteSpace(_plan))
                sb.AppendLine($"[Plan Summary]: {(_plan.Length > 500 ? _plan[..500] + "..." : _plan)}");
            sb.AppendLine("--- (messages since replan) ---");
            foreach (var m in recentMessages)
                sb.AppendLine(FormatMessage(m));
            return sb.ToString().TrimEnd();
        }

        var filtered = history.Where(IsContentMessage).ToList();
        var window = filtered.Count > maxMessages ? filtered.Skip(filtered.Count - maxMessages) : filtered;
        return string.Join("\n", window.Select(FormatMessage));
    }

    private async Task<string> GatherFactsAsync(IReadOnlyList<ChatMessage> history, CancellationToken ct)
    {
        var prompt = TaskLedgerFactsPrompt
            .Replace("{team_description}", GetTeamDescription())
            .Replace("{task}", _task)
            .Replace("{history_summary}", GetHistorySummary(history));

        var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };
        var options = new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(FactsSchema) };
        var response = await _orchestratorClient.GetResponseAsync(messages, options, ct);
        var raw = response.Text ?? "{}";
        EmitThought("facts", raw);
        return FormatFacts(raw);
    }

    private async Task<string> CreatePlanAsync(CancellationToken ct)
    {
        var prompt = TaskLedgerPlanPrompt
            .Replace("{team_description}", GetTeamDescription())
            .Replace("{facts}", _facts)
            .Replace("{task}", _task);

        var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };
        var options = new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(PlanSchema) };
        var response = await _orchestratorClient.GetResponseAsync(messages, options, ct);
        var raw = response.Text ?? "{}";
        EmitThought("plan", raw);
        return FormatPlan(raw);
    }

    private async Task<string> UpdateFactsAsync(IReadOnlyList<ChatMessage> history, CancellationToken ct)
    {
        var prompt = FactsUpdatePrompt
            .Replace("{facts}", _facts)
            .Replace("{recent_history}", GetHistorySummary(history));

        var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };
        var options = new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(FactsSchema) };
        var response = await _orchestratorClient.GetResponseAsync(messages, options, ct);
        var raw = response.Text ?? "{}";
        EmitThought("facts_update", raw);
        return FormatFacts(raw);
    }

    private async Task<string> UpdatePlanAsync(CancellationToken ct)
    {
        var prompt = PlanUpdatePrompt
            .Replace("{plan}", _plan)
            .Replace("{facts}", _facts)
            .Replace("{team_description}", GetTeamDescription())
            .Replace("{task}", _task);

        var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };
        var options = new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(PlanSchema) };
        var response = await _orchestratorClient.GetResponseAsync(messages, options, ct);
        var raw = response.Text ?? "{}";
        EmitThought("plan_update", raw);
        return FormatPlan(raw);
    }

    // ── Inner Loop: Progress Ledger Evaluation ─────────────────────────

    private async Task<ProgressLedger> EvaluateProgressLedgerAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct)
    {
        var agentNames = string.Join(", ", _agents.Select(a => a.Name));
        var prompt = ProgressLedgerPrompt
            .Replace("{team_description}", GetTeamDescription())
            .Replace("{task}", _task)
            .Replace("{plan}", _plan)
            .Replace("{facts}", _facts)
            .Replace("{recent_history}", GetHistorySummary(history))
            .Replace("{agent_names}", agentNames);

        var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };
        var options = new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(ProgressLedgerSchema) };

        for (var attempt = 0; attempt < _maxLedgerAttempts; attempt++)
        {
            var response = await _orchestratorClient.GetResponseAsync(messages, options, ct);
            var text = response.Text ?? "";
            _logger?.LogDebug("[MagneticOne] Progress ledger raw LLM response (attempt {Attempt}): {Text}", attempt + 1, text.Length > 500 ? text[..500] + "..." : text);

            var ledger = ParseProgressLedger(text);
            if (ledger is not null)
            {
                EmitThought("progress_ledger", text);
                // Validate and fix next_speaker
                ledger = ValidateNextSpeaker(ledger);
                return ledger;
            }

            // Retry with error feedback
            messages.Add(new ChatMessage(ChatRole.Assistant, text));
            messages.Add(new ChatMessage(ChatRole.User,
                $"Your response could not be parsed. Return a valid JSON object with: " +
                $"is_request_satisfied (bool), is_request_satisfied_reason (string), " +
                $"is_in_loop (bool), is_progress_being_made (bool), " +
                $"next_speaker (string, one of [{agentNames}]), instruction_or_question (string)."));
        }

        // All attempts failed — safe default
        return new ProgressLedger
        {
            IsRequestSatisfied = false,
            IsRequestSatisfiedReason = "Failed to parse progress ledger from LLM.",
            IsInLoop = false,
            IsProgressBeingMade = true,
            NextSpeaker = _agents[0].Name ?? "Unknown",
            InstructionOrQuestion = "Continue working on the task."
        };
    }

    /// <summary>Strip markdown code-block wrappers (```json ... ```) from LLM output.</summary>
    private static string ExtractJson(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0) text = text[(firstNewline + 1)..];
            if (text.EndsWith("```")) text = text[..^3];
            text = text.Trim();
        }
        return text;
    }

    /// <summary>Parse a flat JSON progress ledger response.</summary>
    private static ProgressLedger? ParseProgressLedger(string json)
    {
        try
        {
            json = ExtractJson(json);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new ProgressLedger
            {
                IsRequestSatisfied = root.GetProperty("is_request_satisfied").GetBoolean(),
                IsRequestSatisfiedReason = root.GetProperty("is_request_satisfied_reason").GetString() ?? "",
                IsInLoop = root.GetProperty("is_in_loop").GetBoolean(),
                IsProgressBeingMade = root.GetProperty("is_progress_being_made").GetBoolean(),
                NextSpeaker = root.GetProperty("next_speaker").GetString() ?? "",
                InstructionOrQuestion = root.GetProperty("instruction_or_question").GetString() ?? "Continue."
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Validate that next_speaker refers to a real agent, fix if needed.</summary>
    private ProgressLedger ValidateNextSpeaker(ProgressLedger ledger)
    {
        var exactMatch = _agents.Any(a =>
            string.Equals(a.Name, ledger.NextSpeaker, StringComparison.OrdinalIgnoreCase));
        if (exactMatch) return ledger;

        // Try fuzzy match
        var fuzzy = _agents.FirstOrDefault(a =>
            ledger.NextSpeaker.Contains(a.Name!, StringComparison.OrdinalIgnoreCase));

        return ledger with { NextSpeaker = fuzzy?.Name ?? _agents[0].Name ?? "Unknown" };
    }

    // ── Final Answer Synthesis (#2) ────────────────────────────────────

    /// <summary>
    /// Build a filtered conversation context for LLM calls, excluding tool call events.
    /// Mirrors AutoGen's _thread_to_context() which skips ToolCallRequestEvent and ToolCallExecutionEvent.
    ///
    /// IMPORTANT: The Anthropic API requires every tool_use block to be followed immediately
    /// by a tool_result message. Simply skipping tool-role messages would leave orphaned
    /// tool_use blocks and cause HTTP 400. This method strips FunctionCallContent from
    /// assistant messages (keeping any co-located text) and drops FunctionResultContent /
    /// tool-role messages entirely.
    /// </summary>
    private static List<ChatMessage> BuildFilteredContext(IReadOnlyList<ChatMessage> history)
    {
        var context = new List<ChatMessage>();
        foreach (var m in history)
        {
            // Drop tool-result messages entirely
            if (m.Role == ChatRole.Tool) continue;

            // Check whether this message contains any tool-related content
            var hasFunctionCall = m.Contents.Any(c => c is FunctionCallContent);
            var hasFunctionResult = m.Contents.Any(c => c is FunctionResultContent);

            if (!hasFunctionCall && !hasFunctionResult)
            {
                // Clean message — pass through as-is
                context.Add(m);
                continue;
            }

            // Strip tool-related content, keep the rest (text, images, etc.)
            var cleaned = m.Contents
                .Where(c => c is not FunctionCallContent and not FunctionResultContent)
                .ToList();

            if (cleaned.Count == 0) continue; // Entire message was tool content

            var msg = new ChatMessage(m.Role, cleaned) { AuthorName = m.AuthorName };
            context.Add(msg);
        }
        return context;
    }

    /// <summary>
    /// Generate a synthesized final answer from the full conversation.
    /// Ported from AutoGen's _prepare_final_answer.
    /// </summary>
    private async Task<string> GenerateFinalAnswerAsync(IReadOnlyList<ChatMessage> history, CancellationToken ct)
    {
        var context = BuildFilteredContext(history);

        var prompt = _finalAnswerPrompt ?? DefaultFinalAnswerPrompt;
        prompt = prompt.Contains("{task}")
            ? prompt.Replace("{task}", _task)
            : $"{prompt}\n\nOriginal request: {_task}";

        context.Add(new ChatMessage(ChatRole.User, prompt));

        var response = await _orchestratorClient.GetResponseAsync(context, cancellationToken: ct);
        var raw = response.Text ?? "";
        EmitThought("final_answer", raw);
        return raw;
    }

    // ── Orchestrator Thought Emission ──────────────────────────────────

    /// <summary>
    /// Emit an orchestrator "thought" event for each LLM call.
    /// These are the raw LLM responses for facts, plan, progress ledger, etc.
    /// </summary>
    private void EmitThought(string category, string content)
    {
        var dto = new TeamLedgerUpdateEventDto(
            "thought",
            "Orchestrator",
            JsonSerializer.Serialize(new { category, content }));
        OnLedgerUpdate?.Invoke(dto);
    }

    // ── JSON → Display Text Formatting ─────────────────────────────────

    /// <summary>Format a facts JSON response into a human-readable string for the outer ledger.</summary>
    private static string FormatFacts(string json)
    {
        try
        {
            json = ExtractJson(json);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var sb = new System.Text.StringBuilder();

            FormatFactCategory(sb, root, "given", "GIVEN");
            FormatFactCategory(sb, root, "to_look_up", "TO LOOK UP");
            FormatFactCategory(sb, root, "to_derive", "TO DERIVE");
            FormatFactCategory(sb, root, "guess", "GUESS");

            return sb.ToString().TrimEnd();
        }
        catch
        {
            return json; // Fallback to raw JSON if parsing fails
        }
    }

    private static void FormatFactCategory(System.Text.StringBuilder sb, JsonElement root, string key, string label)
    {
        if (root.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            sb.AppendLine($"**{label}**:");
            foreach (var item in arr.EnumerateArray())
                sb.AppendLine($"  - {item.GetString()}");
        }
    }

    /// <summary>Format a plan JSON response into a human-readable string for the outer ledger.</summary>
    private static string FormatPlan(string json)
    {
        try
        {
            json = ExtractJson(json);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var step in steps.EnumerateArray())
                {
                    var num = step.TryGetProperty("step_number", out var n) ? n.GetInt32() : 0;
                    var desc = step.TryGetProperty("description", out var d) ? d.GetString() : "?";
                    var agent = step.TryGetProperty("assigned_agent", out var a) ? a.GetString() : "?";
                    sb.AppendLine($"{num}. [{agent}] {desc}");
                }
                return sb.ToString().TrimEnd();
            }

            return json;
        }
        catch
        {
            return json;
        }
    }

    // ── Agent Resolution ───────────────────────────────────────────────

    private AIAgent FindAgent(string name)
    {
        var agent = _agents.FirstOrDefault(a =>
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

        if (agent is not null) return agent;

        // Fuzzy fallback
        agent = _agents.FirstOrDefault(a =>
            name.Contains(a.Name!, StringComparison.OrdinalIgnoreCase));

        return agent ?? _agents[0];
    }

    // ── SSE Event Emission ─────────────────────────────────────────────

    private void EmitOuterLedgerUpdate()
    {
        OuterLedger.NStalls = _nStalls;
        OuterLedger.MaxStalls = _maxStalls;
        var dto = new TeamLedgerUpdateEventDto(
            "outer",
            null,
            JsonSerializer.Serialize(OuterLedger));
        OnLedgerUpdate?.Invoke(dto);
    }

    private void EmitInnerLedgerUpdate(InnerLedgerEntry entry)
    {
        var content = JsonSerializer.Serialize(new
        {
            agentName = entry.AgentName,
            task = entry.Task,
            status = entry.Status,
            summary = entry.Summary,
            timestamp = entry.Timestamp
        });
        var dto = new TeamLedgerUpdateEventDto(
            "inner",
            entry.AgentName,
            content);
        OnLedgerUpdate?.Invoke(dto);
    }

    /// <summary>
    /// Update the most recent inner ledger entry for the given agent to "completed"
    /// and emit the change via SSE.
    /// </summary>
    internal void CompleteInnerLedgerEntry(string agentName, string? summary = null)
    {
        for (var i = InnerLedgerEntries.Count - 1; i >= 0; i--)
        {
            var e = InnerLedgerEntries[i];
            if (string.Equals(e.AgentName, agentName, StringComparison.OrdinalIgnoreCase) && e.Status == "running")
            {
                var completed = new InnerLedgerEntry(e.AgentName, e.Task, "completed", summary ?? e.Summary, e.Timestamp);
                InnerLedgerEntries[i] = completed;
                EmitInnerLedgerUpdate(completed);
                break;
            }
        }
    }
}
