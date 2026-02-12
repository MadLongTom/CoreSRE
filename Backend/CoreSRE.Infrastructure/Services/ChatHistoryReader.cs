using System.Text.Json;
using CoreSRE.Application.Interfaces;
using CoreSRE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// 从 AgentSessionRecord 读取聊天历史 — 实现 IChatHistoryReader。
/// </summary>
public class ChatHistoryReader : IChatHistoryReader
{
    private readonly AppDbContext _context;

    public ChatHistoryReader(AppDbContext context)
    {
        _context = context;
    }

    public async Task<JsonElement?> GetSessionDataAsync(
        string agentId,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        var record = await _context.AgentSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.AgentId == agentId && s.ConversationId == conversationId,
                cancellationToken);

        return record?.SessionData;
    }

    public async Task DeleteSessionAsync(
        string agentId,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        var record = await _context.AgentSessions
            .FirstOrDefaultAsync(
                s => s.AgentId == agentId && s.ConversationId == conversationId,
                cancellationToken);

        if (record is not null)
        {
            _context.AgentSessions.Remove(record);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
