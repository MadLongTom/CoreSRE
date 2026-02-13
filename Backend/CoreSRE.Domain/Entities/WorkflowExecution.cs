using System.Text.Json;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;

namespace CoreSRE.Domain.Entities;

/// <summary>
/// 工作流的一次执行实例（聚合根）。
/// </summary>
public class WorkflowExecution : BaseEntity
{
    /// <summary>关联的工作流定义 ID</summary>
    public Guid WorkflowDefinitionId { get; private set; }

    /// <summary>执行状态</summary>
    public ExecutionStatus Status { get; private set; }

    /// <summary>执行输入数据</summary>
    public JsonElement Input { get; private set; }

    /// <summary>执行输出数据（完成后填充）</summary>
    public JsonElement? Output { get; private set; }

    /// <summary>实际开始执行时间</summary>
    public DateTime? StartedAt { get; private set; }

    /// <summary>执行完成时间</summary>
    public DateTime? CompletedAt { get; private set; }

    /// <summary>失败时的错误信息</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>OpenTelemetry Trace ID（占位字段）</summary>
    public string? TraceId { get; private set; }

    /// <summary>启动时的 DAG 图快照</summary>
    public WorkflowGraphVO GraphSnapshot { get; private set; } = new();

    /// <summary>各节点的执行记录</summary>
    public List<NodeExecutionVO> NodeExecutions { get; private set; } = [];

    // EF Core requires parameterless constructor
    private WorkflowExecution() { }

    /// <summary>
    /// 创建工作流执行实例（Pending 状态）。
    /// 从 graphSnapshot.Nodes 生成所有 NodeExecutionVO（均为 Pending 状态）。
    /// </summary>
    public static WorkflowExecution Create(Guid workflowDefinitionId, JsonElement input, WorkflowGraphVO graphSnapshot)
    {
        ArgumentNullException.ThrowIfNull(graphSnapshot, nameof(graphSnapshot));

        return new WorkflowExecution
        {
            WorkflowDefinitionId = workflowDefinitionId,
            Status = ExecutionStatus.Pending,
            Input = input,
            GraphSnapshot = graphSnapshot,
            NodeExecutions = graphSnapshot.Nodes.Select(n => new NodeExecutionVO
            {
                NodeId = n.NodeId,
                Status = NodeExecutionStatus.Pending
            }).ToList()
        };
    }

    /// <summary>
    /// 开始执行（Pending → Running）。
    /// </summary>
    public void Start()
    {
        GuardStatus(ExecutionStatus.Pending, "开始执行");
        Status = ExecutionStatus.Running;
        StartedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 标记节点开始执行，并记录节点输入数据。
    /// </summary>
    public void StartNode(string nodeId, string? input)
    {
        var node = FindNode(nodeId);
        UpdateNode(nodeId, node with
        {
            Status = NodeExecutionStatus.Running,
            Input = input,
            StartedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// 标记节点执行完成。
    /// </summary>
    public void CompleteNode(string nodeId, string? output)
    {
        var node = FindNode(nodeId);
        UpdateNode(nodeId, node with
        {
            Status = NodeExecutionStatus.Completed,
            Output = output,
            CompletedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// 标记节点执行失败。
    /// </summary>
    public void FailNode(string nodeId, string errorMessage)
    {
        var node = FindNode(nodeId);
        UpdateNode(nodeId, node with
        {
            Status = NodeExecutionStatus.Failed,
            ErrorMessage = errorMessage,
            CompletedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// 标记节点被跳过（条件分支未命中）。
    /// </summary>
    public void SkipNode(string nodeId)
    {
        var node = FindNode(nodeId);
        UpdateNode(nodeId, node with
        {
            Status = NodeExecutionStatus.Skipped
        });
    }

    /// <summary>
    /// 工作流执行完成（Running → Completed）。
    /// </summary>
    public void Complete(JsonElement output)
    {
        GuardStatus(ExecutionStatus.Running, "完成");
        Status = ExecutionStatus.Completed;
        Output = output;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 工作流执行失败（Running → Failed）。
    /// </summary>
    public void Fail(string errorMessage)
    {
        GuardStatus(ExecutionStatus.Running, "标记失败");
        Status = ExecutionStatus.Failed;
        ErrorMessage = errorMessage;
        CompletedAt = DateTime.UtcNow;
    }

    private NodeExecutionVO FindNode(string nodeId)
    {
        var node = NodeExecutions.FirstOrDefault(n => n.NodeId == nodeId);
        if (node is null)
            throw new InvalidOperationException($"未找到节点: {nodeId}");
        return node;
    }

    private void UpdateNode(string nodeId, NodeExecutionVO updated)
    {
        var index = NodeExecutions.FindIndex(n => n.NodeId == nodeId);
        NodeExecutions[index] = updated;
    }

    private void GuardStatus(ExecutionStatus expected, string operation)
    {
        if (Status != expected)
            throw new InvalidOperationException(
                $"无法{operation}：当前状态为 {Status}，预期 {expected}。");
    }
}
