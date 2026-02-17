namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 数据源引用 VO — 控制 Agent 绑定数据源的函数级别粒度。
/// null EnabledFunctions = 暴露全部 AIFunction，否则只暴露指定名称的函数。
/// </summary>
public sealed record DataSourceRefVO
{
    /// <summary>绑定的数据源 ID</summary>
    public Guid DataSourceId { get; init; }

    /// <summary>启用的函数名列表。null = 全部函数，否则只暴露列出的函数</summary>
    public List<string>? EnabledFunctions { get; init; }
}
