using System.Text;
using CoreSRE.Application.Alerts.Interfaces;
using CoreSRE.Application.Interfaces;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// IAgentCaller 的 Infrastructure 实现。
/// 封装 IAgentResolver + AgentSessionStore，为 Application 层屏蔽框架依赖。
/// </summary>
public class AgentCallerService(
    IAgentResolver agentResolver,
    AgentSessionStore sessionStore,
    ILogger<AgentCallerService> logger) : IAgentCaller
{
    /// <inheritdoc />
    public async Task<string> SendMessageAsync(
        Guid agentId,
        string conversationId,
        string userMessage,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(10);

        // 1. 解析 Agent
        var resolved = await agentResolver.ResolveAsync(agentId, conversationId, cancellationToken);
        var aiAgent = resolved.Agent;

        // 2. 构造消息列表
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, userMessage)
        };

        // 3. 加载或创建 Session
        AgentSession? session;
        try
        {
            session = await sessionStore.GetSessionAsync(aiAgent, conversationId, cancellationToken);
        }
        catch
        {
            session = await aiAgent.CreateSessionAsync(cancellationToken);
        }

        // 4. 带超时流式执行
        using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        var fullResponse = new StringBuilder();

        await foreach (var update in aiAgent.RunStreamingAsync(messages, session, cancellationToken: linkedCts.Token))
        {
            foreach (var content in update.Contents)
            {
                if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                {
                    fullResponse.Append(textContent.Text);
                }
            }
        }

        // 5. 保存 Session
        try
        {
            await sessionStore.SaveSessionAsync(aiAgent, conversationId, session, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save agent session for Agent {AgentId}, Conversation {ConversationId}.",
                agentId, conversationId);
        }

        return fullResponse.ToString();
    }
}
