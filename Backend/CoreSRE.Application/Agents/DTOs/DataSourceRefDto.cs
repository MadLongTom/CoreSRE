namespace CoreSRE.Application.Agents.DTOs;

/// <summary>
/// 数据源引用 DTO — 控制 Agent 绑定数据源的函数级别粒度。
/// </summary>
public class DataSourceRefDto
{
    /// <summary>绑定的数据源 ID</summary>
    public Guid DataSourceId { get; set; }

    /// <summary>启用的函数名列表。null = 全部函数</summary>
    public List<string>? EnabledFunctions { get; set; }
}
