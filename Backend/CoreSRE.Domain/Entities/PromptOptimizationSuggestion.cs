using CoreSRE.Domain.Enums;

namespace CoreSRE.Domain.Entities;

/// <summary>
/// Agent Prompt 优化建议。由系统分析历史数据后自动生成。
/// </summary>
public class PromptOptimizationSuggestion : BaseEntity
{
    public Guid AgentId { get; private set; }
    public PromptIssueType IssueType { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string SuggestedPromptPatch { get; private set; } = string.Empty;

    /// <summary>依据的 Incident 列表</summary>
    public List<Guid> BasedOnIncidentIds { get; private set; } = [];

    public SuggestionStatus Status { get; private set; } = SuggestionStatus.Pending;

    /// <summary>变更前的 Instructions 快照（用于回退）</summary>
    public string? PreviousInstructionSnapshot { get; private set; }

    public DateTime? AppliedAt { get; private set; }

    private PromptOptimizationSuggestion() { }

    public static PromptOptimizationSuggestion Create(
        Guid agentId,
        PromptIssueType issueType,
        string description,
        string suggestedPromptPatch,
        List<Guid> basedOnIncidentIds)
    {
        return new PromptOptimizationSuggestion
        {
            AgentId = agentId,
            IssueType = issueType,
            Description = description,
            SuggestedPromptPatch = suggestedPromptPatch,
            BasedOnIncidentIds = basedOnIncidentIds,
            Status = SuggestionStatus.Pending
        };
    }

    /// <summary>应用建议，保存旧 Instructions 快照</summary>
    public void Apply(string previousInstructions)
    {
        if (Status != SuggestionStatus.Pending)
            throw new InvalidOperationException($"Cannot apply suggestion in '{Status}' status.");

        PreviousInstructionSnapshot = previousInstructions;
        Status = SuggestionStatus.Applied;
        AppliedAt = DateTime.UtcNow;
    }

    /// <summary>驳回建议</summary>
    public void Reject()
    {
        if (Status != SuggestionStatus.Pending)
            throw new InvalidOperationException($"Cannot reject suggestion in '{Status}' status.");

        Status = SuggestionStatus.Rejected;
    }

    /// <summary>自动回退已应用的建议</summary>
    public void AutoRevert()
    {
        if (Status != SuggestionStatus.Applied)
            throw new InvalidOperationException($"Cannot revert suggestion in '{Status}' status.");

        Status = SuggestionStatus.AutoReverted;
    }
}
