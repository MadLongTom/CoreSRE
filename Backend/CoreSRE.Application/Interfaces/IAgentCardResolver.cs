using CoreSRE.Application.Agents.DTOs;

namespace CoreSRE.Application.Interfaces;

/// <summary>
/// 从远程 A2A Agent 端点解析 AgentCard。
/// Application 层定义接口，Infrastructure 层提供实现。
/// </summary>
public interface IAgentCardResolver
{
    /// <summary>
    /// 从指定 URL 解析 AgentCard 并映射为 DTO。
    /// </summary>
    /// <param name="url">A2A Agent 的 Endpoint URL</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>解析后的 AgentCard DTO</returns>
    Task<ResolvedAgentCardDto> ResolveAsync(string url, CancellationToken cancellationToken);
}
