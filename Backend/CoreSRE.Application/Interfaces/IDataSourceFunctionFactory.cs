using CoreSRE.Domain.ValueObjects;
using Microsoft.Extensions.AI;

namespace CoreSRE.Application.Interfaces;

/// <summary>
/// 数据源 AIFunction 工厂接口。
/// 根据 DataSourceRefVO 列表将数据源查询能力暴露为 AIFunction。
/// </summary>
public interface IDataSourceFunctionFactory
{
    /// <summary>
    /// 根据数据源引用列表创建 AIFunction 集合。
    /// 每个 DataSourceRefVO 的 EnabledFunctions 控制暴露哪些函数（null = 全部）。
    /// </summary>
    Task<IReadOnlyList<AIFunction>> CreateFunctionsAsync(
        IReadOnlyList<DataSourceRefVO> dataSourceRefs,
        CancellationToken ct = default);
}
