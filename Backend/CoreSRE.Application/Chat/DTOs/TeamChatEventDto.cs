namespace CoreSRE.Application.Chat.DTOs;

/// <summary>
/// Base for team-specific SSE event data transmitted during Team mode conversations.
/// </summary>
public abstract record TeamChatEventDto(string EventType);

/// <summary>
/// Handoff notification: "🔀 Agent A → Agent B" — emitted in Handoffs mode
/// when an agent triggers a handoff_to_* tool call.
/// </summary>
public sealed record TeamHandoffEventDto(
    string FromAgentId,
    string FromAgentName,
    string ToAgentId,
    string ToAgentName
) : TeamChatEventDto("TEAM_HANDOFF");

/// <summary>
/// Ledger update for MagneticOne mode — emitted for outer plan changes
/// and inner per-agent task log entries.
/// </summary>
public sealed record TeamLedgerUpdateEventDto(
    string LedgerType,      // "outer" | "inner"
    string? AgentName,       // null for outer ledger
    string Content           // JSON string of ledger data
) : TeamChatEventDto("TEAM_LEDGER_UPDATE");

/// <summary>
/// Progress indicator for all team modes — emitted when orchestrator transitions
/// to a new participant agent.
/// </summary>
public sealed record TeamProgressEventDto(
    string CurrentAgentId,
    string CurrentAgentName,
    int? Step,
    int? TotalSteps,
    string Mode              // TeamMode as string
) : TeamChatEventDto("TEAM_PROGRESS");

/// <summary>
/// Outer ledger maintained by MagneticOneGroupChatManager — plan, progress, next step.
/// In-memory only (not persisted to DB).
/// </summary>
public sealed record OuterLedger
{
    public string Facts { get; set; } = "";
    public string Plan { get; set; } = "";
    public string NextStep { get; set; } = "";
    public string Progress { get; set; } = "";
    public bool IsComplete { get; set; }
    /// <summary>Synthesized final answer (from AutoGen _prepare_final_answer port).</summary>
    public string? FinalAnswer { get; set; }
    /// <summary>Current orchestrator iteration (1-based).</summary>
    public int Iteration { get; set; }
    /// <summary>Consecutive stall count.</summary>
    public int NStalls { get; set; }
    /// <summary>Stall threshold that triggers replanning.</summary>
    public int MaxStalls { get; set; }
}

/// <summary>
/// Inner ledger entry for MagneticOne mode — records per-agent task execution.
/// </summary>
public sealed record InnerLedgerEntry(
    string AgentName,
    string Task,
    string Status,     // "running" | "completed" | "failed"
    string? Summary,
    DateTime Timestamp
);

/// <summary>
/// Progress Ledger — the MagneticOne inner loop evaluation result.
/// Produced by the orchestrator LLM at each iteration to decide next action.
/// Mirrors the AutoGen MagneticOne ProgressLedger JSON schema.
/// </summary>
public sealed record ProgressLedger
{
    /// <summary>Whether the original user request has been fully addressed.</summary>
    public required bool IsRequestSatisfied { get; init; }

    /// <summary>LLM reasoning for the satisfaction decision.</summary>
    public required string IsRequestSatisfiedReason { get; init; }

    /// <summary>Whether the team is repeating the same actions in a loop.</summary>
    public required bool IsInLoop { get; init; }

    /// <summary>Whether meaningful progress is being made toward the goal.</summary>
    public required bool IsProgressBeingMade { get; init; }

    /// <summary>The name of the next agent that should act.</summary>
    public required string NextSpeaker { get; init; }

    /// <summary>The instruction or question to give to the next speaker.</summary>
    public required string InstructionOrQuestion { get; init; }
}
