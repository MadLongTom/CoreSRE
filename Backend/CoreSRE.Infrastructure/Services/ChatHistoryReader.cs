using System.Text.Json;
using CoreSRE.Application.Interfaces;
using CoreSRE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// 从 AgentSessionRecord 读取聊天历史 — 实现 IChatHistoryReader。
/// </summary>
public class ChatHistoryReader : IChatHistoryReader
{
    private readonly AppDbContext _context;
    private readonly string _memoryTable;

    public ChatHistoryReader(AppDbContext context, IConfiguration configuration)
    {
        _context = context;
        _memoryTable = configuration["SemanticMemory:CollectionName"] ?? "coresre_memory";
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

    public async Task DeleteMemoryAsync(
        string agentId,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        // Delete vector memory records scoped to this agent + conversation (SessionId).
        // Uses raw SQL because coresre_memory is managed by pgvector VectorStore,
        // not by EF Core.
        // Table name comes from config (safe), column values are parameterized.
        var sql = $@"DELETE FROM ""{_memoryTable}"" WHERE ""AgentId"" = {{0}} AND ""SessionId"" = {{1}}";
        await _context.Database.ExecuteSqlRawAsync(sql, [agentId, conversationId], cancellationToken);
    }
}
