using System.Text.Json.Serialization;

namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// Handoff 交接目标值对象。定义一个 Agent 可以交接到的目标 Agent 及原因。
/// 存储为 TeamConfigVO 内嵌 JSONB。
/// </summary>
public sealed record HandoffTargetVO
{
    /// <summary>目标 Agent 的注册 ID</summary>
    public Guid TargetAgentId { get; init; }

    /// <summary>交接原因描述（可选）</summary>
    public string? Reason { get; init; }

    // Parameterless constructor for STJ deserialization (Npgsql JSONB)
    [JsonConstructor]
    private HandoffTargetVO() { }

    public HandoffTargetVO(Guid targetAgentId, string? reason = null)
    {
        if (targetAgentId == Guid.Empty)
            throw new ArgumentException("TargetAgentId must not be empty.", nameof(targetAgentId));

        TargetAgentId = targetAgentId;
        Reason = reason;
    }
}
