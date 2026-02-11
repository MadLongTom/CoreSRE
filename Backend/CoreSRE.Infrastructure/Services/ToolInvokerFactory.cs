using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Enums;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// 工具调用器工厂。根据 ToolType 选择对应的 IToolInvoker 实现。
/// </summary>
public class ToolInvokerFactory : IToolInvokerFactory
{
    private readonly IEnumerable<IToolInvoker> _invokers;

    public ToolInvokerFactory(IEnumerable<IToolInvoker> invokers)
    {
        _invokers = invokers;
    }

    /// <summary>
    /// 获取能够处理指定工具类型的调用器。
    /// </summary>
    /// <param name="toolType">工具类型</param>
    /// <returns>匹配的调用器</returns>
    /// <exception cref="NotSupportedException">当没有匹配的调用器时抛出</exception>
    public IToolInvoker GetInvoker(ToolType toolType)
    {
        var invoker = _invokers.FirstOrDefault(i => i.CanHandle(toolType));
        if (invoker is null)
        {
            throw new NotSupportedException($"No invoker registered for tool type: {toolType}");
        }
        return invoker;
    }
}
