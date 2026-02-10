using System.Text.Json;

namespace CoreSRE.Domain.Entities;

/// <summary>
/// Agent 会话持久化记录。独立实体，不继承 BaseEntity（使用复合字符串主键）。
/// 通过 PostgreSQL agent_sessions 表存储 Agent Framework 的会话序列化数据。
/// </summary>
public class AgentSessionRecord
{
    /// <summary>Agent 标识符（来自 AIAgent.Id）</summary>
    public string AgentId { get; private set; } = string.Empty;

    /// <summary>对话标识符</summary>
    public string ConversationId { get; private set; } = string.Empty;

    /// <summary>序列化的会话数据（不透明 JSON，映射为 PostgreSQL JSONB）</summary>
    public JsonElement SessionData { get; private set; }

    /// <summary>会话类型名称（如 ChatClientAgentSession），用于运维诊断</summary>
    public string SessionType { get; private set; } = string.Empty;

    /// <summary>记录创建时间 (UTC)</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>最后更新时间 (UTC)</summary>
    public DateTime UpdatedAt { get; private set; }

    // EF Core requires parameterless constructor
    private AgentSessionRecord() { }

    /// <summary>
    /// 创建新的 AgentSessionRecord 实例。
    /// </summary>
    public static AgentSessionRecord Create(
        string agentId,
        string conversationId,
        JsonElement sessionData,
        string sessionType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId, nameof(agentId));
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId, nameof(conversationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionType, nameof(sessionType));

        var now = DateTime.UtcNow;
        return new AgentSessionRecord
        {
            AgentId = agentId,
            ConversationId = conversationId,
            SessionData = sessionData,
            SessionType = sessionType,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// 更新会话数据和类型，刷新 UpdatedAt 时间戳。
    /// </summary>
    public void Update(JsonElement sessionData, string sessionType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionType, nameof(sessionType));

        SessionData = sessionData;
        SessionType = sessionType;
        UpdatedAt = DateTime.UtcNow;
    }
}
