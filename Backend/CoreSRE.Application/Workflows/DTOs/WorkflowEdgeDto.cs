namespace CoreSRE.Application.Workflows.DTOs;

/// <summary>
/// 工作流图边 DTO
/// </summary>
public record WorkflowEdgeDto
{
    public string EdgeId { get; init; } = string.Empty;
    public string SourceNodeId { get; init; } = string.Empty;
    public string TargetNodeId { get; init; } = string.Empty;
    public string EdgeType { get; init; } = string.Empty;
    public string? Condition { get; init; }
}
