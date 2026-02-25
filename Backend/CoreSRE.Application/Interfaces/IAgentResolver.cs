using CoreSRE.Application.Chat.DTOs;
using CoreSRE.Domain.ValueObjects;
using Microsoft.Agents.AI;
using System.Collections.Concurrent;

namespace CoreSRE.Application.Interfaces;

/// <summary>
/// 解析结果 — 包含就绪的 AIAgent 实例和关联的 LlmConfig（可空，A2A/Team Agent 无 LlmConfig）。
/// IsTeam 标识是否为 Team 类型 Agent（需要使用 Team 流式处理路径）。
/// TeamEventQueue 用于 MagneticOne 模式的 Ledger 更新事件推送。
/// </summary>
public record ResolvedAgent(
    AIAgent Agent,
    LlmConfigVO? LlmConfig,
    bool IsTeam = false,
    ConcurrentQueue<TeamChatEventDto>? TeamEventQueue = null);

/// <summary>
/// 解析 AgentRegistration ID 为可运行的 AIAgent 实例。
/// Infrastructure 层实现负责从 AgentRegistration 获取 LlmProvider 配置，
/// 构建 OpenAIClient → IChatClient → ChatClientAgent。
/// </summary>
public interface IAgentResolver
{
    /// <summary>
    /// 根据 AgentRegistration ID 解析并构建 AIAgent。
    /// </summary>
    /// <param name="agentRegistrationId">AgentRegistration 的主键 ID</param>
    /// <param name="conversationId">对话 ID，用于配置 ChatHistoryProvider 的 session key</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含 AIAgent 和 LlmConfig 的解析结果</returns>
    Task<ResolvedAgent> ResolveAsync(Guid agentRegistrationId, string conversationId, CancellationToken cancellationToken = default);
}
