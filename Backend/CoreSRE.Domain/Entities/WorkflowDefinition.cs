using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;

namespace CoreSRE.Domain.Entities;

/// <summary>
/// 工作流定义聚合根。以 DAG 图描述 Agent 和 Tool 的编排关系。
/// </summary>
public class WorkflowDefinition : BaseEntity
{
    /// <summary>工作流名称，全局唯一，最长 200 字符</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>工作流描述（可选，最长 2000 字符）</summary>
    public string? Description { get; private set; }

    /// <summary>工作流状态（Draft/Published），仅 Draft 可编辑/删除</summary>
    public WorkflowStatus Status { get; private set; }

    /// <summary>DAG 图定义（节点 + 边），JSONB 存储</summary>
    public WorkflowGraphVO Graph { get; private set; } = new();

    // EF Core requires parameterless constructor
    private WorkflowDefinition() { }

    /// <summary>
    /// 创建工作流定义（Draft 状态）。
    /// </summary>
    /// <param name="name">工作流名称（唯一，最长 200 字符）</param>
    /// <param name="description">描述（可选，最长 2000 字符）</param>
    /// <param name="graph">已校验的 DAG 图</param>
    /// <returns>Draft 状态的 WorkflowDefinition 实例</returns>
    public static WorkflowDefinition Create(string name, string? description, WorkflowGraphVO graph)
    {
        ValidateName(name);
        ValidateDescription(description);
        ArgumentNullException.ThrowIfNull(graph, nameof(graph));

        return new WorkflowDefinition
        {
            Name = name.Trim(),
            Description = description?.Trim(),
            Status = WorkflowStatus.Draft,
            Graph = graph
        };
    }

    /// <summary>
    /// 更新工作流定义。仅 Draft 状态可调用。
    /// </summary>
    /// <param name="name">新名称</param>
    /// <param name="description">新描述</param>
    /// <param name="graph">新的已校验 DAG 图</param>
    /// <exception cref="InvalidOperationException">当状态非 Draft 时</exception>
    public void Update(string name, string? description, WorkflowGraphVO graph)
    {
        GuardDraftStatus("编辑");
        ValidateName(name);
        ValidateDescription(description);
        ArgumentNullException.ThrowIfNull(graph, nameof(graph));

        Name = name.Trim();
        Description = description?.Trim();
        Graph = graph;
    }

    /// <summary>
    /// 发布工作流定义（Draft → Published）。
    /// Stub for SPEC-026.
    /// </summary>
    /// <exception cref="InvalidOperationException">当状态非 Draft 时</exception>
    public void Publish()
    {
        GuardDraftStatus("发布");
        Status = WorkflowStatus.Published;
    }

    /// <summary>
    /// 取消发布工作流定义（Published → Draft）。
    /// </summary>
    /// <exception cref="InvalidOperationException">当状态非 Published 时</exception>
    public void Unpublish()
    {
        if (Status != WorkflowStatus.Published)
            throw new InvalidOperationException("只有已发布的工作流才可取消发布。");
        Status = WorkflowStatus.Draft;
    }

    /// <summary>
    /// 检查工作流是否可删除（必须为 Draft）。
    /// </summary>
    /// <exception cref="InvalidOperationException">当状态非 Draft 时</exception>
    public void GuardCanDelete()
    {
        GuardDraftStatus("删除");
    }

    private void GuardDraftStatus(string operation)
    {
        if (Status != WorkflowStatus.Draft)
            throw new InvalidOperationException($"已发布的工作流不可{operation}，请先取消发布。");
    }

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        if (name.Trim().Length > 200)
            throw new ArgumentException("Name must not exceed 200 characters.", nameof(name));
    }

    private static void ValidateDescription(string? description)
    {
        if (description is not null && description.Trim().Length > 2000)
            throw new ArgumentException("Description must not exceed 2000 characters.", nameof(description));
    }
}
