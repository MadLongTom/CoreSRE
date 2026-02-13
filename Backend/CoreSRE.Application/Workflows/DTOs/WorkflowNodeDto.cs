namespace CoreSRE.Application.Workflows.DTOs;

/// <summary>
/// 工作流图节点 DTO
/// </summary>
public record WorkflowNodeDto
{
    public string NodeId { get; init; } = string.Empty;
    public string NodeType { get; init; } = string.Empty;
    public Guid? ReferenceId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? Config { get; init; }
    public int InputCount { get; init; } = 1;
    public int OutputCount { get; init; } = 1;
}
