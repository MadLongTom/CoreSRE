using System.Text.Json;

namespace CoreSRE.Application.Interfaces;

/// <summary>
/// 对话消息读取器 — 从 AgentSessionRecord.SessionData 读取聊天历史。
/// Application 层只关心读取接口，不关心存储细节。
/// </summary>
public interface IChatHistoryReader
{
    /// <summary>
    /// 读取指定对话的消息历史（从 AgentSessionRecord.SessionData 中提取）。
    /// </summary>
    /// <param name="agentId">Agent 注册 ID（AgentSessionRecord.AgentId）</param>
    /// <param name="conversationId">对话 ID（AgentSessionRecord.ConversationId）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>SessionData JSON（可能为 null 如果没有记录）</returns>
    Task<JsonElement?> GetSessionDataAsync(string agentId, string conversationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除指定对话的会话记录。
    /// </summary>
    /// <param name="agentId">Agent 注册 ID（AgentSessionRecord.AgentId）</param>
    /// <param name="conversationId">对话 ID（AgentSessionRecord.ConversationId）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task DeleteSessionAsync(string agentId, string conversationId, CancellationToken cancellationToken = default);
}
