using CoreSRE.Domain.Enums;

namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 工作流 DAG 图值对象，包含节点列表和边列表。JSONB 存储。
/// 提供 DAG 有效性校验方法（环检测、孤立节点、自环、重复等）。
/// </summary>
public sealed record WorkflowGraphVO
{
    /// <summary>建议的最大节点数量</summary>
    public const int RecommendedMaxNodes = 100;

    /// <summary>节点列表</summary>
    public List<WorkflowNodeVO> Nodes { get; init; } = [];

    /// <summary>边列表</summary>
    public List<WorkflowEdgeVO> Edges { get; init; } = [];

    /// <summary>
    /// 验证 DAG 图的有效性。
    /// 校验项：空图、重复节点 ID、重复边 ID、自环、边引用无效节点、重复无条件边、孤立节点、环路检测。
    /// </summary>
    /// <returns>校验结果。Success 为 true 时 Errors 为空；false 时 Errors 包含所有校验失败信息。Warning 包含建议性警告。</returns>
    public DagValidationResult Validate()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // 1. 至少一个节点
        if (Nodes.Count == 0)
        {
            errors.Add("工作流图至少需要一个节点。");
            return new DagValidationResult(errors, warnings);
        }

        // 2. 节点数量警告
        if (Nodes.Count > RecommendedMaxNodes)
        {
            warnings.Add($"工作流图包含 {Nodes.Count} 个节点，超过建议上限 {RecommendedMaxNodes}。");
        }

        // 3. 重复节点 ID
        var nodeIds = new HashSet<string>();
        foreach (var node in Nodes)
        {
            if (!nodeIds.Add(node.NodeId))
            {
                errors.Add($"节点 ID 重复: {node.NodeId}");
            }
        }

        // 4. 重复边 ID
        var edgeIds = new HashSet<string>();
        foreach (var edge in Edges)
        {
            if (!edgeIds.Add(edge.EdgeId))
            {
                errors.Add($"边 ID 重复: {edge.EdgeId}");
            }
        }

        // 5. 自环检测
        foreach (var edge in Edges)
        {
            if (edge.SourceNodeId == edge.TargetNodeId)
            {
                errors.Add($"边不能指向自身节点: {edge.EdgeId} ({edge.SourceNodeId} → {edge.TargetNodeId})");
            }
        }

        // 6. 边引用的节点必须存在
        foreach (var edge in Edges)
        {
            if (!nodeIds.Contains(edge.SourceNodeId))
            {
                errors.Add($"边 {edge.EdgeId} 引用了不存在的源节点: {edge.SourceNodeId}");
            }
            if (!nodeIds.Contains(edge.TargetNodeId))
            {
                errors.Add($"边 {edge.EdgeId} 引用了不存在的目标节点: {edge.TargetNodeId}");
            }
        }

        // 7. 重复无条件边（同一对 source/target 只允许一条 Normal 边）
        var normalEdgePairs = new HashSet<string>();
        foreach (var edge in Edges.Where(e => e.EdgeType == WorkflowEdgeType.Normal))
        {
            var pair = $"{edge.SourceNodeId}->{edge.TargetNodeId}";
            if (!normalEdgePairs.Add(pair))
            {
                errors.Add($"同一对节点之间存在重复的无条件边: {edge.SourceNodeId} → {edge.TargetNodeId}");
            }
        }

        // 如果已有结构性错误，不进行拓扑排序（会产生误导性结果）
        if (errors.Count > 0)
            return new DagValidationResult(errors, warnings);

        // 7.5 端口索引校验
        var nodeMapForPorts = Nodes.ToDictionary(n => n.NodeId);
        foreach (var edge in Edges)
        {
            if (nodeMapForPorts.TryGetValue(edge.SourceNodeId, out var sourceNode))
            {
                if (edge.SourcePortIndex >= sourceNode.OutputCount)
                {
                    errors.Add($"边 '{edge.EdgeId}' 的源端口索引 {edge.SourcePortIndex} 超出节点 '{edge.SourceNodeId}' 的输出端口数 {sourceNode.OutputCount}");
                }
            }

            if (nodeMapForPorts.TryGetValue(edge.TargetNodeId, out var targetNode))
            {
                if (edge.TargetPortIndex >= targetNode.InputCount)
                {
                    errors.Add($"边 '{edge.EdgeId}' 的目标端口索引 {edge.TargetPortIndex} 超出节点 '{edge.TargetNodeId}' 的输入端口数 {targetNode.InputCount}");
                }
            }
        }

        // 7.6 Condition 节点 OutputCount >= 2（仅当有边使用非零端口时）
        foreach (var node in Nodes)
        {
            if (node.NodeType == WorkflowNodeType.Condition && node.OutputCount < 2)
            {
                // 检查是否有边引用了非零的 SourcePortIndex（即使用了多端口路由）
                var usesMultiPort = Edges.Any(e =>
                    e.SourceNodeId == node.NodeId && e.SourcePortIndex > 0);
                if (usesMultiPort)
                {
                    errors.Add($"条件节点 '{node.NodeId}' 的输出端口数必须 >= 2，当前为 {node.OutputCount}");
                }
            }
        }

        if (errors.Count > 0)
            return new DagValidationResult(errors, warnings);

        // 8. 孤立节点检测（没有任何入边或出边的节点，仅在节点数 > 1 时检测）
        if (Nodes.Count > 1)
        {
            var connectedNodes = new HashSet<string>();
            foreach (var edge in Edges)
            {
                connectedNodes.Add(edge.SourceNodeId);
                connectedNodes.Add(edge.TargetNodeId);
            }

            foreach (var node in Nodes)
            {
                if (!connectedNodes.Contains(node.NodeId))
                {
                    errors.Add($"工作流图包含未连接的孤立节点: {node.NodeId}");
                }
            }

            if (errors.Count > 0)
                return new DagValidationResult(errors, warnings);
        }

        // 9. 环路检测 — Kahn's algorithm (BFS topological sort)
        var inDegree = new Dictionary<string, int>();
        var adjacency = new Dictionary<string, List<string>>();

        foreach (var node in Nodes)
        {
            inDegree[node.NodeId] = 0;
            adjacency[node.NodeId] = [];
        }

        foreach (var edge in Edges)
        {
            adjacency[edge.SourceNodeId].Add(edge.TargetNodeId);
            inDegree[edge.TargetNodeId]++;
        }

        var queue = new Queue<string>();
        foreach (var (nodeId, degree) in inDegree)
        {
            if (degree == 0)
                queue.Enqueue(nodeId);
        }

        var visited = 0;
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            visited++;

            foreach (var neighbor in adjacency[current])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        if (visited != Nodes.Count)
        {
            var cycleNodes = inDegree
                .Where(kv => kv.Value > 0)
                .Select(kv => kv.Key);
            errors.Add($"工作流图包含环路，必须为有向无环图。涉及节点: {string.Join(", ", cycleNodes)}");
        }

        return new DagValidationResult(errors, warnings);
    }
}

/// <summary>
/// DAG 校验结果
/// </summary>
public sealed record DagValidationResult(List<string> Errors, List<string> Warnings)
{
    /// <summary>校验是否通过（无错误）</summary>
    public bool IsValid => Errors.Count == 0;
}
