using CoreSRE.Domain.ValueObjects;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// 执行上下文 — 管理执行栈、等待队列、运行结果和边映射。
/// 执行引擎的核心状态容器。
/// </summary>
internal sealed class ExecutionContext
{
    /// <summary>执行栈（LIFO — 后进先出，实现 DFS 图遍历）</summary>
    private readonly Stack<NodeExecutionTask> _stack = new();

    /// <summary>等待队列（多输入节点等待所有端口数据到齐）</summary>
    private readonly Dictionary<string, WaitingNodeData> _waitingNodes = new();

    /// <summary>节点运行结果（每个节点可多次执行）</summary>
    private readonly Dictionary<string, List<NodeRunResult>> _runResults = new();

    /// <summary>节点执行计数（用于无限循环保护）</summary>
    private readonly Dictionary<string, int> _executionCounts = new();

    /// <summary>出边映射：源节点 ID → 出边列表</summary>
    public IReadOnlyDictionary<string, List<WorkflowEdgeVO>> OutgoingEdges { get; }

    /// <summary>入边映射：目标节点 ID → 入边列表</summary>
    public IReadOnlyDictionary<string, List<WorkflowEdgeVO>> IncomingEdges { get; }

    /// <summary>节点映射：节点 ID → 节点</summary>
    public IReadOnlyDictionary<string, WorkflowNodeVO> NodeMap { get; }

    /// <summary>每个节点的最大执行次数（无限循环保护）</summary>
    public int MaxExecutionsPerNode { get; init; } = 100;

    public ExecutionContext(WorkflowGraphVO graph)
    {
        NodeMap = graph.Nodes.ToDictionary(n => n.NodeId);

        var outgoing = new Dictionary<string, List<WorkflowEdgeVO>>();
        var incoming = new Dictionary<string, List<WorkflowEdgeVO>>();

        foreach (var node in graph.Nodes)
        {
            outgoing[node.NodeId] = [];
            incoming[node.NodeId] = [];
        }

        foreach (var edge in graph.Edges)
        {
            outgoing[edge.SourceNodeId].Add(edge);
            incoming[edge.TargetNodeId].Add(edge);
        }

        OutgoingEdges = outgoing;
        IncomingEdges = incoming;
    }

    /// <summary>将任务压入执行栈</summary>
    public void Push(NodeExecutionTask task)
    {
        _stack.Push(task);
    }

    /// <summary>从执行栈弹出任务</summary>
    public NodeExecutionTask Pop()
    {
        return _stack.Pop();
    }

    /// <summary>执行栈是否为空</summary>
    public bool IsStackEmpty => _stack.Count == 0;

    /// <summary>执行栈当前大小</summary>
    public int StackCount => _stack.Count;

    /// <summary>
    /// 将节点添加到等待队列，接收指定端口的数据。
    /// </summary>
    public void AddToWaiting(string nodeId, int portIndex, PortDataVO data, int expectedPortCount)
    {
        if (!_waitingNodes.TryGetValue(nodeId, out var waiting))
        {
            waiting = new WaitingNodeData(expectedPortCount);
            _waitingNodes[nodeId] = waiting;
        }
        waiting.ReceivePort(portIndex, data);
    }

    /// <summary>
    /// 尝试将等待节点提升到执行栈。如果所有端口数据到齐，构建 NodeInputData 并返回 true。
    /// </summary>
    public bool TryPromote(string nodeId, out NodeInputData? inputData)
    {
        inputData = null;
        if (!_waitingNodes.TryGetValue(nodeId, out var waiting) || !waiting.IsComplete)
            return false;

        inputData = waiting.BuildInputData();
        _waitingNodes.Remove(nodeId);
        return true;
    }

    /// <summary>
    /// 检查节点是否在等待队列中。
    /// </summary>
    public bool IsWaiting(string nodeId) => _waitingNodes.ContainsKey(nodeId);

    /// <summary>
    /// 获取所有等待中的节点 ID。
    /// </summary>
    public IEnumerable<string> GetWaitingNodeIds() => _waitingNodes.Keys;

    /// <summary>
    /// 记录节点运行结果。
    /// </summary>
    public void RecordResult(string nodeId, NodeRunResult result)
    {
        if (!_runResults.TryGetValue(nodeId, out var results))
        {
            results = [];
            _runResults[nodeId] = results;
        }
        results.Add(result);
    }

    /// <summary>
    /// 获取节点的所有运行结果。
    /// </summary>
    public IReadOnlyList<NodeRunResult> GetResults(string nodeId)
    {
        return _runResults.TryGetValue(nodeId, out var results)
            ? results
            : [];
    }

    /// <summary>
    /// 增加节点执行计数并检查是否超过限制。
    /// </summary>
    /// <returns>true 如果在限制内，false 如果超过限制</returns>
    public bool IncrementAndCheckLimit(string nodeId)
    {
        if (!_executionCounts.TryGetValue(nodeId, out var count))
            count = 0;

        count++;
        _executionCounts[nodeId] = count;

        return count <= MaxExecutionsPerNode;
    }

    /// <summary>
    /// 获取节点的当前执行计数。
    /// </summary>
    public int GetExecutionCount(string nodeId)
    {
        return _executionCounts.TryGetValue(nodeId, out var count) ? count : 0;
    }
}
