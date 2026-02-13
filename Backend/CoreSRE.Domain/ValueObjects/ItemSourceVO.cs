namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 数据项溯源信息 — 记录数据项由哪个节点、哪个输出端口、第几个位置产生。
/// 用于数据追踪和调试。
/// </summary>
/// <param name="NodeId">产生此数据项的节点 ID</param>
/// <param name="OutputIndex">产生此数据项的输出端口索引</param>
/// <param name="ItemIndex">此数据项在输出端口列表中的位置</param>
public sealed record ItemSourceVO(string NodeId, int OutputIndex, int ItemIndex);
