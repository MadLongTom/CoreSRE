using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Entities;

namespace CoreSRE.Application.Interfaces;

/// <summary>
/// MCP 工具发现服务接口。通过 MCP 协议握手并发现工具源下的所有 Tool。
/// Application 层定义接口，Infrastructure 层提供实现。
/// </summary>
public interface IMcpToolDiscoveryService
{
    /// <summary>
    /// 对指定 MCP Server 工具源执行握手并发现其下的所有 Tool。
    /// </summary>
    /// <param name="registration">已注册的 MCP Server 工具源</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>发现的 McpToolItem 列表；握手失败返回 Failure</returns>
    Task<Result<IReadOnlyList<McpToolItem>>> DiscoverToolsAsync(
        ToolRegistration registration,
        CancellationToken cancellationToken = default);
}
