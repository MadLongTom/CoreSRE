namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 单个端口上的数据 — 有序的 WorkflowItemVO 列表。
/// 空端口用 Empty 表示。
/// </summary>
public sealed record PortDataVO
{
    /// <summary>端口上的数据项列表</summary>
    public IReadOnlyList<WorkflowItemVO> Items { get; init; }

    /// <summary>空端口（无数据项）</summary>
    public static PortDataVO Empty { get; } = new() { Items = [] };

    public PortDataVO()
    {
        Items = [];
    }

    public PortDataVO(IReadOnlyList<WorkflowItemVO> items)
    {
        Items = items;
    }
}
