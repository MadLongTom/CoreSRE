using CoreSRE.Application.Tools.DTOs;
using CoreSRE.Domain.Enums;

namespace CoreSRE.Application.Interfaces;

/// <summary>
/// 工具调用器接口。根据 ToolType 适配不同协议（REST / MCP）进行统一调用。
/// Application 层定义接口，Infrastructure 层提供实现。
/// </summary>
public interface IToolInvoker
{
    /// <summary>
    /// 判断此调用器是否能处理指定类型的工具。
    /// </summary>
    /// <param name="toolType">工具类型</param>
    /// <returns>是否可处理</returns>
    bool CanHandle(ToolType toolType);

    /// <summary>
    /// 调用工具并返回标准化结果。
    /// </summary>
    /// <param name="tool">已注册的工具</param>
    /// <param name="mcpToolName">MCP 子工具名称（仅 McpServer 类型需要）</param>
    /// <param name="parameters">调用参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>标准化调用结果</returns>
    Task<ToolInvocationResultDto> InvokeAsync(
        Domain.Entities.ToolRegistration tool,
        string? mcpToolName,
        IDictionary<string, object?> parameters,
        IDictionary<string, string>? queryParameters = null,
        IDictionary<string, string>? headerParameters = null,
        CancellationToken cancellationToken = default);
}
