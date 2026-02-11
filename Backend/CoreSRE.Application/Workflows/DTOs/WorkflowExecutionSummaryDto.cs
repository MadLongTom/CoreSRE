namespace CoreSRE.Application.Workflows.DTOs;

/// <summary>
/// 工作流执行摘要 DTO（列表视图）
/// </summary>
public record WorkflowExecutionSummaryDto
{
    public Guid Id { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public DateTime CreatedAt { get; init; }
}
