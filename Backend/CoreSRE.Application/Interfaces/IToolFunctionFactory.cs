using Microsoft.Extensions.AI;

namespace CoreSRE.Application.Interfaces;

/// <summary>
/// 将 ToolRefs（Guid 列表）解析为可调用的 AIFunction 实例列表。
/// 跳过已删除/未找到的 ID 并记录警告。
/// </summary>
public interface IToolFunctionFactory
{
    /// <summary>
    /// Resolves tool references to invocable AIFunction instances.
    /// Skips unresolved (deleted) IDs with a logged warning.
    /// </summary>
    Task<IReadOnlyList<AIFunction>> CreateFunctionsAsync(
        IReadOnlyList<Guid> toolRefs,
        CancellationToken cancellationToken = default);
}
