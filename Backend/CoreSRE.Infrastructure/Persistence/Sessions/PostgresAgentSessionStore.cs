using CoreSRE.Domain.Entities;
using CoreSRE.Infrastructure.Persistence;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.EntityFrameworkCore;

namespace CoreSRE.Infrastructure.Persistence.Sessions;

/// <summary>
/// PostgreSQL 持久化的 Agent 会话存储。
/// 继承 Agent Framework 的 AgentSessionStore 抽象类，使用 UPSERT 语义存储会话到 agent_sessions 表。
/// 通过 IDbContextFactory 解决 singleton (keyed singleton store) 与 scoped (DbContext) 的生命周期不匹配。
/// </summary>
public class PostgresAgentSessionStore : AgentSessionStore
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public PostgresAgentSessionStore(IDbContextFactory<AppDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory, nameof(contextFactory));
        _contextFactory = contextFactory;
    }

    /// <summary>
    /// 序列化并持久化 Agent 会话到 PostgreSQL。
    /// 使用 INSERT ... ON CONFLICT DO UPDATE (UPSERT) 实现首次插入和后续更新的原子操作。
    /// </summary>
    public override async ValueTask SaveSessionAsync(
        AIAgent agent,
        string conversationId,
        AgentSession session,
        CancellationToken cancellationToken = default)
    {
        var sessionData = agent.SerializeSession(session);
        var sessionDataJson = sessionData.GetRawText();
        var agentId = agent.Id;
        var sessionType = session.GetType().Name;
        var now = DateTime.UtcNow;

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO agent_sessions (agent_id, conversation_id, session_data, session_type, created_at, updated_at)
            VALUES ({agentId}, {conversationId}, {sessionDataJson}::jsonb, {sessionType}, {now}, {now})
            ON CONFLICT (agent_id, conversation_id)
            DO UPDATE SET
                session_data = EXCLUDED.session_data,
                session_type = EXCLUDED.session_type,
                updated_at = EXCLUDED.updated_at
            """,
            cancellationToken);
    }

    /// <summary>
    /// 从 PostgreSQL 读取并反序列化 Agent 会话。
    /// 如果记录不存在，调用 agent.CreateSessionAsync() 创建新会话（永不返回 null）。
    /// </summary>
    public override async ValueTask<AgentSession> GetSessionAsync(
        AIAgent agent,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var record = await context.AgentSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.AgentId == agent.Id && s.ConversationId == conversationId,
                cancellationToken);

        if (record is null)
        {
            return await agent.CreateSessionAsync(cancellationToken);
        }

        return await agent.DeserializeSessionAsync(record.SessionData, cancellationToken: cancellationToken);
    }
}
