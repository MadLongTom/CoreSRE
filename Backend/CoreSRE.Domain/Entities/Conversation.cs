namespace CoreSRE.Domain.Entities;

/// <summary>
/// 对话聚合根。代表用户与某个 Agent 之间的一轮完整对话。
/// 聊天历史存储在关联的 AgentSessionRecord.SessionData 中（SPEC-004），本实体仅保存元数据。
/// </summary>
public class Conversation : BaseEntity
{
    /// <summary>关联的 AgentRegistration ID，创建后不可变更</summary>
    public Guid AgentId { get; private set; }

    /// <summary>对话标题（从第一条消息自动生成，最长 200 字符）</summary>
    public string? Title { get; private set; }

    // EF Core requires parameterless constructor
    private Conversation() { }

    /// <summary>创建新对话，绑定到指定 Agent</summary>
    public static Conversation Create(Guid agentId)
    {
        if (agentId == Guid.Empty)
            throw new ArgumentException("AgentId must not be empty.", nameof(agentId));

        return new Conversation
        {
            AgentId = agentId
        };
    }

    /// <summary>设置对话标题（仅在未设置时生效）</summary>
    public void SetTitle(string title)
    {
        if (Title is not null) return;

        ArgumentException.ThrowIfNullOrWhiteSpace(title, nameof(title));
        Title = title.Length > 200 ? title[..200] : title;
    }

    /// <summary>刷新最后活跃时间</summary>
    public void Touch()
    {
        UpdatedAt = DateTime.UtcNow;
    }
}
