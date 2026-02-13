using System.Text.Json;

namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 工作流数据项 — 节点间数据流的基本单位。
/// 包含 JSON 负载和可选的溯源信息。
/// </summary>
/// <param name="Json">数据负载（JSON 元素）</param>
/// <param name="Source">可选溯源信息（产生此项的节点/端口/位置）</param>
public sealed record WorkflowItemVO(JsonElement Json, ItemSourceVO? Source = null);
