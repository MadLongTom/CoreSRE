namespace CoreSRE.Application.Alerts.Interfaces;

/// <summary>
/// Agent 调用器接口。封装了 Agent 解析 + Session 管理 + 流式调用。
/// Application 层通过此接口调用 Agent，无需直接依赖 Microsoft.Agents.AI.Hosting。
/// </summary>
public interface IAgentCaller
{
    /// <summary>
    /// 向指定 Agent 发送一条消息并获取完整响应文本。
    /// </summary>
    /// <param name="agentId">Agent 注册 ID</param>
    /// <param name="conversationId">对话 ID（用于 session 管理）</param>
    /// <param name="userMessage">用户消息文本</param>
    /// <param name="timeout">超时时长</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>Agent 完整响应文本</returns>
    Task<string> SendMessageAsync(
        Guid agentId,
        string conversationId,
        string userMessage,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
