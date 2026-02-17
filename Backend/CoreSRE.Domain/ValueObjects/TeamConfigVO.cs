using System.Text.Json.Serialization;
using CoreSRE.Domain.Enums;

namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// Team Agent 配置值对象。存储为 PostgreSQL JSONB 列。
/// 按 TeamMode 区分：通用字段始终存在，模式专属字段仅在对应模式下有值。
/// </summary>
public sealed record TeamConfigVO
{
    /// <summary>编排模式</summary>
    public TeamMode Mode { get; init; }

    /// <summary>参与者 Agent ID 列表（至少 1 个）</summary>
    public List<Guid> ParticipantIds { get; init; } = [];

    /// <summary>最大迭代/轮次数</summary>
    public int MaxIterations { get; init; } = 40;

    // ── Handoffs 模式专属 ──────────────────────────────────────────────

    /// <summary>交接路由表：SourceAgentId → 可交接目标列表（仅 Handoffs 模式）</summary>
    public Dictionary<Guid, List<HandoffTargetVO>>? HandoffRoutes { get; init; }

    /// <summary>Handoffs 模式的初始 Agent ID（仅 Handoffs 模式）</summary>
    public Guid? InitialAgentId { get; init; }

    // ── Selector 模式专属 ─────────────────────────────────────────────

    /// <summary>Selector 使用的 LLM Provider ID（仅 Selector 模式）</summary>
    public Guid? SelectorProviderId { get; init; }

    /// <summary>Selector 使用的 LLM 模型标识符（仅 Selector 模式）</summary>
    public string? SelectorModelId { get; init; }

    /// <summary>Selector 自定义提示词（仅 Selector 模式）</summary>
    public string? SelectorPrompt { get; init; }

    /// <summary>是否允许同一 Agent 连续发言（仅 Selector 模式）</summary>
    public bool AllowRepeatedSpeaker { get; init; } = true;

    // ── MagneticOne 模式专属 ──────────────────────────────────────────

    /// <summary>Orchestrator 使用的 LLM Provider ID（仅 MagneticOne 模式）</summary>
    public Guid? OrchestratorProviderId { get; init; }

    /// <summary>Orchestrator 使用的 LLM 模型标识符（仅 MagneticOne 模式）</summary>
    public string? OrchestratorModelId { get; init; }

    /// <summary>最大停滞次数，超过后强制终止（仅 MagneticOne 模式）</summary>
    public int MaxStalls { get; init; } = 3;

    /// <summary>最终答案提示词（仅 MagneticOne 模式）</summary>
    public string? FinalAnswerPrompt { get; init; }

    // ── Concurrent 模式专属 ──────────────────────────────────────────

    /// <summary>并发结果聚合策略（仅 Concurrent 模式，如 "Merge" / "Vote"）</summary>
    public string? AggregationStrategy { get; init; }

    // Parameterless constructor for STJ deserialization (Npgsql JSONB)
    [JsonConstructor]
    private TeamConfigVO() { }

    /// <summary>
    /// 创建 TeamConfigVO 并执行模式特定验证。
    /// </summary>
    public static TeamConfigVO Create(
        TeamMode mode,
        List<Guid> participantIds,
        int maxIterations = 40,
        Dictionary<Guid, List<HandoffTargetVO>>? handoffRoutes = null,
        Guid? initialAgentId = null,
        Guid? selectorProviderId = null,
        string? selectorModelId = null,
        string? selectorPrompt = null,
        bool allowRepeatedSpeaker = true,
        Guid? orchestratorProviderId = null,
        string? orchestratorModelId = null,
        int maxStalls = 3,
        string? finalAnswerPrompt = null,
        string? aggregationStrategy = null)
    {
        ArgumentNullException.ThrowIfNull(participantIds, nameof(participantIds));

        if (participantIds.Count == 0)
            throw new ArgumentException("ParticipantIds must not be empty.", nameof(participantIds));

        if (participantIds.Any(id => id == Guid.Empty))
            throw new ArgumentException("ParticipantIds must not contain empty GUIDs.", nameof(participantIds));

        if (maxIterations <= 0)
            throw new ArgumentException("MaxIterations must be greater than 0.", nameof(maxIterations));

        // Mode-specific validation
        switch (mode)
        {
            case TeamMode.Sequential:
            case TeamMode.Concurrent:
            case TeamMode.RoundRobin:
                if (participantIds.Count < 2)
                    throw new ArgumentException(
                        $"{mode} Team requires at least 2 participants.", nameof(participantIds));
                break;

            case TeamMode.Handoffs:
                if (initialAgentId is null)
                    throw new ArgumentException(
                        "InitialAgentId is required for Handoffs mode.", nameof(initialAgentId));
                if (!participantIds.Contains(initialAgentId.Value))
                    throw new ArgumentException(
                        "InitialAgentId must be one of the ParticipantIds.", nameof(initialAgentId));
                if (handoffRoutes is null || handoffRoutes.Count == 0)
                    throw new ArgumentException(
                        "HandoffRoutes is required for Handoffs mode.", nameof(handoffRoutes));
                // Validate all route sources and targets are within participants
                foreach (var (source, targets) in handoffRoutes)
                {
                    if (!participantIds.Contains(source))
                        throw new ArgumentException(
                            $"HandoffRoutes source '{source}' is not in ParticipantIds.", nameof(handoffRoutes));
                    foreach (var target in targets)
                    {
                        if (!participantIds.Contains(target.TargetAgentId))
                            throw new ArgumentException(
                                $"HandoffRoutes target '{target.TargetAgentId}' is not in ParticipantIds.",
                                nameof(handoffRoutes));
                    }
                }
                break;

            case TeamMode.Selector:
                if (participantIds.Count < 2)
                    throw new ArgumentException(
                        "Selector Team requires at least 2 participants.", nameof(participantIds));
                if (selectorProviderId is null)
                    throw new ArgumentException(
                        "SelectorProviderId is required for Selector mode.", nameof(selectorProviderId));
                if (string.IsNullOrWhiteSpace(selectorModelId))
                    throw new ArgumentException(
                        "SelectorModelId is required for Selector mode.", nameof(selectorModelId));
                break;

            case TeamMode.MagneticOne:
                if (orchestratorProviderId is null)
                    throw new ArgumentException(
                        "OrchestratorProviderId is required for MagneticOne mode.", nameof(orchestratorProviderId));
                if (string.IsNullOrWhiteSpace(orchestratorModelId))
                    throw new ArgumentException(
                        "OrchestratorModelId is required for MagneticOne mode.", nameof(orchestratorModelId));
                if (maxStalls <= 0)
                    throw new ArgumentException(
                        "MaxStalls must be greater than 0.", nameof(maxStalls));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported TeamMode.");
        }

        return new TeamConfigVO
        {
            Mode = mode,
            ParticipantIds = participantIds,
            MaxIterations = maxIterations,
            HandoffRoutes = handoffRoutes,
            InitialAgentId = initialAgentId,
            SelectorProviderId = selectorProviderId,
            SelectorModelId = selectorModelId,
            SelectorPrompt = selectorPrompt,
            AllowRepeatedSpeaker = allowRepeatedSpeaker,
            OrchestratorProviderId = orchestratorProviderId,
            OrchestratorModelId = orchestratorModelId,
            MaxStalls = maxStalls,
            FinalAnswerPrompt = finalAnswerPrompt,
            AggregationStrategy = aggregationStrategy,
        };
    }
}
