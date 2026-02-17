namespace CoreSRE.Application.Agents.DTOs;

/// <summary>
/// Team 配置 DTO — TeamConfigVO 的 API 表示。
/// </summary>
public class TeamConfigDto
{
    /// <summary>编排模式（TeamMode 枚举名称）</summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>参与者 Agent ID 列表</summary>
    public List<Guid> ParticipantIds { get; set; } = [];

    /// <summary>最大迭代/轮次数</summary>
    public int MaxIterations { get; set; } = 40;

    // ── Handoffs 模式专属 ──────────────────────────────────────────────

    /// <summary>交接路由表：SourceAgentId → 可交接目标列表（仅 Handoffs 模式）</summary>
    public Dictionary<Guid, List<HandoffTargetDto>>? HandoffRoutes { get; set; }

    /// <summary>Handoffs 模式的初始 Agent ID（仅 Handoffs 模式）</summary>
    public Guid? InitialAgentId { get; set; }

    // ── Selector 模式专属 ─────────────────────────────────────────────

    /// <summary>Selector 使用的 LLM Provider ID（仅 Selector 模式）</summary>
    public Guid? SelectorProviderId { get; set; }

    /// <summary>Selector 使用的 LLM 模型标识符（仅 Selector 模式）</summary>
    public string? SelectorModelId { get; set; }

    /// <summary>Selector 自定义提示词（仅 Selector 模式）</summary>
    public string? SelectorPrompt { get; set; }

    /// <summary>是否允许同一 Agent 连续发言（仅 Selector 模式）</summary>
    public bool AllowRepeatedSpeaker { get; set; } = true;

    // ── MagneticOne 模式专属 ──────────────────────────────────────────

    /// <summary>Orchestrator 使用的 LLM Provider ID（仅 MagneticOne 模式）</summary>
    public Guid? OrchestratorProviderId { get; set; }

    /// <summary>Orchestrator 使用的 LLM 模型标识符（仅 MagneticOne 模式）</summary>
    public string? OrchestratorModelId { get; set; }

    /// <summary>最大停滞次数（仅 MagneticOne 模式）</summary>
    public int MaxStalls { get; set; } = 3;

    /// <summary>最终答案提示词（仅 MagneticOne 模式）</summary>
    public string? FinalAnswerPrompt { get; set; }

    // ── Concurrent 模式专属 ──────────────────────────────────────────

    /// <summary>并发结果聚合策略（仅 Concurrent 模式）</summary>
    public string? AggregationStrategy { get; set; }
}

/// <summary>
/// Handoff 交接目标 DTO — HandoffTargetVO 的 API 表示。
/// </summary>
public class HandoffTargetDto
{
    /// <summary>目标 Agent ID</summary>
    public Guid TargetAgentId { get; set; }

    /// <summary>交接原因描述（可选）</summary>
    public string? Reason { get; set; }
}
