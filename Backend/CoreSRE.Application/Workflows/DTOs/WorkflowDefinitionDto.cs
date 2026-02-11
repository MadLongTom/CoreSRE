namespace CoreSRE.Application.Workflows.DTOs;

/// <summary>
/// 工作流定义完整详情 DTO（含 DAG 图）
/// </summary>
public record WorkflowDefinitionDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Status { get; init; } = string.Empty;
    public WorkflowGraphDto Graph { get; init; } = new();
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
