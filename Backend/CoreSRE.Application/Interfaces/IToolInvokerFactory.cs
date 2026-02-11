using CoreSRE.Domain.Enums;

namespace CoreSRE.Application.Interfaces;

/// <summary>
/// 工具调用器工厂接口。根据 ToolType 选择对应的 IToolInvoker 实现。
/// </summary>
public interface IToolInvokerFactory
{
    /// <summary>
    /// 获取能够处理指定工具类型的调用器。
    /// </summary>
    /// <param name="toolType">工具类型</param>
    /// <returns>匹配的调用器</returns>
    /// <exception cref="NotSupportedException">当没有匹配的调用器时抛出</exception>
    IToolInvoker GetInvoker(ToolType toolType);
}
