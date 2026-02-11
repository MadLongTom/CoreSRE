namespace CoreSRE.Application.Workflows.DTOs;

/// <summary>
/// 工作流定义摘要 DTO（列表视图，含节点数量）
/// </summary>
public record WorkflowSummaryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Status { get; init; } = string.Empty;
    public int NodeCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
